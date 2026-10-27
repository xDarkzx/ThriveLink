// Azure SQL with Entra-only authentication (SQL logins disabled) on the serverless free offer.
param serverName string
param location string
param tags object
param adminObjectId string
param adminLogin string
param adminPrincipalType string
param deployerIp string

resource server 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: adminLogin
      sid: adminObjectId
      tenantId: tenant().tenantId
      principalType: adminPrincipalType
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: server
  name: 'ThriveLink'
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
  }
}

// Lets Azure services (App Service, Functions, Data Factory) reach the server.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource allowDeployer 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (!empty(deployerIp)) {
  parent: server
  name: 'deployer'
  properties: {
    startIpAddress: deployerIp
    endIpAddress: deployerIp
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
