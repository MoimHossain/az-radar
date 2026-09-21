targetScope = 'resourceGroup'

@description('Azure region for dispatch compute and messaging.')
param location string = resourceGroup().location

@description('AzRadar resource name prefix.')
param namePrefix string = 'az-radar'

@description('Existing AzRadar Cosmos DB account name.')
param cosmosAccountName string

@description('Existing AzRadar Cosmos SQL database name.')
param cosmosDatabaseName string = 'az-radar-db'

@description('Existing private-endpoint subnet resource ID.')
param privateEndpointSubnetId string

@description('Existing delegated subnet resource ID for the public Bot Gateway.')
param gatewayIntegrationSubnetId string

@description('Existing delegated subnet resource ID for the dispatch worker.')
param workerIntegrationSubnetId string

@description('Existing VNet name used for private DNS links.')
param vnetName string = 'az-radar-vnet'

@description('Principal ID of the existing CloudLens API managed identity that stores PATs.')
param apiPrincipalId string

@description('Globally unique Service Bus namespace name.')
param serviceBusNamespaceName string = '${namePrefix}-dispatch-${uniqueString(resourceGroup().id)}'

@description('Globally unique Azure Bot resource name.')
param botResourceName string = '${namePrefix}-teams-${uniqueString(resourceGroup().id)}'

@description('Globally unique Bot Gateway App Service name.')
param gatewayAppName string = '${namePrefix}-bot-${uniqueString(resourceGroup().id)}'

@description('Globally unique dispatch worker App Service name.')
param workerAppName string = '${namePrefix}-dispatch-${uniqueString(resourceGroup().id)}'

@description('Docker image for the Bot Gateway.')
param gatewayImage string = 'moimhossain/az-radar-bot-gateway:blue'

@description('Docker image for the dispatch worker.')
param workerImage string = 'moimhossain/az-radar-dispatch-worker:blue'

@description('Linux App Service plan SKU.')
param appServicePlanSku string = 'B1'

@description('Tags applied to dispatch resources.')
param tags object = {
  application: 'az-radar'
  component: 'dispatching'
  managedBy: 'bicep'
}

var topicName = 'service-health-delivery'
var teamsSubscriptionName = 'teams-realtime'
var wikiSubscriptionName = 'azure-devops-wiki'
var serviceBusSenderRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
)
var serviceBusReceiverRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
)
var cosmosDataContributorRoleId = '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
var keyVaultSecretsOfficerRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
)
var keyVaultSecretsUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)

resource gatewayIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-bot-gateway-uami'
  location: location
  tags: tags
}

resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-dispatch-worker-uami'
  location: location
  tags: tags
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: serviceBusNamespaceName
  location: location
  tags: tags
  sku: {
    name: 'Premium'
    tier: 'Premium'
    capacity: 1
  }
  properties: {
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
    zoneRedundant: false
    minimumTlsVersion: '1.2'
  }
}

resource deliveryTopic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: topicName
  properties: {
    defaultMessageTimeToLive: 'P14D'
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    enableBatchedOperations: true
    enableExpress: false
    requiresDuplicateDetection: true
    supportOrdering: true
  }
}

resource teamsSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: deliveryTopic
  name: teamsSubscriptionName
  properties: {
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D'
    lockDuration: 'PT5M'
    maxDeliveryCount: 8
  }
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
  parent: deliveryTopic
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

resource serviceBusPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' existing = {
  name: 'privatelink.servicebus.windows.net'
}

resource serviceBusPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: 'pe-${serviceBus.name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'namespace'
        properties: {
          privateLinkServiceId: serviceBus.id
          groupIds: [
            'namespace'
          ]
        }
      }
    ]
  }
}

resource serviceBusDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: serviceBusPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'servicebus'
        properties: {
          privateDnsZoneId: serviceBusPrivateDnsZone.id
        }
      }
    ]
  }
}

resource senderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, workerIdentity.id, serviceBusSenderRoleId)
  scope: serviceBus
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: serviceBusSenderRoleId
  }
}

resource receiverRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, workerIdentity.id, serviceBusReceiverRoleId)
  scope: serviceBus
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: serviceBusReceiverRoleId
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: cosmosAccountName
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' existing = {
  parent: cosmosAccount
  name: cosmosDatabaseName
}

resource conversationReferences 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'teams-conversation-references'
  properties: {
    resource: {
      id: 'teams-conversation-references'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource deliveryAttempts 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'teams-delivery-attempts'
  properties: {
    resource: {
      id: 'teams-delivery-attempts'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource gatewayCosmosRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, gatewayIdentity.id, cosmosDataContributorRoleId)
  properties: {
    principalId: gatewayIdentity.properties.principalId
    roleDefinitionId: cosmosDataContributorRoleId
    scope: cosmosAccount.id
  }
}

resource workerCosmosRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, workerIdentity.id, cosmosDataContributorRoleId)
  properties: {
    principalId: workerIdentity.properties.principalId
    roleDefinitionId: cosmosDataContributorRoleId
    scope: cosmosAccount.id
  }
}

resource wikiKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('${namePrefix}-wiki-${uniqueString(resourceGroup().id)}', 24)
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

resource existingVnet 'Microsoft.Network/virtualNetworks@2023-11-01' existing = {
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
      id: existingVnet.id
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
  name: guid(wikiKeyVault.id, workerIdentity.id, keyVaultSecretsUserRoleId)
  scope: wikiKeyVault
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsUserRoleId
  }
}

resource gatewayPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-bot-plan'
  location: location
  tags: tags
  sku: {
    name: appServicePlanSku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource workerPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-dispatch-plan'
  location: location
  tags: tags
  sku: {
    name: appServicePlanSku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

var gatewayIdentityMap = {
  '${gatewayIdentity.id}': {}
}
var workerIdentityMap = {
  '${gatewayIdentity.id}': {}
  '${workerIdentity.id}': {}
}
var commonContainerSettings = [
  {
    name: 'WEBSITES_PORT'
    value: '8080'
  }
  {
    name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
    value: 'false'
  }
]
var cosmosEndpoint = cosmosAccount.properties.documentEndpoint

resource gatewayApp 'Microsoft.Web/sites@2023-12-01' = {
  name: gatewayAppName
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: gatewayIdentityMap
  }
  properties: {
    serverFarmId: gatewayPlan.id
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    virtualNetworkSubnetId: gatewayIntegrationSubnetId
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      linuxFxVersion: 'DOCKER|${gatewayImage}'
      minTlsVersion: '1.2'
      vnetRouteAllEnabled: true
      appSettings: concat(commonContainerSettings, [
        {
          name: 'Connections__ServiceConnection__Settings__AuthType'
          value: 'UserManagedIdentity'
        }
        {
          name: 'Connections__ServiceConnection__Settings__ClientId'
          value: gatewayIdentity.properties.clientId
        }
        {
          name: 'Connections__ServiceConnection__Settings__Scopes__0'
          value: 'https://api.botframework.com/.default'
        }
        {
          name: 'TokenValidation__Audiences__0'
          value: gatewayIdentity.properties.clientId
        }
        {
          name: 'TokenValidation__TenantId'
          value: tenant().tenantId
        }
        {
          name: 'DispatchingCosmos__Endpoint'
          value: cosmosEndpoint
        }
        {
          name: 'DispatchingCosmos__DatabaseName'
          value: cosmosDatabaseName
        }
        {
          name: 'DispatchingCosmos__ManagedIdentityClientId'
          value: gatewayIdentity.properties.clientId
        }
      ])
    }
  }
}

resource workerApp 'Microsoft.Web/sites@2023-12-01' = {
  name: workerAppName
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: workerIdentityMap
  }
  properties: {
    serverFarmId: workerPlan.id
    httpsOnly: true
    publicNetworkAccess: 'Disabled'
    virtualNetworkSubnetId: workerIntegrationSubnetId
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      linuxFxVersion: 'DOCKER|${workerImage}'
      minTlsVersion: '1.2'
      vnetRouteAllEnabled: true
      appSettings: concat(commonContainerSettings, [
        {
          name: 'Connections__ServiceConnection__Settings__AuthType'
          value: 'UserManagedIdentity'
        }
        {
          name: 'Connections__ServiceConnection__Settings__ClientId'
          value: gatewayIdentity.properties.clientId
        }
        {
          name: 'Connections__ServiceConnection__Settings__Scopes__0'
          value: 'https://api.botframework.com/.default'
        }
        {
          name: 'DispatchingCosmos__Endpoint'
          value: cosmosEndpoint
        }
        {
          name: 'DispatchingCosmos__DatabaseName'
          value: cosmosDatabaseName
        }
        {
          name: 'DispatchingCosmos__ManagedIdentityClientId'
          value: workerIdentity.properties.clientId
        }
        {
          name: 'DispatchingServiceBus__FullyQualifiedNamespace'
          value: '${serviceBus.name}.servicebus.windows.net'
        }
        {
          name: 'DispatchingServiceBus__TopicName'
          value: topicName
        }
        {
          name: 'DispatchingServiceBus__TeamsSubscriptionName'
          value: teamsSubscriptionName
        }
        {
          name: 'DispatchingServiceBus__WikiSubscriptionName'
          value: wikiSubscriptionName
        }
        {
          name: 'DispatchingServiceBus__ManagedIdentityClientId'
          value: workerIdentity.properties.clientId
        }
        {
          name: 'AzureDevOpsWiki__KeyVaultUri'
          value: wikiKeyVault.properties.vaultUri
        }
        {
          name: 'AzureDevOpsWiki__ManagedIdentityClientId'
          value: workerIdentity.properties.clientId
        }
      ])
    }
  }
}

resource azureBot 'Microsoft.BotService/botServices@2023-09-15-preview' = {
  name: botResourceName
  location: 'global'
  tags: tags
  kind: 'azurebot'
  sku: {
    name: 'F0'
  }
  properties: {
    displayName: 'CloudLens Service Health Alerts'
    disableLocalAuth: true
    endpoint: 'https://${gatewayApp.properties.defaultHostName}/api/messages'
    msaAppId: gatewayIdentity.properties.clientId
    msaAppMSIResourceId: gatewayIdentity.id
    msaAppTenantId: tenant().tenantId
    msaAppType: 'UserAssignedMSI'
    publicNetworkAccess: 'Enabled'
    isStreamingSupported: false
  }
}

resource teamsChannel 'Microsoft.BotService/botServices/channels@2023-09-15-preview' = {
  parent: azureBot
  name: 'MsTeamsChannel'
  location: 'global'
  kind: 'azurebot'
  properties: {
    channelName: 'MsTeamsChannel'
    properties: {
      acceptedTerms: true
      enableCalling: false
      isEnabled: true
    }
  }
}

output botClientId string = gatewayIdentity.properties.clientId
output botResourceName string = azureBot.name
output botGatewayHostName string = gatewayApp.properties.defaultHostName
output workerAppName string = workerApp.name
output serviceBusNamespace string = serviceBus.name
output serviceBusTopic string = deliveryTopic.name
output teamsSubscription string = teamsSubscription.name
output wikiSubscription string = wikiSubscription.name
output wikiKeyVaultName string = wikiKeyVault.name
output wikiKeyVaultUri string = wikiKeyVault.properties.vaultUri
