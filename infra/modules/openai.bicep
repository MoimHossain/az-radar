// ---------------------------------------------------------------------------
// Azure OpenAI module (OPTIONAL — VNet protected)
// Creates a Cognitive Services (OpenAI) account that is only reachable through
// a private endpoint:
//   * public network access disabled, network ACLs default-deny
//   * AAD-only (local keys disabled) — callers use managed identity
//   * a model deployment (default gpt-4o)
//   * private endpoint (groupId 'account') + privatelink.openai.azure.com DNS
//   * "Cognitive Services OpenAI User" role granted to the supplied identities
//
// This lets the platform run its LLM calls against an in-tenant, network-
// isolated endpoint instead of an externally provided public one.
// ---------------------------------------------------------------------------

@description('Azure region for the OpenAI account.')
param location string

@description('Globally-unique Cognitive Services (OpenAI) account name. Also used as the custom subdomain (required for private endpoints + AAD).')
param accountName string

@description('Resource id of the subnet that hosts the private endpoint.')
param privateEndpointSubnetId string

@description('Resource id of the virtual network to link the private DNS zone to.')
param vnetId string

@description('Model deployment name (what the app calls).')
param deploymentName string = 'gpt-4o'

@description('Model name.')
param modelName string = 'gpt-4o'

@description('Model version.')
param modelVersion string = '2024-08-06'

@description('Deployment SKU name.')
param deploymentSkuName string = 'Standard'

@description('Deployment capacity (thousands of tokens per minute).')
param deploymentCapacity int = 20

@description('Object (principal) ids granted the Cognitive Services OpenAI User role.')
param openAiUserPrincipalIds array = []

@description('Tags applied to all resources.')
param tags object = {}

// Built-in "Cognitive Services OpenAI User" role.
var openAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
var openAiPrivateDnsZoneName = 'privatelink.openai.azure.com'

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // Custom subdomain is mandatory for private endpoints and AAD token auth.
    customSubDomainName: accountName
    // AAD-only: disable account keys entirely (no-keys posture).
    disableLocalAuth: true
    // VNet protection: no public access; reachable only via private endpoint.
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
    }
  }
}

// Model deployment (control-plane op — works even with public access disabled).
resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: deploymentName
  sku: {
    name: deploymentSkuName
    capacity: deploymentCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

resource openAiPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: openAiPrivateDnsZoneName
  location: 'global'
  tags: tags
}

resource openAiDnsZoneVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: openAiPrivateDnsZone
  name: '${accountName}-link'
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
          privateLinkServiceId: account.id
          groupIds: [
            'account'
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
        name: 'openai'
        properties: {
          privateDnsZoneId: openAiPrivateDnsZone.id
        }
      }
    ]
  }
}

// Grant the supplied identities data-plane access to call the model.
resource roleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in openAiUserPrincipalIds: {
    name: guid(account.id, principalId, openAiUserRoleId)
    scope: account
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', openAiUserRoleId)
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]

output accountName string = account.name
output accountId string = account.id
output endpoint string = account.properties.endpoint
output deploymentName string = deployment.name
