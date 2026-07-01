// ---------------------------------------------------------------------------
// User-assigned managed identity module
// Creates the identity the AzRadar apps use to authenticate (no keys) to:
//   * Cosmos DB  — data-plane role granted by this template
//   * Azure OpenAI (LLM) — role must be granted MANUALLY on the AI resource,
//     which is owned by another team / may live in another subscription.
// ---------------------------------------------------------------------------

@description('Azure region for the identity.')
param location string

@description('Name of the user-assigned managed identity.')
param name string

@description('Tags applied to all resources.')
param tags object = {}

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
  tags: tags
}

output resourceId string = uami.id
output principalId string = uami.properties.principalId
output clientId string = uami.properties.clientId
output name string = uami.name
