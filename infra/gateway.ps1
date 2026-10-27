<#
.SYNOPSIS
  Creates (or removes) the Application Gateway WAF v2 in front of the web app.
  It costs roughly US$10/day while it exists, so create it for a demo and remove it afterwards.

.EXAMPLE
  ./infra/gateway.ps1            # create and test, including a blocked SQL-injection request
  ./infra/gateway.ps1 -Remove    # delete the gateway, WAF policy, public IP and VNet
#>
param([string]$Env = 'dev', [switch]$Remove)

$ErrorActionPreference = 'Continue'
$rg = "rg-thrivelink-$Env"

if ($Remove) {
    foreach ($type in 'Microsoft.Network/applicationGateways', 'Microsoft.Network/ApplicationGatewayWebApplicationFirewallPolicies', 'Microsoft.Network/publicIPAddresses', 'Microsoft.Network/virtualNetworks') {
        foreach ($id in (az resource list -g $rg --resource-type $type --query "[].id" -o tsv)) {
            Write-Host "deleting $id"
            az resource delete --ids $id -o none
        }
    }
    Write-Host 'Gateway resources removed.' -ForegroundColor Green
    return
}

$web = az webapp list -g $rg --query "[?starts_with(name, 'app-')].name | [0]" -o tsv
Write-Host '==> Application Gateway WAF v2 (takes ~5-10 minutes)' -ForegroundColor Cyan
$url = az deployment group create -g $rg -n "gateway-$(Get-Date -Format yyyyMMdd-HHmmss)" -f "$PSScriptRoot/gateway.bicep" `
    -p env=$Env webAppName=$web --query properties.outputs.url.value -o tsv
if ($LASTEXITCODE -ne 0) { throw 'gateway deployment failed' }

Write-Host '==> Tests through the gateway' -ForegroundColor Cyan
for ($i = 1; $i -le 20; $i++) {
    try { $h = Invoke-RestMethod "$url/health" -TimeoutSec 30; break } catch { Start-Sleep 15 }
}
Write-Host "  GET /health -> $($h.status)"
try {
    Invoke-WebRequest "$url/api/members/TL100001'%20OR%201=1--" -UseBasicParsing -TimeoutSec 30 | Out-Null
    Write-Host '  SQL injection probe was NOT blocked' -ForegroundColor Red
} catch { Write-Host "  SQL injection probe -> HTTP $($_.Exception.Response.StatusCode.value__) (403 = blocked by WAF)" }
Write-Host "`nGateway: $url   (remember: ./infra/gateway.ps1 -Remove)" -ForegroundColor Green
