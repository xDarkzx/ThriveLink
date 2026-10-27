// Blob storage for raw wearable payloads, partner files and claim documents. No public access.
param name string
param location string
param tags object

@description('Deployer/admin who can read and write blobs (e.g. to upload test partner files).')
param adminObjectId string
param adminPrincipalType string

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }
  }
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for c in ['activity-raw', 'partner-files', 'claim-documents', 'deployments']: {
    parent: blobService
    name: c
    properties: { publicAccess: 'None' }
  }
]

var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource adminBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, adminObjectId, storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
    principalId: adminObjectId
    principalType: adminPrincipalType
  }
}

output name string = account.name
output id string = account.id
