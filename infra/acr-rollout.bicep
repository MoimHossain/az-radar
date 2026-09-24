targetScope = 'resourceGroup'

@description('Azure region for ACR and its private endpoint.')
param location string = resourceGroup().location

@description('AzRadar resource name prefix.')
param namePrefix string = 'az-radar'

@description('Existing AzRadar VNet name.')
param vnetName string = '${namePrefix}-vnet'

@description('Existing subnet name used for private endpoints.')
param privateEndpointSubnetName string = 'snet-private-endpoints'

@description('Existing UAMI used by the API and JobHost.')
param runtimeIdentityName string = '${namePrefix}-uami'

@description('Existing UAMI used by the dispatch worker.')
param dispatchWorkerIdentityName string = '${namePrefix}-dispatch-worker-uami'

@description('Existing UAMI used by the CloudLens bot gateway.')
param botGatewayIdentityName string = '${namePrefix}-bot-gateway-uami'

@description('Include the existing CloudLens bot gateway identity in AcrPull assignments.')
param deployTeamsDispatch bool = true

@allowed([
  'Enabled'
  'Disabled'
])
@description('ACR public access. Use Enabled only during ACR Tasks image publication.')
param publicNetworkAccess string = 'Disabled'

@description('Tags applied to ACR resources.')
param tags object = {
  application: 'az-radar'
  component: 'container-registry'
  managedBy: 'bicep'
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: vnetName
}

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: vnet
  name: privateEndpointSubnetName
}

resource runtimeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: runtimeIdentityName
}

resource dispatchWorkerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: dispatchWorkerIdentityName
}

resource botGatewayIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = if (deployTeamsDispatch) {
  name: botGatewayIdentityName
}

var pullPrincipalIds = concat([
  runtimeIdentity.properties.principalId
  dispatchWorkerIdentity.properties.principalId
], deployTeamsDispatch ? [
  botGatewayIdentity!.properties.principalId
] : [])

module containerRegistry 'modules/container-registry.bicep' = {
  name: 'container-registry'
  params: {
    location: location
    namePrefix: namePrefix
    vnetId: vnet.id
    privateEndpointSubnetId: privateEndpointSubnet.id
    publicNetworkAccess: publicNetworkAccess
    pullPrincipalIds: pullPrincipalIds
    tags: tags
  }
}

output name string = containerRegistry.outputs.name
output resourceId string = containerRegistry.outputs.resourceId
output loginServer string = containerRegistry.outputs.loginServer
output privateEndpointId string = containerRegistry.outputs.privateEndpointId
