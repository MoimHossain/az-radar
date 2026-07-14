// ---------------------------------------------------------------------------
// Cosmos DB module
// Creates a serverless, AAD-only Cosmos DB account that is fully protected by
// the virtual network: public network access is disabled and connectivity is
// only available through a private endpoint. The SQL database and all required
// containers are pre-created at the control plane so that the application
// (which authenticates with a data-plane RBAC role that cannot create
// resources) can connect successfully.
// ---------------------------------------------------------------------------

@description('Azure region for the Cosmos DB account.')
param location string

@description('Globally-unique Cosmos DB account name.')
param accountName string

@description('Cosmos SQL database name.')
param databaseName string = 'az-radar-db'

@description('Resource id of the subnet that hosts the private endpoint.')
param privateEndpointSubnetId string

@description('Resource id of the private DNS zone (privatelink.documents.azure.com).')
param privateDnsZoneId string

@description('Tags applied to all resources.')
param tags object = {}

// Container definitions: name + partition key path.
var containers = [
  { name: 'crawl-jobs', partitionKey: '/id' }
  { name: 'feed-items', partitionKey: '/id' }
  { name: 'change-feed-leases', partitionKey: '/id' }
  { name: 'watchlist', partitionKey: '/id' }
  { name: 'repo-watchlist', partitionKey: '/id' }
  { name: 'doc-insights', partitionKey: '/id' }
  { name: 'app-config', partitionKey: '/id' }
  { name: 'blast-radius-results', partitionKey: '/id' }
  { name: 'job-diagnostics', partitionKey: '/jobId' }
]

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: accountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    // Enforce identity-based (AAD) auth only — no account keys.
    disableLocalAuth: true
    // VNet protection: block all public access; reachable only via private endpoint.
    publicNetworkAccess: 'Disabled'
    enableAutomaticFailover: false
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
  }
}

resource sqlDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmos
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

resource sqlContainers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [
  for c in containers: {
    parent: sqlDatabase
    name: c.name
    properties: {
      resource: {
        id: c.name
        partitionKey: {
          paths: [
            c.partitionKey
          ]
          kind: 'Hash'
        }
      }
    }
  }
]

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pe-${accountName}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'pe-${accountName}-conn'
        properties: {
          privateLinkServiceId: cosmos.id
          groupIds: [
            'Sql'
          ]
        }
      }
    ]
  }
}

resource privateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: privateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'documents'
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

output accountName string = cosmos.name
output accountId string = cosmos.id
output endpoint string = cosmos.properties.documentEndpoint
output databaseName string = sqlDatabase.name
