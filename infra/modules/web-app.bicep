// ---------------------------------------------------------------------------
// Web App (Linux container) module
// Deploys a containerized App Service that:
//   * pulls its image from Docker Hub (public, no registry credentials),
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

@description('Docker image reference, e.g. moimhossain/az-radar-api:blue')
param dockerImage string

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

resource site 'Microsoft.Web/sites@2023-12-01' = {
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
    siteConfig: {
      linuxFxVersion: 'DOCKER|${dockerImage}'
      // Route ALL outbound app traffic through the VNet so private-endpoint
      // DNS for Cosmos resolves and is reachable.
      vnetRouteAllEnabled: true
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: appSettings
    }
  }
}

output siteName string = site.name
output defaultHostName string = site.properties.defaultHostName
