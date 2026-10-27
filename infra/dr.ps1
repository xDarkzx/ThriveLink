<#
.SYNOPSIS
  Azure Site Recovery (Azure-to-Azure) for the legacy policy admin VM: Australia East -> Australia Southeast.

.DESCRIPTION
  -Setup     deploy dr.bicep (VM, cache storage, vault, recovery + test networks) and enable replication
  -Status    show replication health
  -Drill     run a test failover into the isolated test network, verify, then clean it up
  -Remove    disable replication and delete the DR resource groups (stops all DR charges)

.EXAMPLE
  ./infra/dr.ps1 -Setup
  ./infra/dr.ps1 -Status      # initial replication takes ~30-60 minutes
  ./infra/dr.ps1 -Drill
#>
param(
    [string]$Env = 'dev',
    [switch]$Setup,
    [switch]$Status,
    [switch]$Drill,
    [switch]$Remove
)

$ErrorActionPreference = 'Continue'
$api = 'api-version=2024-04-01'
$sourceRg = "rg-thrivelink-legacy-$Env"
$targetRg = "rg-thrivelink-dr-target-$Env"
$primaryLocation = 'australiaeast'
$recoveryLocation = 'australiasoutheast'
$sub = az account show --query id -o tsv

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

function Arm([string]$Method, [string]$Path, $Body) {
    $url = "https://management.azure.com$Path`?$api"
    if ($Body) {
        $file = New-TemporaryFile
        $Body | ConvertTo-Json -Depth 20 | Set-Content $file -Encoding utf8
        $result = az rest --method $Method --url $url --body "@$file" -o json 2>&1
        Remove-Item $file
    } else {
        $result = az rest --method $Method --url $url -o json 2>&1
    }
    if ($LASTEXITCODE -ne 0) { throw "ARM $Method $Path failed: $result" }
    if ($result) { return ($result | Out-String | ConvertFrom-Json) }
}

function Get-Arm([string]$Path) {
    $r = az rest --method get --url "https://management.azure.com$Path`?$api" -o json 2>$null
    if ($LASTEXITCODE -eq 0 -and $r) { return ($r | Out-String | ConvertFrom-Json) }
    return $null
}

function Wait-For([string]$What, [scriptblock]$Condition, [int]$TimeoutMinutes = 15) {
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while (-not (& $Condition)) {
        if ((Get-Date) -gt $deadline) { throw "Timed out waiting for $What" }
        Write-Host "  waiting for $What..."; Start-Sleep 20
    }
}

$vaultPath = $null
function Resolve-Vault {
    $name = az resource list -g $sourceRg --resource-type Microsoft.RecoveryServices/vaults --query "[0].name" -o tsv
    if (-not $name) { throw "No Recovery Services vault in $sourceRg (run -Setup first)" }
    return "/subscriptions/$sub/resourceGroups/$sourceRg/providers/Microsoft.RecoveryServices/vaults/$name"
}

$pf = 'fabric-australiaeast'; $rf = 'fabric-australiasoutheast'
$pc = 'container-australiaeast'; $rc = 'container-australiasoutheast'
$policy = 'policy-24h'; $mapping = 'mapping-ae-to-ase'; $netMapping = 'netmap-ae-to-ase'

if ($Setup) {
    Step "Resource groups ($sourceRg in $primaryLocation, $targetRg in $recoveryLocation)"
    az group create -n $sourceRg -l $primaryLocation --tags app=thrivelink env=$Env -o none
    az group create -n $targetRg -l $recoveryLocation --tags app=thrivelink env=$Env -o none

    Step 'SSH key for the VM'
    $keyPath = Join-Path $HOME '.ssh/thrivelink_legacy'
    if (-not (Test-Path "$keyPath.pub")) { ssh-keygen -t ed25519 -N '""' -f $keyPath -C thrivelink-legacy -q }
    $pub = (Get-Content "$keyPath.pub" -Raw).Trim()

    Step 'Legacy VM, cache storage, vault and networks (dr.bicep)'
    $out = az deployment group create -g $sourceRg -n "dr-$(Get-Date -Format yyyyMMdd-HHmmss)" -f "$PSScriptRoot/dr.bicep" `
        -p env=$Env sshPublicKey="$pub" --query properties.outputs -o json
    if ($LASTEXITCODE -ne 0) { throw 'dr.bicep deployment failed' }
    $o = $out | ConvertFrom-Json
    $vaultPath = "/subscriptions/$sub/resourceGroups/$sourceRg/providers/Microsoft.RecoveryServices/vaults/$($o.vaultName.value)"

    Step 'Site Recovery fabrics (one per region)'
    foreach ($f in @(@($pf, $primaryLocation), @($rf, $recoveryLocation))) {
        if (-not (Get-Arm "$vaultPath/replicationFabrics/$($f[0])")) {
            Arm put "$vaultPath/replicationFabrics/$($f[0])" @{ properties = @{ customDetails = @{ instanceType = 'Azure'; location = $f[1] } } } | Out-Null
        }
        $name = $f[0]
        Wait-For "fabric $name" { (Get-Arm "$vaultPath/replicationFabrics/$name").properties.friendlyName }
    }

    Step 'Protection containers'
    foreach ($c in @(@($pf, $pc), @($rf, $rc))) {
        $path = "$vaultPath/replicationFabrics/$($c[0])/replicationProtectionContainers/$($c[1])"
        if (-not (Get-Arm $path)) { Arm put $path @{ properties = @{ providerSpecificInput = @(@{ instanceType = 'A2A' }) } } | Out-Null }
        Wait-For "container $($c[1])" { (Get-Arm $path).properties.friendlyName }
    }

    Step 'Replication policy (24h of recovery points, crash-consistent every 5 min)'
    $policyPath = "$vaultPath/replicationPolicies/$policy"
    if (-not (Get-Arm $policyPath)) {
        Arm put $policyPath @{ properties = @{ providerSpecificInput = @{
            instanceType = 'A2A'; recoveryPointHistory = 1440; crashConsistentFrequencyInMinutes = 5
            appConsistentFrequencyInMinutes = 0; multiVmSyncStatus = 'Disable' } } } | Out-Null
    }
    Wait-For 'policy' { (Get-Arm $policyPath).properties.friendlyName }
    $policyId = (Get-Arm $policyPath).id

    Step 'Container mapping (East -> Southeast)'
    $pcPath = "$vaultPath/replicationFabrics/$pf/replicationProtectionContainers/$pc"
    $rcId = (Get-Arm "$vaultPath/replicationFabrics/$rf/replicationProtectionContainers/$rc").id
    $mapPath = "$pcPath/replicationProtectionContainerMappings/$mapping"
    if (-not (Get-Arm $mapPath)) {
        Arm put $mapPath @{ properties = @{ targetProtectionContainerId = $rcId; policyId = $policyId
            providerSpecificInput = @{ instanceType = 'A2A'; agentAutoUpdateStatus = 'Disabled' } } } | Out-Null
    }
    Wait-For 'container mapping' { (Get-Arm $mapPath).properties.state -eq 'Paired' }

    Step 'Network mapping (failed-over VM lands in the recovery VNet)'
    $netPath = "$vaultPath/replicationFabrics/$pf/replicationNetworks/azureNetwork/replicationNetworkMappings/$netMapping"
    if (-not (Get-Arm $netPath)) {
        Arm put $netPath @{ properties = @{ recoveryFabricName = $rf; recoveryNetworkId = $o.recoveryVnetId.value
            fabricSpecificDetails = @{ instanceType = 'AzureToAzure'; primaryNetworkId = $o.sourceVnetId.value } } } | Out-Null
    }
    Wait-For 'network mapping' { (Get-Arm $netPath).properties.state }

    Step 'Enable replication for the legacy VM'
    $targetRgId = "/subscriptions/$sub/resourceGroups/$targetRg"
    $itemPath = "$pcPath/replicationProtectedItems/$($o.vmName.value)"
    if (-not (Get-Arm $itemPath)) {
        Arm put $itemPath @{ properties = @{ policyId = $policyId; protectableItemId = ''
            providerSpecificDetails = @{
                instanceType = 'A2A'; fabricObjectId = $o.vmId.value; recoveryContainerId = $rcId
                recoveryResourceGroupId = $targetRgId; recoveryAzureNetworkId = $o.recoveryVnetId.value
                vmManagedDisks = @(@{ diskId = $o.osDiskId.value; primaryStagingAzureStorageAccountId = $o.cacheStorageId.value
                    recoveryResourceGroupId = $targetRgId; recoveryReplicaDiskAccountType = 'Standard_LRS'; recoveryTargetDiskAccountType = 'Standard_LRS' })
            } } } | Out-Null
    }
    Write-Host '  Replication enabled. Initial sync takes ~30-60 minutes; check with: ./infra/dr.ps1 -Status' -ForegroundColor Green
}

if ($Status -or $Drill) {
    $vaultPath = Resolve-Vault
    $vm = "vm-legacy-$Env"
    $itemPath = "$vaultPath/replicationFabrics/$pf/replicationProtectionContainers/$pc/replicationProtectedItems/$vm"
    $item = Get-Arm $itemPath
    if (-not $item) { throw "VM $vm is not protected yet" }
    $p = $item.properties
    Write-Host "  $vm  state=$($p.protectionStateDescription)  health=$($p.replicationHealth)  testFailover=$($p.testFailoverStateDescription)"
    if ($p.providerSpecificDetails.monitoringPercentageCompletion) { Write-Host "  initial sync: $($p.providerSpecificDetails.monitoringPercentageCompletion)%" }
}

if ($Drill) {
    if ($p.protectionState -ne 'Protected') { throw "Replication not ready yet ($($p.protectionStateDescription)); try again later" }
    $testVnet = az network vnet list -g $sourceRg --query "[?contains(name, 'drtest')].id | [0]" -o tsv

    Step 'Test failover into the isolated test network (production keeps running)'
    Arm post "$itemPath/testFailover" @{ properties = @{ failoverDirection = 'PrimaryToRecovery'; networkType = 'VmNetworkAsInput'
        networkId = $testVnet; providerSpecificDetails = @{ instanceType = 'A2A' } } } | Out-Null
    Wait-For 'test failover' { (Get-Arm $itemPath).properties.testFailoverState -eq 'WaitingForCompletion' } 30
    $testVm = az vm list -g $targetRg --query "[?contains(name, 'legacy')].{name:name, state:powerState}" -d -o json | ConvertFrom-Json
    Write-Host "  Test VM in ${recoveryLocation}: $($testVm.name) ($($testVm.state))" -ForegroundColor Green

    Step 'Clean up the test failover'
    Arm post "$itemPath/testFailoverCleanup" @{ properties = @{ comments = "DR drill $(Get-Date -Format s) - test VM booted in $recoveryLocation" } } | Out-Null
    Wait-For 'cleanup' { (Get-Arm $itemPath).properties.testFailoverState -in 'None', 'MarkedForDeletion' } 30
    Write-Host '  Drill complete.' -ForegroundColor Green
}

if ($Remove) {
    $vaultPath = Resolve-Vault
    $itemPath = "$vaultPath/replicationFabrics/$pf/replicationProtectionContainers/$pc/replicationProtectedItems/vm-legacy-$Env"
    if (Get-Arm $itemPath) {
        Step 'Disable replication'
        Arm post "$itemPath/remove" @{ properties = @{ disableProtectionReason = 'NotSpecified' } } | Out-Null
        Wait-For 'replication removal' { -not (Get-Arm $itemPath) } 20
    }
    Step 'Delete DR resource groups'
    az group delete -n $targetRg --yes --no-wait
    az group delete -n $sourceRg --yes --no-wait
    Write-Host '  deletion started' -ForegroundColor Green
}
