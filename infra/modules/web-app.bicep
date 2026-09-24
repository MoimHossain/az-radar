// ---------------------------------------------------------------------------
// Web App (Linux container) module
// Deploys a containerized App Service that:
//   * pulls its image from ACR with a user-assigned managed identity,
//   * is assigned one or more user-assigned managed identities,
//   * is integrated into a delegated subnet with all outbound traffic routed
//     through the VNet (so it reaches Cosmos DB via the private endpoint).
// ---------------------------------------------------------------------------

@description('Azure region for the web app.')
param location string

@description('Web app name (also the default host name prefix).')
param name string

@description('Resource id of the App Service Plan.')
param planId string

@description('Container image reference.')
param dockerImage string

@description('Client ID of the user-assigned identity used for ACR pulls. Empty keeps public-registry behavior.')
param containerRegistryManagedIdentityClientId string = ''

@description('Resource id of the delegated subnet used for VNet integration.')
param vnetIntegrationSubnetId string

@description('Resource ids of user-assigned managed identities to attach.')
param userAssignedIdentityIds array

@description('Application settings as an array of { name, value } objects.')
param appSettings array

@description('Tags applied to all resources.')
param tags object = {}

var identityObject = {
  type: 'UserAssigned'
  userAssignedIdentities: reduce(
    userAssignedIdentityIds,
    {},
    (acc, id) => union(acc, { '${id}': {} })
  )
}

resource site 'Microsoft.Web/sites@2024-11-01' = {
  name: name
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: identityObject
  properties: {
    serverFarmId: planId
    httpsOnly: true
    // Regional VNet integration.
    virtualNetworkSubnetId: vnetIntegrationSubnetId
    outboundVnetRouting: {
      allTraffic: true
      // Private ACR image pulls must traverse regional VNet integration.
      imagePullTraffic: !empty(containerRegistryManagedIdentityClientId)
    }
    siteConfig: union({
      linuxFxVersion: 'DOCKER|${dockerImage}'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: appSettings
    }, empty(containerRegistryManagedIdentityClientId) ? {} : {
      acrUseManagedIdentityCreds: true
      acrUserManagedIdentityID: containerRegistryManagedIdentityClientId
    })
  }
}

output siteName string = site.name
output defaultHostName string = site.properties.defaultHostName
