targetScope = 'resourceGroup'

@description('Azure region matching the existing AzRadar resource group.')
param location string = resourceGroup().location

@description('Existing AzRadar virtual network name.')
param vnetName string = 'az-radar-vnet'

@description('Existing subnet used for private endpoints.')
param privateEndpointSubnetName string = 'snet-private-endpoints'

@description('Existing AzRadar runtime managed identity name.')
param runtimeIdentityName string = 'az-radar-uami'

@description('Dedicated Service Health provisioning identity name.')
param provisioningIdentityName string = 'az-radar-service-health-provisioner'

@description('Globally unique Event Hubs namespace name.')
param eventHubsNamespaceName string = 'az-radar-service-health-${uniqueString(subscription().id, resourceGroup().id)}'

@allowed([
  'Standard'
  'Premium'
])
@description('Event Hubs namespace tier. Standard is used for the Central US pilot because new Premium namespaces are restricted there.')
param eventHubsSkuName string = 'Standard'

@description('Existing Cosmos DB account name.')
param cosmosAccountName string

@description('Existing Cosmos SQL database name.')
param cosmosDatabaseName string = 'az-radar-db'

@description('Pilot subscription diagnostic setting name.')
param diagnosticSettingName string = 'az-radar-service-health'

@description('Tags applied to new resources.')
param tags object = {
  application: 'az-radar'
  component: 'service-health'
  environment: 'pilot'
}

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' existing = {
  name: vnetName
}

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-11-01' existing = {
  parent: vnet
  name: privateEndpointSubnetName
}

resource runtimeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: runtimeIdentityName
}

module provisioningIdentity 'modules/identity.bicep' = {
  name: 'service-health-provisioning-identity'
  params: {
    location: location
    name: provisioningIdentityName
    tags: tags
  }
}

module eventHubs 'modules/event-hubs.bicep' = {
  name: 'service-health-event-hubs'
  params: {
    location: location
    namespaceName: eventHubsNamespaceName
    eventHubName: 'service-health'
    skuName: eventHubsSkuName
    privateEndpointSubnetId: privateEndpointSubnet.id
    vnetId: vnet.id
    receiverPrincipalIds: [
      runtimeIdentity.properties.principalId
    ]
    senderPrincipalIds: [
      runtimeIdentity.properties.principalId
    ]
    provisioningPrincipalId: provisioningIdentity.outputs.principalId
    tags: tags
  }
}

module cosmosContainers 'modules/cosmos-service-health-containers.bicep' = {
  name: 'service-health-cosmos-containers'
  params: {
    accountName: cosmosAccountName
    databaseName: cosmosDatabaseName
  }
}

module pilotSubscription 'modules/service-health-subscription.bicep' = {
  name: 'service-health-pilot-subscription'
  scope: subscription(subscription().subscriptionId)
  params: {
    provisioningPrincipalId: provisioningIdentity.outputs.principalId
    diagnosticSettingName: diagnosticSettingName
    eventHubAuthorizationRuleId: eventHubs.outputs.authorizationRuleId
    eventHubName: eventHubs.outputs.eventHubName
  }
}

output provisioningIdentityClientId string = provisioningIdentity.outputs.clientId
output provisioningIdentityPrincipalId string = provisioningIdentity.outputs.principalId
output eventHubsNamespace string = eventHubs.outputs.fullyQualifiedNamespace
output eventHubName string = eventHubs.outputs.eventHubName
output eventHubAuthorizationRuleId string = eventHubs.outputs.authorizationRuleId
output diagnosticSettingName string = pilotSubscription.outputs.diagnosticSettingName
output subscriptionsContainerName string = cosmosContainers.outputs.subscriptionsContainerName
output channelsContainerName string = cosmosContainers.outputs.channelsContainerName
output eventsContainerName string = cosmosContainers.outputs.eventsContainerName
output deliveryIntentsContainerName string = cosmosContainers.outputs.deliveryIntentsContainerName
output checkpointsContainerName string = cosmosContainers.outputs.checkpointsContainerName
output quarantineContainerName string = cosmosContainers.outputs.quarantineContainerName
