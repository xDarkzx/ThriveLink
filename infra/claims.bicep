// Phase 7: claims triage as a scheduled Azure Container Apps Job, image in Azure Container Registry.
// A user-assigned identity is the job's only credential: it pulls the image, signs in to SQL and reads Key Vault.
targetScope = 'resourceGroup'

param env string = 'dev'
param location string = resourceGroup().location
param workspaceName string
param keyVaultName string
param sqlServerFqdn string
param sqlDatabaseName string

@description('Set false on the first run (registry only), true once the image has been built.')
param deployJob bool = false

@description('Image tag in the registry, e.g. claims-triage:20261028-1200.')
param image string = ''

@description('Key Vault secret holding an Anthropic API key. Empty disables AI review (rules still run).')
param anthropicSecretName string = ''

@description('Deployer allowed to push images (local docker push; ACR Tasks are not available on free trials).')
param adminObjectId string
param adminPrincipalType string = 'User'

var suffix = take(uniqueString(resourceGroup().id), 6)
var base = 'thrivelink-${env}-${suffix}'
var tags = {
  app: 'thrivelink'
  env: env
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: take('acrtl${env}${suffix}', 50)
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-claims-${base}'
  location: location
  tags: tags
}

var acrPull = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'

resource pullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, identity.id, acrPull)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPull)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

var acrPush = '8311e382-0749-4cb8-b61a-304f252e45ec'

resource adminPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, adminObjectId, acrPush)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPush)
    principalId: adminObjectId
    principalType: adminPrincipalType
  }
}

resource secretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, identity.id, keyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${base}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
  }
}

var aiEnabled = !empty(anthropicSecretName)

resource job 'Microsoft.App/jobs@2024-03-01' = if (deployJob) {
  name: 'job-claims-triage-${env}'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  dependsOn: [pullRole, secretsRole]
  properties: {
    environmentId: containerEnv.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Schedule'
      scheduleTriggerConfig: {
        cronExpression: '*/5 * * * *'
        parallelism: 1
        replicaCompletionCount: 1
      }
      replicaTimeout: 600
      replicaRetryLimit: 1
      registries: [{ server: acr.properties.loginServer, identity: identity.id }]
      secrets: aiEnabled
        ? [{ name: 'anthropic-api-key', keyVaultUrl: '${vault.properties.vaultUri}secrets/${anthropicSecretName}', identity: identity.id }]
        : []
    }
    template: {
      containers: [
        {
          name: 'claims-triage'
          image: '${acr.properties.loginServer}/${image}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(
            [
              { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
              {
                name: 'ConnectionStrings__Sql'
                value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;Connect Timeout=60'
              }
            ],
            aiEnabled ? [{ name: 'Anthropic__ApiKey', secretRef: 'anthropic-api-key' }] : []
          )
        }
      ]
    }
  }
}

output registryName string = acr.name
output registryServer string = acr.properties.loginServer
output identityName string = identity.name
output jobName string = deployJob ? job.name : ''
