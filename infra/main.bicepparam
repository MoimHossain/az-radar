using './main.bicep'

// ---------------------------------------------------------------------------
// Example / test parameters.
//
// This template creates its OWN user-assigned managed identity and grants it
// Cosmos DB data-plane access automatically. After deployment you must MANUALLY
// grant that identity access to the Azure OpenAI / AI Foundry resource (owned
// by another team) — see infra/README.md. Use the `managedIdentityClientId`
// / `managedIdentityPrincipalId` deployment outputs for that grant.
// ---------------------------------------------------------------------------

param namePrefix = 'az-radar'

// Public Azure OpenAI endpoint (owned by another team — out of IaC scope).
param openAiEndpoint = 'https://octolamp-foundry26.cognitiveservices.azure.com/'
param openAiDeploymentName = 'gpt-4o'

// Docker Hub images (blue/green tags — no ACR).
param apiImage = 'moimhossain/az-radar-api:blue'
param jobImage = 'moimhossain/az-radar-jobhost:green'

// Optional: attach extra identities to both apps (e.g. a subscription-reader
// UAMI used by the JobHost). Leave empty unless required.
// param additionalAppIdentityResourceIds = []
// param additionalCosmosDataPrincipalIds = []
