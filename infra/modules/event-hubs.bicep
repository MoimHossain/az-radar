// ---------------------------------------------------------------------------
// Single-region Event Hubs Premium ingestion boundary for Azure Service Health.
// Azure Monitor writes through a dedicated namespace authorization rule.
// AzRadar consumers use managed identity over a private endpoint.
// ---------------------------------------------------------------------------

@description('Azure region for Event Hubs.')
param location string

@description('Globally unique Event Hubs namespace name.')
param namespaceName string

@description('Event Hub name receiving Service Health Activity Log records.')
param eventHubName string = 'service-health'

@allowed([
  'Standard'
  'Premium'
])
@description('Event Hubs namespace pricing tier.')
param skuName string = 'Premium'

@description('Private endpoint subnet resource id.')
param privateEndpointSubnetId string

@description('Virtual network resource id for private DNS linking.')
param vnetId string

@description('Managed identity principal ids that consume events.')
param receiverPrincipalIds array

@description('Managed identity principal ids that publish events.')
param senderPrincipalIds array = []

@description('Dedicated provisioning UAMI principal id.')
param provisioningPrincipalId string

@description('Tags applied to all resources.')
param tags object = {}

var dataReceiverRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'a638d3c7-ab3a-418d-83e6-5f17a39d4fde'
)
var dataSenderRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '2b629674-e913-4c01-ae53-ef4638d8f975'
)
var provisioningRoleDefinitionName = guid(
  subscription().id,
  resourceGroup().id,
  'az-radar-service-health-eventhub-provisioner'
)

resource eventHubsNamespace 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
    capacity: 1
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    zoneRedundant: false
  }
}

resource networkRules 'Microsoft.EventHub/namespaces/networkRuleSets@2024-01-01' = {
  parent: eventHubsNamespace
  name: 'default'
  properties: {
    defaultAction: 'Deny'
    publicNetworkAccess: 'Disabled'
    trustedServiceAccessEnabled: true
    ipRules: []
    virtualNetworkRules: []
  }
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = {
  parent: eventHubsNamespace
  name: eventHubName
  properties: {
    partitionCount: 4
    messageRetentionInDays: 7
  }
}

resource liveConsumerGroup 'Microsoft.EventHub/namespaces/eventhubs/consumergroups@2024-01-01' = {
  parent: eventHub
  name: 'azradar-live'
  properties: {}
}

resource replayConsumerGroup 'Microsoft.EventHub/namespaces/eventhubs/consumergroups@2024-01-01' = {
  parent: eventHub
  name: 'azradar-replay'
  properties: {}
}

resource azureMonitorAuthorizationRule 'Microsoft.EventHub/namespaces/authorizationRules@2024-01-01' = {
  parent: eventHubsNamespace
  name: 'azure-monitor-service-health'
  properties: {
    rights: [
      'Listen'
      'Send'
      'Manage'
    ]
  }
}

resource receiverRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in receiverPrincipalIds: {
    scope: eventHubsNamespace
    name: guid(eventHubsNamespace.id, principalId, dataReceiverRoleDefinitionId)
    properties: {
      roleDefinitionId: dataReceiverRoleDefinitionId
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]

resource senderRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in senderPrincipalIds: {
    scope: eventHubsNamespace
    name: guid(eventHubsNamespace.id, principalId, dataSenderRoleDefinitionId)
    properties: {
      roleDefinitionId: dataSenderRoleDefinitionId
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]

resource provisioningRoleDefinition 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: provisioningRoleDefinitionName
  properties: {
    roleName: 'AzRadar Service Health Event Hub Provisioner'
    description: 'Allows AzRadar to validate the dedicated Azure Monitor Event Hub authorization rule.'
    type: 'CustomRole'
    assignableScopes: [
      resourceGroup().id
    ]
    permissions: [
      {
        actions: [
          'Microsoft.EventHub/namespaces/read'
          'Microsoft.EventHub/namespaces/eventhubs/read'
          'Microsoft.EventHub/namespaces/authorizationRules/read'
          'Microsoft.EventHub/namespaces/authorizationRules/listKeys/action'
        ]
        notActions: []
        dataActions: []
        notDataActions: []
      }
    ]
  }
}

resource provisioningRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: azureMonitorAuthorizationRule
  name: guid(azureMonitorAuthorizationRule.id, provisioningPrincipalId, provisioningRoleDefinition.id)
  properties: {
    roleDefinitionId: provisioningRoleDefinition.id
    principalId: provisioningPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.servicebus.windows.net'
  location: 'global'
  tags: tags
}

resource privateDnsZoneVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: privateDnsZone
  name: '${namespaceName}-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnetId
    }
  }
}

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pe-${namespaceName}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'pe-${namespaceName}-conn'
        properties: {
          privateLinkServiceId: eventHubsNamespace.id
          groupIds: [
            'namespace'
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
        name: 'eventhubs'
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}

output namespaceName string = eventHubsNamespace.name
output namespaceId string = eventHubsNamespace.id
output fullyQualifiedNamespace string = '${eventHubsNamespace.name}.servicebus.windows.net'
output eventHubName string = eventHub.name
output authorizationRuleId string = azureMonitorAuthorizationRule.id
