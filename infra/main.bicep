// ThriveLink platform (phase 2): monitoring, Key Vault, Blob Storage, Azure SQL and App Service.
// Everything authenticates with managed identity; there are no passwords or keys in app settings.
targetScope = 'resourceGroup'

@description('Environment name, e.g. dev, test, prod.')
param env string = 'dev'

param location string = resourceGroup().location

@description('Entra ID object id of the person or pipeline that administers SQL and Key Vault.')
param adminObjectId string

@description('Display name / UPN for the SQL Entra admin.')
param adminLogin string

@allowed(['User', 'Group', 'Application'])
param adminPrincipalType string = 'User'

@description('Public IP allowed through the SQL firewall for migrations from the deployer machine. Empty to skip.')
param deployerIp string = ''

@description('App Service plan SKU. F1 is free; B1 or higher enables Always On.')
param appServiceSku string = 'F1'

var suffix = take(uniqueString(resourceGroup().id), 6)
var base = 'thrivelink-${env}-${suffix}'
var tags = {
  app: 'thrivelink'
  env: env
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    base: base
    location: location
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: take('sttl${env}${suffix}', 24)
    location: location
    tags: tags
    adminObjectId: adminObjectId
    adminPrincipalType: adminPrincipalType
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: take('kv-tl-${env}-${suffix}', 24)
    location: location
    tags: tags
    adminObjectId: adminObjectId
    adminPrincipalType: adminPrincipalType
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    serverName: 'sql-${base}'
    location: location
    tags: tags
    adminObjectId: adminObjectId
    adminLogin: adminLogin
    adminPrincipalType: adminPrincipalType
    deployerIp: deployerIp
  }
}

module app 'modules/appservice.bicep' = {
  name: 'appservice'
  params: {
    base: base
    location: location
    tags: tags
    sku: appServiceSku
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    keyVaultName: keyVault.outputs.name
    storageAccountName: storage.outputs.name
    environmentName: env
  }
}

module functions 'modules/functions.bicep' = {
  name: 'functions'
  params: {
    base: base
    location: location
    tags: tags
    storageAccountName: storage.outputs.name
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
  }
}

module dataFactory 'modules/datafactory.bicep' = {
  name: 'datafactory'
  params: {
    base: base
    location: location
    tags: tags
    storageAccountName: storage.outputs.name
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
  }
}

output dataFactoryName string = dataFactory.outputs.name
output functionAppName string = functions.outputs.name
output functionAppHost string = functions.outputs.hostName
output webAppName string = app.outputs.webAppName
output webAppUrl string = app.outputs.url
output webAppPrincipalId string = app.outputs.principalId
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output keyVaultName string = keyVault.outputs.name
output storageAccountName string = storage.outputs.name
output logAnalyticsWorkspaceId string = monitoring.outputs.workspaceId
output logAnalyticsWorkspaceName string = monitoring.outputs.workspaceName
output appInsightsName string = monitoring.outputs.appInsightsName
