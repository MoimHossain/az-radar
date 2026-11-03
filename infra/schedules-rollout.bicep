@description('Existing Cosmos account. No other infrastructure is redeployed.')
param accountName string

param databaseName string = 'az-radar-db'

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: accountName
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' existing = {
  parent: account
  name: databaseName
}

resource schedules 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'schedule-control'
  properties: {
    resource: {
      id: 'schedule-control'
      partitionKey: {
        paths: ['/scheduleId']
        kind: 'Hash'
      }
      defaultTtl: -1
    }
  }
}

output containerId string = schedules.id
