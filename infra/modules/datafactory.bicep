// Azure Data Factory: nightly import of partner health-check files (CSV in Blob) into Azure SQL.
// Pipeline: copy CSV -> staging table -> stored procedure applies results and points -> archive the file.
// The factory signs in to Blob and SQL with its own managed identity.
param base string
param location string
param tags object
param storageAccountName string
param sqlServerFqdn string
param sqlDatabaseName string

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource factory 'Microsoft.DataFactory/factories@2018-06-01' = {
  name: 'adf-${base}'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {}
}

resource blobLinked 'Microsoft.DataFactory/factories/linkedservices@2018-06-01' = {
  parent: factory
  name: 'PartnerBlob'
  properties: {
    type: 'AzureBlobStorage'
    typeProperties: {
      serviceEndpoint: storage.properties.primaryEndpoints.blob
      accountKind: 'StorageV2'
    }
  }
}

resource sqlLinked 'Microsoft.DataFactory/factories/linkedservices@2018-06-01' = {
  parent: factory
  name: 'ThriveLinkSql'
  properties: {
    type: 'AzureSqlDatabase'
    typeProperties: {
      connectionString: 'Data Source=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;Connection Timeout=60'
    }
  }
}

resource incomingCsv 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: factory
  name: 'IncomingHealthChecksCsv'
  properties: {
    type: 'DelimitedText'
    linkedServiceName: { referenceName: blobLinked.name, type: 'LinkedServiceReference' }
    typeProperties: {
      location: {
        type: 'AzureBlobStorageLocation'
        container: 'partner-files'
        folderPath: 'incoming'
      }
      columnDelimiter: ','
      firstRowAsHeader: true
      quoteChar: '"'
    }
  }
}

resource incomingBinary 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: factory
  name: 'IncomingFilesBinary'
  properties: {
    type: 'Binary'
    linkedServiceName: { referenceName: blobLinked.name, type: 'LinkedServiceReference' }
    typeProperties: {
      location: {
        type: 'AzureBlobStorageLocation'
        container: 'partner-files'
        folderPath: 'incoming'
      }
    }
  }
}

resource archiveBinary 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: factory
  name: 'ArchiveFilesBinary'
  properties: {
    type: 'Binary'
    linkedServiceName: { referenceName: blobLinked.name, type: 'LinkedServiceReference' }
    parameters: { runFolder: { type: 'string' } }
    typeProperties: {
      location: {
        type: 'AzureBlobStorageLocation'
        container: 'partner-files'
        folderPath: { value: '@concat(\'archive/\', dataset().runFolder)', type: 'Expression' }
      }
    }
  }
}

resource stagingTable 'Microsoft.DataFactory/factories/datasets@2018-06-01' = {
  parent: factory
  name: 'HealthCheckStaging'
  properties: {
    type: 'AzureSqlTable'
    linkedServiceName: { referenceName: sqlLinked.name, type: 'LinkedServiceReference' }
    typeProperties: {
      schema: 'staging'
      table: 'HealthCheckImport'
    }
  }
}

resource pipeline 'Microsoft.DataFactory/factories/pipelines@2018-06-01' = {
  parent: factory
  name: 'ImportPartnerHealthChecks'
  properties: {
    description: 'Loads partner health-check CSVs from partner-files/incoming, awards points, archives the files.'
    activities: [
      {
        name: 'CheckForFiles'
        type: 'GetMetadata'
        typeProperties: {
          dataset: { referenceName: incomingBinary.name, type: 'DatasetReference' }
          fieldList: ['childItems']
          storeSettings: { type: 'AzureBlobStorageReadSettings', recursive: false }
        }
      }
      {
        name: 'IfFilesArrived'
        type: 'IfCondition'
        dependsOn: [{ activity: 'CheckForFiles', dependencyConditions: ['Succeeded'] }]
        typeProperties: {
          expression: { value: '@greater(length(activity(\'CheckForFiles\').output.childItems), 0)', type: 'Expression' }
          ifTrueActivities: [
            {
              name: 'CopyCsvToStaging'
              type: 'Copy'
              inputs: [{ referenceName: incomingCsv.name, type: 'DatasetReference' }]
              outputs: [{ referenceName: stagingTable.name, type: 'DatasetReference' }]
              typeProperties: {
                source: {
                  type: 'DelimitedTextSource'
                  storeSettings: { type: 'AzureBlobStorageReadSettings', recursive: false, wildcardFileName: '*.csv' }
                  formatSettings: { type: 'DelimitedTextReadSettings' }
                }
                sink: {
                  type: 'AzureSqlSink'
                  preCopyScript: 'DELETE FROM staging.HealthCheckImport'
                  writeBehavior: 'insert'
                }
                translator: {
                  type: 'TabularTranslator'
                  mappings: [
                    { source: { name: 'MemberNumber' }, sink: { name: 'MemberNumber' } }
                    { source: { name: 'CheckDate' }, sink: { name: 'CheckDate' } }
                    { source: { name: 'Measure' }, sink: { name: 'Measure' } }
                    { source: { name: 'Partner' }, sink: { name: 'Partner' } }
                  ]
                }
              }
            }
            {
              name: 'ApplyHealthChecks'
              type: 'SqlServerStoredProcedure'
              dependsOn: [{ activity: 'CopyCsvToStaging', dependencyConditions: ['Succeeded'] }]
              linkedServiceName: { referenceName: sqlLinked.name, type: 'LinkedServiceReference' }
              typeProperties: { storedProcedureName: 'dbo.usp_ApplyHealthChecks' }
            }
            {
              name: 'ArchiveFiles'
              type: 'Copy'
              dependsOn: [{ activity: 'ApplyHealthChecks', dependencyConditions: ['Succeeded'] }]
              inputs: [{ referenceName: incomingBinary.name, type: 'DatasetReference' }]
              outputs: [
                {
                  referenceName: archiveBinary.name
                  type: 'DatasetReference'
                  parameters: { runFolder: { value: '@formatDateTime(utcNow(), \'yyyy/MM/dd\')', type: 'Expression' } }
                }
              ]
              typeProperties: {
                source: {
                  type: 'BinarySource'
                  storeSettings: { type: 'AzureBlobStorageReadSettings', recursive: false, wildcardFileName: '*.csv', deleteFilesAfterCompletion: true }
                }
                sink: {
                  type: 'BinarySink'
                  storeSettings: { type: 'AzureBlobStorageWriteSettings' }
                }
              }
            }
          ]
        }
      }
    ]
  }
}

// Nightly at 14:00 UTC = 02:00/03:00 NZ. Started by the deploy script (triggers deploy stopped).
resource nightly 'Microsoft.DataFactory/factories/triggers@2018-06-01' = {
  parent: factory
  name: 'Nightly'
  properties: {
    type: 'ScheduleTrigger'
    pipelines: [{ pipelineReference: { referenceName: pipeline.name, type: 'PipelineReference' } }]
    typeProperties: {
      recurrence: {
        frequency: 'Day'
        interval: 1
        startTime: '2026-01-01T14:00:00Z'
        timeZone: 'UTC'
      }
    }
  }
}

var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource adfBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, factory.id, storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
    principalId: factory.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output name string = factory.name
output principalId string = factory.identity.principalId
