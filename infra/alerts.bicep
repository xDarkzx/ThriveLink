// Phase 6a: Azure Monitor alerting for the running platform.
targetScope = 'resourceGroup'

param env string = 'dev'
param location string = resourceGroup().location
param alertEmail string
param webAppName string
param dataFactoryName string
param appInsightsName string
param workspaceName string

@description('Alert when more than this many linked devices have not synced for 72 hours.')
param staleDeviceThreshold int = 5

var tags = {
  app: 'thrivelink'
  env: env
}

resource web 'Microsoft.Web/sites@2023-12-01' existing = {
  name: webAppName
}

resource adf 'Microsoft.DataFactory/factories@2018-06-01' existing = {
  name: dataFactoryName
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-thrivelink-${env}'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'thrivelink'
    enabled: true
    emailReceivers: [{ name: 'on-call', emailAddress: alertEmail, useCommonAlertSchema: true }]
  }
}

// Synthetic check from three regions every 5 minutes.
resource availability 'Microsoft.Insights/webtests@2022-06-15' = {
  name: 'health-${webAppName}'
  location: location
  tags: union(tags, { 'hidden-link:${appInsights.id}': 'Resource' })
  kind: 'standard'
  properties: {
    SyntheticMonitorId: 'health-${webAppName}'
    Name: 'ThriveLink /health'
    Enabled: true
    Frequency: 300
    Timeout: 60
    Kind: 'standard'
    RetryEnabled: true
    Locations: [{ Id: 'emea-au-syd-edge' }, { Id: 'apac-sg-sin-azr' }, { Id: 'us-ca-sjc-azr' }]
    Request: {
      RequestUrl: 'https://${web.properties.defaultHostName}/health'
      HttpVerb: 'GET'
      ParseDependentRequests: false
    }
    ValidationRules: { ExpectedHttpStatusCode: 200, SSLCheck: true, SSLCertRemainingLifetimeCheck: 7 }
  }
}

resource availabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'thrivelink-${env}-health-check-failing'
  location: 'global'
  tags: tags
  properties: {
    description: '/health failing from 2 or more locations.'
    severity: 1
    enabled: true
    scopes: [availability.id, appInsights.id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      webTestId: availability.id
      componentId: appInsights.id
      failedLocationCount: 2
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

resource http5xx 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'thrivelink-${env}-web-5xx'
  location: 'global'
  tags: tags
  properties: {
    description: 'More than 5 server errors in 5 minutes on the web app.'
    severity: 2
    enabled: true
    scopes: [web.id]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'http5xx'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'Http5xx'
          metricNamespace: 'Microsoft.Web/sites'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroup.id }]
  }
}

// The StaleDeviceSweep function logs StaleDeviceCount as a structured value; alert on it.
resource staleDevices 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'thrivelink-${env}-stale-devices'
  location: location
  tags: tags
  properties: {
    description: 'Too many linked devices have not synced in 72 hours (likely provider outage or app bug).'
    severity: 3
    enabled: true
    scopes: [appInsights.id]
    evaluationFrequency: 'PT1H'
    windowSize: 'P1D'
    criteria: {
      allOf: [
        {
          query: 'traces | where message startswith "Stale device sweep" | extend StaleDeviceCount = toint(customDimensions.StaleDeviceCount) | summarize StaleDeviceCount = max(StaleDeviceCount)'
          timeAggregation: 'Maximum'
          metricMeasureColumn: 'StaleDeviceCount'
          operator: 'GreaterThan'
          threshold: staleDeviceThreshold
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [actionGroup.id] }
  }
}

// Data Factory run history -> Log Analytics, so pipeline failures can be queried and alerted on.
resource adfDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: adf
  properties: {
    workspaceId: workspace.id
    logAnalyticsDestinationType: 'Dedicated'
    logs: [
      { category: 'PipelineRuns', enabled: true }
      { category: 'ActivityRuns', enabled: true }
      { category: 'TriggerRuns', enabled: true }
    ]
  }
}

resource adfFailed 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'thrivelink-${env}-adf-pipeline-failed'
  location: location
  tags: tags
  dependsOn: [adfDiagnostics]
  properties: {
    description: 'A Data Factory pipeline run failed (partner health-check import).'
    severity: 2
    enabled: true
    scopes: [workspace.id]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: 'ADFPipelineRun | where Status == "Failed"'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [actionGroup.id] }
  }
}
