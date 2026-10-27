// Phase 5b: Application Gateway WAF v2 in front of the member web app.
// OWASP / Microsoft Default Rule Set in Prevention mode. Costs ~US$10/day while it exists,
// so deploy it for demos and remove it afterwards (see infra/gateway.ps1).
targetScope = 'resourceGroup'

param env string = 'dev'
param location string = resourceGroup().location
param webAppName string

var suffix = take(uniqueString(resourceGroup().id), 6)
var base = 'thrivelink-${env}-${suffix}'
var tags = {
  app: 'thrivelink'
  env: env
  costNote: 'delete-after-demo'
}

resource web 'Microsoft.Web/sites@2023-12-01' existing = {
  name: webAppName
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: 'vnet-${base}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.40.0.0/16'] }
    subnets: [
      {
        name: 'snet-appgw'
        properties: { addressPrefix: '10.40.1.0/24' }
      }
    ]
  }
}

resource pip 'Microsoft.Network/publicIPAddresses@2024-01-01' = {
  name: 'pip-agw-${base}'
  location: location
  tags: tags
  sku: { name: 'Standard' }
  properties: {
    publicIPAllocationMethod: 'Static'
    dnsSettings: { domainNameLabel: 'thrivelink-${env}-${suffix}' }
  }
}

resource wafPolicy 'Microsoft.Network/ApplicationGatewayWebApplicationFirewallPolicies@2024-01-01' = {
  name: 'waf-${base}'
  location: location
  tags: tags
  properties: {
    policySettings: {
      state: 'Enabled'
      mode: 'Prevention'
      requestBodyCheck: true
    }
    managedRules: {
      managedRuleSets: [{ ruleSetType: 'Microsoft_DefaultRuleSet', ruleSetVersion: '2.1' }]
    }
  }
}

var agwName = 'agw-${base}'
var agwId = resourceId('Microsoft.Network/applicationGateways', agwName)

resource agw 'Microsoft.Network/applicationGateways@2024-01-01' = {
  name: agwName
  location: location
  tags: tags
  properties: {
    sku: { name: 'WAF_v2', tier: 'WAF_v2' }
    autoscaleConfiguration: { minCapacity: 0, maxCapacity: 2 }
    firewallPolicy: { id: wafPolicy.id }
    gatewayIPConfigurations: [
      { name: 'gwip', properties: { subnet: { id: vnet.properties.subnets[0].id } } }
    ]
    frontendIPConfigurations: [
      { name: 'public', properties: { publicIPAddress: { id: pip.id } } }
    ]
    frontendPorts: [{ name: 'http', properties: { port: 80 } }]
    backendAddressPools: [
      { name: 'webapp', properties: { backendAddresses: [{ fqdn: web.properties.defaultHostName }] } }
    ]
    probes: [
      {
        name: 'health'
        properties: {
          protocol: 'Https'
          path: '/health'
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: true
        }
      }
    ]
    backendHttpSettingsCollection: [
      {
        name: 'https-to-app'
        properties: {
          port: 443
          protocol: 'Https'
          pickHostNameFromBackendAddress: true
          requestTimeout: 60
          probe: { id: '${agwId}/probes/health' }
        }
      }
    ]
    httpListeners: [
      {
        name: 'http'
        properties: {
          frontendIPConfiguration: { id: '${agwId}/frontendIPConfigurations/public' }
          frontendPort: { id: '${agwId}/frontendPorts/http' }
          protocol: 'Http'
        }
      }
    ]
    requestRoutingRules: [
      {
        name: 'to-webapp'
        properties: {
          ruleType: 'Basic'
          priority: 100
          httpListener: { id: '${agwId}/httpListeners/http' }
          backendAddressPool: { id: '${agwId}/backendAddressPools/webapp' }
          backendHttpSettings: { id: '${agwId}/backendHttpSettingsCollection/https-to-app' }
        }
      }
    ]
  }
}

output url string = 'http://${pip.properties.dnsSettings.fqdn}'
