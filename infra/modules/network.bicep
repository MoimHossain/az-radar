// ---------------------------------------------------------------------------
// Network module
// Creates the VNet, three subnets (private endpoints, API integration,
// JobHost integration) and the private DNS zone used by the Cosmos DB
// private endpoint.
// ---------------------------------------------------------------------------

@description('Azure region for the VNet.')
param location string

@description('Name of the virtual network.')
param vnetName string

@description('Address space for the virtual network.')
param vnetAddressPrefix string = '10.20.0.0/16'

@description('Address prefix for the private endpoints subnet.')
param privateEndpointSubnetPrefix string = '10.20.0.0/24'

@description('Address prefix for the API VNet-integration subnet.')
param apiSubnetPrefix string = '10.20.1.0/24'

@description('Address prefix for the JobHost VNet-integration subnet.')
param jobSubnetPrefix string = '10.20.2.0/24'

@description('Tags applied to all resources.')
param tags object = {}

var privateEndpointSubnetName = 'snet-private-endpoints'
var apiSubnetName = 'snet-app-api'
var jobSubnetName = 'snet-app-job'

// Private DNS zone name for Cosmos DB (SQL API) private endpoints.
var cosmosPrivateDnsZoneName = 'privatelink.documents.azure.com'

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: privateEndpointSubnetName
        properties: {
          addressPrefixes: [
            privateEndpointSubnetPrefix
          ]
          // Required so a private endpoint NIC can be placed in this subnet.
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: apiSubnetName
        properties: {
          addressPrefixes: [
            apiSubnetPrefix
          ]
          // Delegation enables App Service regional VNet integration.
          delegations: [
            {
              name: 'webapp-delegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: jobSubnetName
        properties: {
          addressPrefixes: [
            jobSubnetPrefix
          ]
          delegations: [
            {
              name: 'webapp-delegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
    ]
  }
}

resource cosmosPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: cosmosPrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource cosmosDnsZoneVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: cosmosPrivateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output privateEndpointSubnetId string = '${vnet.id}/subnets/${privateEndpointSubnetName}'
output apiSubnetId string = '${vnet.id}/subnets/${apiSubnetName}'
output jobSubnetId string = '${vnet.id}/subnets/${jobSubnetName}'
output cosmosPrivateDnsZoneId string = cosmosPrivateDnsZone.id
