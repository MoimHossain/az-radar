// ---------------------------------------------------------------------------
// Cosmos DB data-plane RBAC module
// Grants the supplied principals the built-in "Cosmos DB Built-in Data
// Contributor" role (00000000-0000-0000-0000-000000000002) at the account
// scope so they can read/write data using their managed identity (no keys).
// ---------------------------------------------------------------------------

@description('Name of the existing Cosmos DB account.')
param cosmosAccountName string

@description('Object (principal) ids of the identities to grant data access to.')
param principalIds array

// Built-in Cosmos DB Data Contributor role definition id.
var dataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: cosmosAccountName
}

resource roleAssignments 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = [
  for principalId in principalIds: {
    parent: cosmos
    // Deterministic GUID so re-deploys are idempotent.
    name: guid(cosmos.id, principalId, dataContributorRoleId)
    properties: {
      roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${dataContributorRoleId}'
      principalId: principalId
      scope: cosmos.id
    }
  }
]
