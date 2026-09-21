targetScope = 'resourceGroup'

@description('Azure region for the Key Vault.')
param location string = resourceGroup().location

@description('Existing Service Bus namespace.')
param serviceBusNamespaceName string

@description('Existing VNet name.')
param vnetName string = 'az-radar-vnet'

@description('Existing private endpoint subnet resource ID.')
param privateEndpointSubnetId string

@description('Existing Service Bus topic.')
param topicName string = 'service-health-delivery'

@description('CloudLens API managed identity principal ID.')
param apiPrincipalId string

@description('CloudLens dispatch worker managed identity principal ID.')
param workerPrincipalId string

@description('Key Vault name for Azure DevOps Wiki PAT storage.')
param keyVaultName string = take('az-radar-wiki-${uniqueString(resourceGroup().id)}', 24)

@description('Tags applied to additive wiki dispatch resources.')
param tags object = {
  application: 'az-radar'
  component: 'wiki-dispatch'
  managedBy: 'bicep'
}

var teamsSubscriptionName = 'teams-realtime'
var wikiSubscriptionName = 'azure-devops-wiki'
var keyVaultSecretsOfficerRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
)
var keyVaultSecretsUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusNamespaceName
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' existing = {
  parent: serviceBus
  name: topicName
}

resource teamsSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' existing = {
  parent: topic
  name: teamsSubscriptionName
}

resource teamsSubscriptionRule 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: teamsSubscription
  name: 'teams-target-filter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'destinationType = \'teams-bot\''
      compatibilityLevel: 20
    }
  }
}

resource wikiSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic
  name: wikiSubscriptionName
  properties: {
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D'
    lockDuration: 'PT5M'
    maxDeliveryCount: 8
  }
}

resource wikiSubscriptionRule 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: wikiSubscription
  name: 'wiki-target-filter'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'destinationType = \'azure-devops-wiki\''
      compatibilityLevel: 20
    }
  }
}

resource wikiKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
    softDeleteRetentionInDays: 90
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' existing = {
  name: vnetName
}

resource keyVaultPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.vaultcore.azure.net'
  location: 'global'
  tags: tags
}

resource keyVaultDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: keyVaultPrivateDnsZone
  name: '${wikiKeyVault.name}-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource keyVaultPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pe-${wikiKeyVault.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'vault'
        properties: {
          privateLinkServiceId: wikiKeyVault.id
          groupIds: [
            'vault'
          ]
        }
      }
    ]
  }
}

resource keyVaultDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: keyVaultPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'keyvault'
        properties: {
          privateDnsZoneId: keyVaultPrivateDnsZone.id
        }
      }
    ]
  }
}

resource apiKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(wikiKeyVault.id, apiPrincipalId, keyVaultSecretsOfficerRoleId)
  scope: wikiKeyVault
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsOfficerRoleId
  }
}

resource workerKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(wikiKeyVault.id, workerPrincipalId, keyVaultSecretsUserRoleId)
  scope: wikiKeyVault
  properties: {
    principalId: workerPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsUserRoleId
  }
}

output wikiSubscriptionName string = wikiSubscription.name
output keyVaultName string = wikiKeyVault.name
output keyVaultUri string = wikiKeyVault.properties.vaultUri
