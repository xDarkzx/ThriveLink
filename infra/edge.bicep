// Phase 5a: API Management (Consumption) as the partner-facing front door.
// Partners authenticate with an APIM subscription key; APIM injects backend credentials from Key Vault,
// so partners never hold the API key or the Functions key.
targetScope = 'resourceGroup'

param env string = 'dev'
param location string = resourceGroup().location
param publisherEmail string
param webAppName string
param functionAppName string
param keyVaultName string

var suffix = take(uniqueString(resourceGroup().id), 6)
var tags = {
  app: 'thrivelink'
  env: env
}

resource web 'Microsoft.Web/sites@2023-12-01' existing = {
  name: webAppName
}

resource func 'Microsoft.Web/sites@2024-04-01' existing = {
  name: functionAppName
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource apim 'Microsoft.ApiManagement/service@2023-09-01-preview' = {
  name: 'apim-thrivelink-${env}-${suffix}'
  location: location
  tags: tags
  sku: {
    name: 'Consumption'
    capacity: 0
  }
  identity: { type: 'SystemAssigned' }
  properties: {
    publisherEmail: publisherEmail
    publisherName: 'ThriveLink'
  }
}

var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'

resource apimReadsSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, apim.id, keyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId: apim.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource ingestKey 'Microsoft.ApiManagement/service/namedValues@2023-09-01-preview' = {
  parent: apim
  name: 'ingest-api-key'
  dependsOn: [apimReadsSecrets]
  properties: {
    displayName: 'ingest-api-key'
    secret: true
    keyVault: { secretIdentifier: '${vault.properties.vaultUri}secrets/Ingest--ApiKey' }
  }
}

resource functionsKey 'Microsoft.ApiManagement/service/namedValues@2023-09-01-preview' = {
  parent: apim
  name: 'functions-host-key'
  dependsOn: [apimReadsSecrets]
  properties: {
    displayName: 'functions-host-key'
    secret: true
    keyVault: { secretIdentifier: '${vault.properties.vaultUri}secrets/Functions--HostKey' }
  }
}

resource webBackend 'Microsoft.ApiManagement/service/backends@2023-09-01-preview' = {
  parent: apim
  name: 'thrivelink-api'
  properties: {
    protocol: 'http'
    url: 'https://${web.properties.defaultHostName}/api'
    tls: { validateCertificateChain: true, validateCertificateName: true }
  }
}

resource funcBackend 'Microsoft.ApiManagement/service/backends@2023-09-01-preview' = {
  parent: apim
  name: 'thrivelink-functions'
  properties: {
    protocol: 'http'
    url: 'https://${func.properties.defaultHostName}/api'
    tls: { validateCertificateChain: true, validateCertificateName: true }
  }
}

resource api 'Microsoft.ApiManagement/service/apis@2023-09-01-preview' = {
  parent: apim
  name: 'partner-api'
  properties: {
    displayName: 'ThriveLink Partner API'
    description: 'For wearable aggregators and partner systems. Requires an APIM subscription key.'
    path: 'partner'
    protocols: ['https']
    subscriptionRequired: true
    subscriptionKeyParameterNames: { header: 'Ocp-Apim-Subscription-Key', query: 'subscription-key' }
  }
}

// Applies to every operation: throttle each subscription and strip headers that reveal the backend.
resource apiPolicy 'Microsoft.ApiManagement/service/apis/policies@2023-09-01-preview' = {
  parent: api
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: '''
<policies>
  <inbound>
    <base />
    <rate-limit calls="60" renewal-period="60" />
  </inbound>
  <backend><base /></backend>
  <outbound>
    <base />
    <set-header name="X-Powered-By" exists-action="delete" />
    <set-header name="Server" exists-action="delete" />
  </outbound>
  <on-error><base /></on-error>
</policies>
'''
  }
}

resource activityOp 'Microsoft.ApiManagement/service/apis/operations@2023-09-01-preview' = {
  parent: api
  name: 'post-activity'
  properties: {
    displayName: 'Submit one day of activity'
    method: 'POST'
    urlTemplate: '/activity'
  }
}

resource activityPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2023-09-01-preview' = {
  parent: activityOp
  name: 'policy'
  dependsOn: [ingestKey, webBackend]
  properties: {
    format: 'rawxml'
    value: '''
<policies>
  <inbound>
    <base />
    <set-backend-service backend-id="thrivelink-api" />
    <set-header name="X-Api-Key" exists-action="override"><value>{{ingest-api-key}}</value></set-header>
  </inbound>
  <backend><base /></backend>
  <outbound><base /></outbound>
  <on-error><base /></on-error>
</policies>
'''
  }
}

resource syncOp 'Microsoft.ApiManagement/service/apis/operations@2023-09-01-preview' = {
  parent: api
  name: 'post-wearable-sync'
  properties: {
    displayName: 'Submit a wearable sync batch'
    method: 'POST'
    urlTemplate: '/wearables/sync'
  }
}

resource syncPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2023-09-01-preview' = {
  parent: syncOp
  name: 'policy'
  dependsOn: [functionsKey, funcBackend]
  properties: {
    format: 'rawxml'
    value: '''
<policies>
  <inbound>
    <base />
    <set-backend-service backend-id="thrivelink-functions" />
    <set-header name="x-functions-key" exists-action="override"><value>{{functions-host-key}}</value></set-header>
  </inbound>
  <backend><base /></backend>
  <outbound><base /></outbound>
  <on-error><base /></on-error>
</policies>
'''
  }
}

resource memberOp 'Microsoft.ApiManagement/service/apis/operations@2023-09-01-preview' = {
  parent: api
  name: 'get-member'
  properties: {
    displayName: 'Get member status'
    method: 'GET'
    urlTemplate: '/members/{memberNumber}'
    templateParameters: [{ name: 'memberNumber', type: 'string', required: true }]
  }
}

resource memberPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2023-09-01-preview' = {
  parent: memberOp
  name: 'policy'
  dependsOn: [webBackend]
  properties: {
    format: 'rawxml'
    value: '''
<policies>
  <inbound>
    <base />
    <set-backend-service backend-id="thrivelink-api" />
  </inbound>
  <backend><base /></backend>
  <outbound><base /></outbound>
  <on-error><base /></on-error>
</policies>
'''
  }
}

resource product 'Microsoft.ApiManagement/service/products@2023-09-01-preview' = {
  parent: apim
  name: 'partners'
  properties: {
    displayName: 'Partners'
    description: 'Wearable aggregators and health-check partners.'
    subscriptionRequired: true
    approvalRequired: false
    state: 'published'
  }
}

resource productApi 'Microsoft.ApiManagement/service/products/apis@2023-09-01-preview' = {
  parent: product
  name: api.name
}

resource demoSubscription 'Microsoft.ApiManagement/service/subscriptions@2023-09-01-preview' = {
  parent: apim
  name: 'demo-partner'
  properties: {
    displayName: 'Demo partner'
    scope: product.id
    state: 'active'
  }
}

output apimName string = apim.name
output gatewayUrl string = apim.properties.gatewayUrl
