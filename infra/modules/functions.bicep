// Azure Functions on the Flex Consumption plan (serverless, scales to zero).
// Identity-based connections only: storage, deployment package and SQL all use the app's managed identity.
param base string
param location string
param tags object
param storageAccountName string
param appInsightsConnectionString string
param sqlServerFqdn string
param sqlDatabaseName string

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-func-${base}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: { reserved: true }
}

resource func 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-${base}'
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}deployments'
          authentication: { type: 'SystemAssignedIdentity' }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        {
          name: 'ConnectionStrings__Sql'
          value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=60'
        }
        { name: 'Storage__BlobEndpoint', value: storage.properties.primaryEndpoints.blob }
      ]
    }
  }
}

var storageBlobDataOwner = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageQueueDataContributor = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributor = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'

resource roles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [storageBlobDataOwner, storageQueueDataContributor, storageTableDataContributor]: {
    scope: storage
    name: guid(storage.id, func.id, role)
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
      principalId: func.identity.principalId
      principalType: 'ServicePrincipal'
    }
  }
]

output name string = func.name
output principalId string = func.identity.principalId
output hostName string = func.properties.defaultHostName
