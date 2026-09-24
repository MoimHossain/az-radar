targetScope = 'resourceGroup'

@description('Azure region for the container registry and private endpoint.')
param location string

@description('AzRadar resource name prefix.')
@minLength(2)
param namePrefix string

@description('Resource ID of the VNet linked to the ACR private DNS zone.')
param vnetId string

@description('Resource ID of the subnet that hosts private endpoints.')
param privateEndpointSubnetId string

@allowed([
  'Enabled'
  'Disabled'
])
@description('ACR public network access. Use Enabled only while publishing bootstrap images.')
param publicNetworkAccess string = 'Disabled'

@description('Managed identity principal IDs granted AcrPull on this registry.')
param pullPrincipalIds array = []

@description('Tags applied to all resources.')
param tags object = {}

var registryName = 'azr${uniqueString(namePrefix, resourceGroup().id)}'
var privateDnsZoneName = 'privatelink.azurecr.io'
var acrPullRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

resource registry 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: 'Premium'
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    dataEndpointEnabled: true
    networkRuleBypassOptions: 'AzureServices'
    networkRuleSet: {
      defaultAction: publicNetworkAccess == 'Enabled' ? 'Allow' : 'Deny'
      ipRules: []
    }
    policies: {
      exportPolicy: {
        // ACR requires export policy to remain enabled while public access is
        // temporarily enabled for hosted ACR Tasks during bootstrap.
        status: publicNetworkAccess == 'Disabled' ? 'disabled' : 'enabled'
      }
    }
    publicNetworkAccess: publicNetworkAccess
  }
}

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: privateDnsZoneName
  location: 'global'
  tags: tags
}

resource privateDnsZoneVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: privateDnsZone
  name: '${registry.name}-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnetId
    }
  }
}

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2024-07-01' = {
  name: 'pe-${registry.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'registry'
        properties: {
          privateLinkServiceId: registry.id
          groupIds: [
            'registry'
          ]
        }
      }
    ]
  }
}

resource privateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-07-01' = {
  parent: privateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'registry'
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}

resource acrPullAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in pullPrincipalIds: {
    name: guid(registry.id, principalId, acrPullRoleDefinitionId)
    scope: registry
    properties: {
      principalId: principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: acrPullRoleDefinitionId
    }
  }
]

output name string = registry.name
output resourceId string = registry.id
output loginServer string = registry.properties.loginServer
output privateEndpointId string = privateEndpoint.id
