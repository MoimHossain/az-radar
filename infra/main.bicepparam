using './main.bicep'

// ---------------------------------------------------------------------------
// Example / test parameters.
//
// This parameter file matches the existing Central US test deployment in
// az-radar-vnet-rg and preserves its private Azure OpenAI account.
// ---------------------------------------------------------------------------

param namePrefix = 'az-radar'
param location = 'centralus'
param cosmosAccountName = 'az-radar-cosmos-ay637nckh3ebc'
param apiAppName = 'azr-api-x8c5i2'
param jobAppName = 'azr-job-x8c5i2'

// Preserve the existing VNet-protected Azure OpenAI deployment.
param deployOpenAi = true
param openAiAccountName = 'az-radar-openai-ay637nckh3ebc'
param openAiDeploymentName = 'gpt-5.1'
param openAiModelName = 'gpt-5.1'
param openAiModelVersion = '2025-11-13'
param openAiDeploymentSku = 'Standard'
param openAiDeploymentCapacity = 30

// Private ACR images. Tags alternate from the currently deployed Docker Hub colors.
param usePrivateContainerRegistry = true
param containerRegistryPublicNetworkAccess = 'Disabled'
param apiImageTag = 'blue'
param jobImageTag = 'green'
param botGatewayImageTag = 'blue'
param dispatchWorkerImageTag = 'blue'
param deployTeamsDispatch = true

// Optional: attach extra identities to both apps (e.g. a subscription-reader
// UAMI used by the JobHost). Leave empty unless required.
// param additionalAppIdentityResourceIds = []
// param additionalCosmosDataPrincipalIds = []
