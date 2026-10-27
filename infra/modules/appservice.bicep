// Linux App Service running the .NET API + stats page, with a system-assigned managed identity.
param base string
param location string
param tags object
param sku string
param appInsightsConnectionString string
param sqlServerFqdn string
param sqlDatabaseName string
param keyVaultName string
param storageAccountName string
param environmentName string

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'asp-${base}'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: sku }
  properties: { reserved: true }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${base}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: sku != 'F1' && sku != 'D1'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/health'
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
        // Passwordless: the app signs in to SQL as its own managed identity.
        {
          name: 'ConnectionStrings__Sql'
          value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=60'
        }
        // Key Vault reference: App Service resolves the secret with the app's identity at runtime.
        { name: 'Ingest__ApiKey', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Ingest--ApiKey)' }
        { name: 'Storage__BlobEndpoint', value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}' }
      ]
    }
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource webReadsSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, web.id, keyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource webWritesBlobs 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, web.id, storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output webAppName string = web.name
output url string = 'https://${web.properties.defaultHostName}'
output principalId string = web.identity.principalId
