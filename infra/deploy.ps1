<#
.SYNOPSIS
  Deploys ThriveLink to Azure: infrastructure (Bicep), database migrations, SQL access for managed identities,
  Key Vault secret, the web app, the Functions, the Data Factory trigger, and optional demo data.
  Safe to re-run: every step is idempotent.

.EXAMPLE
  ./infra/deploy.ps1 -Env dev -Seed
#>
param(
    [string]$Env = 'dev',
    [string]$Location = 'australiaeast',
    [string]$Sku = 'F1',
    [switch]$Seed,
    [switch]$SkipApp,
    [switch]$TestPipeline
)

# Native tools (az, dotnet, npm) write warnings to stderr; judge them by exit code, not by stderr output.
$ErrorActionPreference = 'Continue'
$repo = Split-Path $PSScriptRoot -Parent
$rg = "rg-thrivelink-$Env"

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

function Exec([scriptblock]$Command, [string]$What) {
    # Out-Host keeps tool output on screen and out of the caller's return value.
    & $Command | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)" }
}

function Invoke-Sql([string]$Server, [string]$Database, [string]$Sql) {
    # Signs in to Azure SQL with the deployer's Entra token (no SQL passwords exist on this server).
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    $conn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$Server,1433;Database=$Database;Encrypt=True;Connect Timeout=90")
    $conn.AccessToken = $token
    $conn.Open()
    try {
        # Split T-SQL batches on GO lines, as sqlcmd does.
        foreach ($batch in ($Sql -split '(?m)^\s*GO\s*$')) {
            if ([string]::IsNullOrWhiteSpace($batch)) { continue }
            $cmd = $conn.CreateCommand()
            $cmd.CommandText = $batch
            $cmd.CommandTimeout = 300
            [void]$cmd.ExecuteNonQuery()
        }
    }
    finally { $conn.Close() }
}

function Grant-SqlAccess([string]$Server, [string]$Database, [string]$Identity, [string]$Extra = '') {
    Invoke-Sql $Server $Database @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$Identity')
    CREATE USER [$Identity] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [$Identity];
ALTER ROLE db_datawriter ADD MEMBER [$Identity];
$Extra
"@
    Write-Host "  $Identity -> db_datareader, db_datawriter $(if ($Extra) { '+ ' + $Extra.Trim() })"
}

function Publish-Zip([string]$Project, [string]$Name) {
    $out = Join-Path ([IO.Path]::GetTempPath()) $Name
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    Exec { dotnet publish $Project -c Release -o $out --nologo -v quiet } "publish $Name"
    $zip = "$out.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    New-LinuxZip $out $zip
    return $zip
}

function New-LinuxZip([string]$Source, [string]$Zip) {
    # Windows PowerShell's Compress-Archive writes entries with backslashes, which Linux App Service
    # treats as literal file names. Write the zip ourselves with forward-slash entry paths.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $root = (Resolve-Path $Source).Path.TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::Open($Zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $Source -Recurse -File) {
            $entry = $file.FullName.Substring($root.Length).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $archive.Dispose() }
}

Step "Resource group $rg"
$me = az ad signed-in-user show --query "{id:id, upn:userPrincipalName}" -o json | ConvertFrom-Json
$ip = (Invoke-RestMethod https://api.ipify.org).Trim()
Exec { az group create -n $rg -l $Location --tags app=thrivelink env=$Env -o none } 'resource group'

Step "Infrastructure (Bicep)"
$outputsJson = az deployment group create -g $rg -n "platform-$(Get-Date -Format yyyyMMdd-HHmmss)" `
    -f "$PSScriptRoot/main.bicep" `
    -p env=$Env adminObjectId=$($me.id) adminLogin=$($me.upn) deployerIp=$ip appServiceSku=$Sku `
    --query properties.outputs -o json
if ($LASTEXITCODE -ne 0) { throw 'Bicep deployment failed' }
$outputs = $outputsJson | ConvertFrom-Json
$app = $outputs.webAppName.value
$func = $outputs.functionAppName.value
$adf = $outputs.dataFactoryName.value
$sqlServer = $outputs.sqlServerFqdn.value
$sqlDb = $outputs.sqlDatabaseName.value
$kv = $outputs.keyVaultName.value
$storage = $outputs.storageAccountName.value
Write-Host "  app: $app`n  func: $func`n  adf: $adf`n  sql: $sqlServer/$sqlDb`n  kv:  $kv"

Step "Key Vault secret Ingest--ApiKey"
$exists = az keyvault secret list --vault-name $kv --query "[?name=='Ingest--ApiKey'] | length(@)" -o tsv 2>$null
if ($exists -ne '1') {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $key = [Convert]::ToBase64String($bytes) -replace '[+/=]', ''
    # RBAC role assignments can take a minute to propagate; retry until we can write.
    for ($i = 1; $i -le 10; $i++) {
        az keyvault secret set --vault-name $kv -n Ingest--ApiKey --value $key -o none 2>$null
        if ($LASTEXITCODE -eq 0) { break }
        Write-Host "  waiting for Key Vault permissions ($i)..."; Start-Sleep 15
    }
    if ($LASTEXITCODE -ne 0) { throw 'Could not write Key Vault secret' }
    Write-Host '  created'
} else { Write-Host '  already present' }

Step 'Database migrations (idempotent script)'
$migrationScript = Join-Path ([IO.Path]::GetTempPath()) 'thrivelink-migrate.sql'
Exec { dotnet ef migrations script --idempotent -p "$repo/src/ThriveLink.Data" -s "$repo/src/ThriveLink.Api" -o $migrationScript } 'migration script'
Invoke-Sql $sqlServer $sqlDb (Get-Content $migrationScript -Raw)
Write-Host '  applied'

Step 'SQL access for managed identities (least privilege: no schema changes)'
Grant-SqlAccess $sqlServer $sqlDb $app
Grant-SqlAccess $sqlServer $sqlDb $func
Grant-SqlAccess $sqlServer $sqlDb $adf "GRANT EXECUTE ON dbo.usp_ApplyHealthChecks TO [$adf];"

if ($Seed) {
    Step 'Demo data (synthetic)'
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:ConnectionStrings__Sql = "Server=tcp:$sqlServer,1433;Database=$sqlDb;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=90"
    $env:Database__MigrateOnStartup = 'false'
    try {
        # --no-launch-profile: launchSettings.json would otherwise force the Development environment.
        Exec { dotnet run --project "$repo/src/ThriveLink.Api" -c Release --no-launch-profile -- --seed-and-exit } 'seeding'
    }
    finally { Remove-Item Env:ConnectionStrings__Sql, Env:Database__MigrateOnStartup, Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue }
    Write-Host '  seeded (skipped automatically if members already exist)'
}

if (-not $SkipApp) {
    Step 'Build and deploy the web app'
    Push-Location "$repo/web"
    try { Exec { npm ci --silent } 'npm ci'; Exec { npm run build --silent } 'web build' } finally { Pop-Location }
    $zip = Publish-Zip "$repo/src/ThriveLink.Api" 'thrivelink-web-publish'
    Exec { az webapp deploy -g $rg -n $app --src-path $zip --type zip -o none } 'web app deploy'
    Write-Host '  deployed'

    Step 'Build and deploy the Functions'
    $funcZip = Publish-Zip "$repo/src/ThriveLink.Functions" 'thrivelink-func-publish'
    Exec { az functionapp deployment source config-zip -g $rg -n $func --src $funcZip -o none } 'functions deploy'
    Write-Host '  deployed'
}

Step 'Data Factory nightly trigger'
az extension add --name datafactory --only-show-errors 2>$null
$state = az datafactory trigger show -g $rg --factory-name $adf -n Nightly --query properties.runtimeState -o tsv
if ($state -ne 'Started') { Exec { az datafactory trigger start -g $rg --factory-name $adf -n Nightly -o none } 'start trigger' }
Write-Host '  Nightly trigger started'

if ($TestPipeline) {
    Step 'Data Factory test run with a sample partner file'
    $sample = Join-Path ([IO.Path]::GetTempPath()) "healthchecks-$(Get-Date -Format yyyyMMddHHmmss).csv"
    $d = (Get-Date).ToUniversalTime().AddDays(-1).ToString('yyyy-MM-dd')
    @"
MemberNumber,CheckDate,Measure,Partner
TL100003,$d,BMI,Partner Pharmacy Hamilton
TL100003,$d,BloodPressure,Partner Pharmacy Hamilton
TL100004,$d,Cholesterol,Partner GP Tauranga
TL999999,$d,BMI,Partner Pharmacy Hamilton
"@ | Set-Content -Path $sample -Encoding ascii
    Exec { az storage blob upload --account-name $storage --auth-mode login -c partner-files -n "incoming/$(Split-Path $sample -Leaf)" -f $sample --overwrite -o none } 'upload sample'
    $runId = az datafactory pipeline create-run -g $rg --factory-name $adf -n ImportPartnerHealthChecks --query runId -o tsv
    do {
        Start-Sleep 15
        $status = az datafactory pipeline-run show -g $rg --factory-name $adf --run-id $runId --query status -o tsv
        Write-Host "  pipeline: $status"
    } while ($status -in 'Queued', 'InProgress')
    if ($status -ne 'Succeeded') { throw "Pipeline run $runId ended $status" }
}

Step 'Monitoring alerts (availability test, 5xx, stale devices, Data Factory failures)'
$alertEmail = az account show --query user.name -o tsv
Exec { az deployment group create -g $rg -n "alerts-$(Get-Date -Format yyyyMMdd-HHmmss)" -f "$PSScriptRoot/alerts.bicep" `
        -p env=$Env alertEmail=$alertEmail webAppName=$app dataFactoryName=$adf `
        appInsightsName=$($outputs.appInsightsName.value) workspaceName=$($outputs.logAnalyticsWorkspaceName.value) -o none } 'alerts'
Write-Host "  alerts go to $alertEmail"

Step 'Smoke test'
$url = $outputs.webAppUrl.value
for ($i = 1; $i -le 12; $i++) {
    try { $h = Invoke-RestMethod "$url/health" -TimeoutSec 60; break } catch { Write-Host "  waiting for app ($i)..."; Start-Sleep 10 }
}
$stats = Invoke-RestMethod "$url/api/stats" -TimeoutSec 90
Write-Host "  health: $($h.status)   members: $($stats.members.total)"
Write-Host "`nDone: $url" -ForegroundColor Green
