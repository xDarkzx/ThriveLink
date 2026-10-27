<#
.SYNOPSIS
  Builds the claims triage image in Azure Container Registry (no local Docker needed), deploys the scheduled
  Container Apps Job, grants its identity SQL access, and runs it once to verify.
  AI review turns on automatically if Key Vault holds an "Anthropic--ApiKey" secret.

.EXAMPLE
  ./infra/claims.ps1
  az keyvault secret set --vault-name <kv> -n Anthropic--ApiKey --value <key>; ./infra/claims.ps1   # enable AI
#>
param([string]$Env = 'dev')

$ErrorActionPreference = 'Continue'
$repo = Split-Path $PSScriptRoot -Parent
$rg = "rg-thrivelink-$Env"
function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

$sqlServer = az sql server list -g $rg --query "[0].fullyQualifiedDomainName" -o tsv
$kv = az keyvault list -g $rg --query "[0].name" -o tsv
$workspace = az monitor log-analytics workspace list -g $rg --query "[0].name" -o tsv
$hasAiKey = (az keyvault secret list --vault-name $kv --query "[?name=='Anthropic--ApiKey'] | length(@)" -o tsv) -eq '1'
$secretName = if ($hasAiKey) { 'Anthropic--ApiKey' } else { '' }
$me = az ad signed-in-user show --query id -o tsv
$common = @("env=$Env", "workspaceName=$workspace", "keyVaultName=$kv", "sqlServerFqdn=$sqlServer", 'sqlDatabaseName=ThriveLink', "anthropicSecretName=$secretName", "adminObjectId=$me")

Step 'Registry, identity and Container Apps environment'
$out = az deployment group create -g $rg -n "claims-base-$(Get-Date -Format yyyyMMdd-HHmmss)" -f "$PSScriptRoot/claims.bicep" `
    -p @common deployJob=false --query properties.outputs -o json
if ($LASTEXITCODE -ne 0) { throw 'claims base deployment failed' }
$o = $out | ConvertFrom-Json
$acr = $o.registryName.value

Step "Build image locally and push to ACR ($acr)"
# ACR Tasks (az acr build) are blocked on free-trial subscriptions, so build with local Docker and push.
$server = $o.registryServer.value
$tag = "claims-triage:$(Get-Date -Format yyyyMMdd-HHmmss)"
docker build -f "$repo/src/ThriveLink.ClaimsTriage/Dockerfile" -t "$server/$tag" $repo
if ($LASTEXITCODE -ne 0) { throw 'image build failed' }
# The AcrPush role can take a minute to propagate on first deploy.
for ($i = 1; $i -le 6; $i++) {
    az acr login -n $acr -o none 2>$null
    docker push "$server/$tag"
    if ($LASTEXITCODE -eq 0) { break }
    Write-Host "  push not permitted yet ($i), waiting for AcrPush role..."; Start-Sleep 20
}
if ($LASTEXITCODE -ne 0) { throw 'image push failed' }
Write-Host "  pushed $server/$tag"

Step 'SQL access for the job identity'
$identity = $o.identityName.value
# Explicit try/catch: with ErrorActionPreference=Continue a failed ExecuteNonQuery would not stop the script.
# A just-created identity can take a minute to become resolvable in Entra, so retry before giving up.
for ($attempt = 1; ; $attempt++) {
    try {
        $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
        $conn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$sqlServer,1433;Database=ThriveLink;Encrypt=True;Connect Timeout=90")
        $conn.AccessToken = $token; $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$identity') CREATE USER [$identity] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader ADD MEMBER [$identity]; ALTER ROLE db_datawriter ADD MEMBER [$identity];"
        [void]$cmd.ExecuteNonQuery(); $conn.Close()
        break
    }
    catch {
        if ($attempt -ge 6) { throw "SQL access for $identity failed: $($_.Exception.Message)" }
        Write-Host "  attempt $attempt failed ($($_.Exception.Message)); retrying in 30s..."; Start-Sleep 30
    }
}
Write-Host "  $identity -> db_datareader, db_datawriter"

Step "Scheduled job (every 5 minutes; AI review: $(if ($hasAiKey) { 'on' } else { 'off - rules only' }))"
$out = az deployment group create -g $rg -n "claims-job-$(Get-Date -Format yyyyMMdd-HHmmss)" -f "$PSScriptRoot/claims.bicep" `
    -p @common deployJob=true image=$tag --query properties.outputs -o json
if ($LASTEXITCODE -ne 0) { throw 'job deployment failed' }
$job = ($out | ConvertFrom-Json).jobName.value

Step 'Run once now and wait'
$execution = az containerapp job start -g $rg -n $job --query name -o tsv
do {
    Start-Sleep 15
    $status = az containerapp job execution show -g $rg -n $job --job-execution-name $execution --query properties.status -o tsv
    Write-Host "  $execution : $status"
} while ($status -in 'Running', 'Processing', 'Unknown', '')   # Unknown is reported briefly while the replica starts
if ($status -ne 'Succeeded') { throw "Job execution $execution ended $status" }
Write-Host "`nDone: $job" -ForegroundColor Green
