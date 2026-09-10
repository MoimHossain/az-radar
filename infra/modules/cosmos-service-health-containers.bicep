@description('Existing Cosmos DB account name.')
param accountName string

@description('Existing Cosmos SQL database name.')
param databaseName string

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: accountName
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' existing = {
  parent: cosmosAccount
  name: databaseName
}

resource subscriptionsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'service-health-subscriptions'
  properties: {
    resource: {
      id: 'service-health-subscriptions'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource channelsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'service-health-channels'
  properties: {
    resource: {
      id: 'service-health-channels'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource eventsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'service-health-events'
  properties: {
    resource: {
      id: 'service-health-events'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource deliveryIntentsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'service-health-delivery-intents'
  properties: {
    resource: {
      id: 'service-health-delivery-intents'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource checkpointsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'service-health-checkpoints'
  properties: {
    resource: {
      id: 'service-health-checkpoints'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource quarantineContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'service-health-quarantine'
  properties: {
    resource: {
      id: 'service-health-quarantine'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

output subscriptionsContainerName string = subscriptionsContainer.name
output channelsContainerName string = channelsContainer.name
output eventsContainerName string = eventsContainer.name
output deliveryIntentsContainerName string = deliveryIntentsContainer.name
output checkpointsContainerName string = checkpointsContainer.name
output quarantineContainerName string = quarantineContainer.name
