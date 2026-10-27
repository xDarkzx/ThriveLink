// Phase 6b: a small "legacy policy admin" VM (the kind of IaaS workload insurers still run) plus the
// Azure Site Recovery plumbing to replicate it to Australia Southeast. Replication itself is enabled
// by infra/dr.ps1 (Site Recovery's A2A setup is driven through its own API objects).
targetScope = 'resourceGroup'

param env string = 'dev'
param location string = resourceGroup().location
param recoveryLocation string = 'australiasoutheast'
param adminUsername string = 'azureuser'

@description('SSH public key for the VM admin user.')
param sshPublicKey string

var suffix = take(uniqueString(resourceGroup().id), 6)
var base = 'thrivelink-${env}-${suffix}'
var tags = {
  app: 'thrivelink'
  env: env
  role: 'legacy-policy-admin'
}

// Source network: private only, no public IP on the VM.
resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: 'vnet-legacy-${base}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.50.0.0/16'] }
    subnets: [{ name: 'snet-legacy', properties: { addressPrefix: '10.50.1.0/24' } }]
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2024-01-01' = {
  name: 'nic-legacy-${base}'
  location: location
  tags: tags
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          subnet: { id: vnet.properties.subnets[0].id }
          privateIPAllocationMethod: 'Dynamic'
        }
      }
    ]
  }
}

// cloud-init: a stand-in "policy admin" service (nginx serving a status page).
var cloudInit = '''
#cloud-config
package_update: true
packages: [nginx]
write_files:
  - path: /var/www/html/index.html
    content: |
      <h1>ThriveLink legacy policy admin</h1><p>Stand-in IaaS workload protected by Azure Site Recovery.</p>
runcmd:
  - systemctl enable --now nginx
'''

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: 'vm-legacy-${env}'
  location: location
  tags: tags
  properties: {
    hardwareProfile: { vmSize: 'Standard_B1s' }
    osProfile: {
      computerName: 'legacy-policy'
      adminUsername: adminUsername
      customData: base64(cloudInit)
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: { publicKeys: [{ path: '/home/${adminUsername}/.ssh/authorized_keys', keyData: sshPublicKey }] }
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        managedDisk: { storageAccountType: 'Standard_LRS' }
        deleteOption: 'Delete'
      }
    }
    networkProfile: { networkInterfaces: [{ id: nic.id, properties: { deleteOption: 'Delete' } }] }
    securityProfile: { securityType: 'TrustedLaunch', uefiSettings: { secureBootEnabled: true, vTpmEnabled: true } }
  }
}

// Site Recovery stages replicated writes in a cache storage account in the SOURCE region.
resource cache 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('stasrcache${env}${suffix}', 24)
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

// Recovery side: vault and network live in the TARGET region.
resource vault 'Microsoft.RecoveryServices/vaults@2024-04-01' = {
  name: 'rsv-${base}'
  location: recoveryLocation
  tags: tags
  sku: { name: 'RS0', tier: 'Standard' }
  identity: { type: 'SystemAssigned' }
  properties: { publicNetworkAccess: 'Enabled' }
}

resource recoveryVnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: 'vnet-legacy-dr-${base}'
  location: recoveryLocation
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.51.0.0/16'] }
    subnets: [{ name: 'snet-legacy', properties: { addressPrefix: '10.51.1.0/24' } }]
  }
}

// Isolated network for test failovers, so a DR drill never touches production traffic.
resource testVnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: 'vnet-legacy-drtest-${base}'
  location: recoveryLocation
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.52.0.0/16'] }
    subnets: [{ name: 'snet-legacy', properties: { addressPrefix: '10.52.1.0/24' } }]
  }
}

output vmId string = vm.id
output vmName string = vm.name
output osDiskId string = vm.properties.storageProfile.osDisk.managedDisk.id
output cacheStorageId string = cache.id
output vaultName string = vault.name
output sourceVnetId string = vnet.id
output recoveryVnetId string = recoveryVnet.id
output testVnetId string = testVnet.id
