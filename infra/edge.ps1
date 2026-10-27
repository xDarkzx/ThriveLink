<#
.SYNOPSIS
  Deploys API Management (edge.bicep) after the main platform, then calls the partner API end to end.
  Stores the Functions host key in Key Vault first, so APIM can read it via its managed identity.
#>
param([string]$Env = 'dev')

$ErrorActionPreference = 'Continue'
$rg = "rg-thrivelink-$Env"
function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Exec([scriptblock]$Command, [string]$What) {
    & $Command | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)" }
}

$web = az webapp list -g $rg --query "[?starts_with(name, 'app-')].name | [0]" -o tsv
$func = az functionapp list -g $rg --query "[?starts_with(name, 'func-')].name | [0]" -o tsv
$kv = az keyvault list -g $rg --query "[0].name" -o tsv
$email = az account show --query user.name -o tsv

Step 'Functions host key -> Key Vault (Functions--HostKey)'
$hostKey = az functionapp keys list -g $rg -n $func --query "functionKeys.default" -o tsv
if (-not $hostKey) { throw 'Could not read the Functions host key (is the function app deployed?)' }
Exec { az keyvault secret set --vault-name $kv -n Functions--HostKey --value $hostKey -o none } 'store host key'

Step 'API Management (Consumption) - first creation takes a few minutes'
# APIM's Key Vault role is granted in the same deployment that reads the secrets. On a fresh environment the
# role can take a few minutes to propagate, so the first attempt may fail on the named values; retry.
for ($attempt = 1; $attempt -le 3; $attempt++) {
    $out = az deployment group create -g $rg -n "edge-$(Get-Date -Format yyyyMMdd-HHmmss)" -f "$PSScriptRoot/edge.bicep" `
        -p env=$Env publisherEmail=$email webAppName=$web functionAppName=$func keyVaultName=$kv `
        --query properties.outputs -o json
    if ($LASTEXITCODE -eq 0) { break }
    if ($attempt -lt 3) { Write-Host "  attempt $attempt failed (likely Key Vault role propagation); retrying in 2 minutes..."; Start-Sleep 120 }
}
if ($LASTEXITCODE -ne 0) { throw 'APIM deployment failed' }
$outputs = $out | ConvertFrom-Json
$apim = $outputs.apimName.value
$gateway = $outputs.gatewayUrl.value
Write-Host "  $gateway"

Step 'Partner API smoke test through APIM'
$sub = az account show --query id -o tsv
$key = az rest --method post --url "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.ApiManagement/service/$apim/subscriptions/demo-partner/listSecrets?api-version=2023-09-01-preview" --query primaryKey -o tsv
$headers = @{ 'Ocp-Apim-Subscription-Key' = $key }
$yesterday = (Get-Date).ToUniversalTime().AddDays(-1).ToString('yyyy-MM-dd')

try {
    Invoke-RestMethod "$gateway/partner/activity" -Method Post -ContentType 'application/json' `
        -Body (@{ memberNumber = 'TL100001'; date = $yesterday; steps = 9000 } | ConvertTo-Json) -TimeoutSec 60 | Out-Null
    Write-Host '  WITHOUT subscription key: unexpectedly accepted' -ForegroundColor Red
} catch { Write-Host "  without subscription key -> HTTP $($_.Exception.Response.StatusCode.value__) (expected 401)" }

$activity = Invoke-RestMethod "$gateway/partner/activity" -Method Post -Headers $headers -ContentType 'application/json' -TimeoutSec 90 `
    -Body (@{ memberNumber = 'TL100001'; date = $yesterday; steps = 9000; activeMinutes = 40; averageHeartRate = 130; provider = 'Garmin' } | ConvertTo-Json)
Write-Host "  POST /partner/activity -> $($activity.outcome), $($activity.total) points"

$sync = Invoke-RestMethod "$gateway/partner/wearables/sync" -Method Post -Headers $headers -ContentType 'application/json' -TimeoutSec 120 `
    -Body (@{ memberNumber = 'TL100002'; provider = 'Fitbit'; days = @(@{ date = $yesterday; steps = 12000; activeMinutes = 65; averageHeartRate = 138 }) } | ConvertTo-Json -Depth 4)
Write-Host "  POST /partner/wearables/sync -> $(($sync.days | ForEach-Object { "$($_.date) $($_.outcome) $($_.points)pts" }) -join '; ')"

$member = Invoke-RestMethod "$gateway/partner/members/TL100002" -Headers $headers -TimeoutSec 60
Write-Host "  GET /partner/members/TL100002 -> $($member.name), $($member.status), $($member.pointsThisYear) points"
Write-Host "`nDone. Partner API: $gateway/partner" -ForegroundColor Green
