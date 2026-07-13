// ===========================================================================
// AzRadar — main deployment (resource-group scope)
//
// Provisions the VNet-protected AzRadar platform:
//   * Virtual network with delegated integration subnets + private endpoint subnet
//   * Cosmos DB (serverless, AAD-only) reachable ONLY via a private endpoint
//   * Two B1 Linux App Service plans + two containerized web apps (API, JobHost)
//     with regional VNet integration so they reach Cosmos privately
//   * Cosmos data-plane RBAC for the supplied managed identities
//
// OUT OF SCOPE (owned by another team): the Azure OpenAI / AI Foundry resource.
// The LLM endpoint is reached over the public internet using the same UAMI.
// ===========================================================================

targetScope = 'resourceGroup'

// ----------------------------- Parameters ---------------------------------

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Short name prefix used to derive resource names.')
param namePrefix string = 'az-radar'

@description('Globally-unique Cosmos DB account name.')
param cosmosAccountName string = '${namePrefix}-cosmos-${uniqueString(resourceGroup().id)}'

@description('Cosmos SQL database name.')
param databaseName string = 'az-radar-db'

@description('Globally-unique name for the API web app (azurewebsites.net host).')
param apiAppName string = '${namePrefix}-api'

@description('Globally-unique name for the JobHost web app (azurewebsites.net host).')
param jobAppName string = '${namePrefix}-jobhost'

@description('Docker Hub image for the API (SPA + REST).')
param apiImage string = 'moimhossain/az-radar-api:blue'

@description('Docker Hub image for the JobHost worker.')
param jobImage string = 'moimhossain/az-radar-jobhost:green'

@description('App Service Plan SKU. Basic (B1) or higher required for VNet integration.')
param appServicePlanSku string = 'B1'

@description('Name of the user-assigned managed identity created and used by the apps.')
param managedIdentityName string = '${namePrefix}-uami'

@description('Optional: extra UAMI resource ids to ALSO attach to both web apps (e.g. a subscription-reader identity). The created UAMI is always attached.')
param additionalAppIdentityResourceIds array = []

@description('Optional: extra principal ids to ALSO grant Cosmos DB data-plane access. The created UAMI is always granted.')
param additionalCosmosDataPrincipalIds array = []

@description('When true, deploy an in-tenant VNet-protected Azure OpenAI account (private endpoint) and use it for LLM calls. When false, use the externally provided openAiEndpoint.')
param deployOpenAi bool = false

@description('Externally provided Azure OpenAI endpoint. Used only when deployOpenAi is false. Leave empty when deployOpenAi is true.')
param openAiEndpoint string = ''

@description('Azure OpenAI deployment (model) name the app calls.')
param openAiDeploymentName string = 'gpt-4o'

@description('Globally-unique name for the VNet-protected Azure OpenAI account (only when deployOpenAi is true).')
param openAiAccountName string = '${namePrefix}-openai-${uniqueString(resourceGroup().id)}'

@description('Model name to deploy on the in-tenant Azure OpenAI account.')
param openAiModelName string = 'gpt-4o'

@description('Model version to deploy on the in-tenant Azure OpenAI account.')
param openAiModelVersion string = '2024-08-06'

@description('Deployment SKU for the in-tenant Azure OpenAI model.')
param openAiDeploymentSku string = 'Standard'

@description('Deployment capacity (thousands of TPM) for the in-tenant Azure OpenAI model.')
param openAiDeploymentCapacity int = 20

@description('Tags applied to all resources.')
param tags object = {
  application: 'az-radar'
  managedBy: 'bicep'
}

// ----------------------------- Network ------------------------------------

module network 'modules/network.bicep' = {
  name: 'network'
  params: {
    location: location
    vnetName: '${namePrefix}-vnet'
    tags: tags
  }
}

// ----------------------------- Managed identity ---------------------------

module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    location: location
    name: managedIdentityName
    tags: tags
  }
}

// Always attach/grant the created UAMI; callers may add extra identities.
var appIdentityResourceIds = union([identity.outputs.resourceId], additionalAppIdentityResourceIds)
var cosmosDataPrincipalIds = union([identity.outputs.principalId], additionalCosmosDataPrincipalIds)
var managedIdentityClientId = identity.outputs.clientId

// ----------------------------- Cosmos DB ----------------------------------

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    location: location
    accountName: cosmosAccountName
    databaseName: databaseName
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    privateDnsZoneId: network.outputs.cosmosPrivateDnsZoneId
    tags: tags
  }
}

module cosmosRbac 'modules/cosmos-rbac.bicep' = {
  name: 'cosmos-rbac'
  params: {
    cosmosAccountName: cosmos.outputs.accountName
    principalIds: cosmosDataPrincipalIds
  }
}

// ----------------------------- Azure OpenAI (optional) --------------------

module openai 'modules/openai.bicep' = if (deployOpenAi) {
  name: 'openai'
  params: {
    location: location
    accountName: openAiAccountName
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    vnetId: network.outputs.vnetId
    deploymentName: openAiDeploymentName
    modelName: openAiModelName
    modelVersion: openAiModelVersion
    deploymentSkuName: openAiDeploymentSku
    deploymentCapacity: openAiDeploymentCapacity
    openAiUserPrincipalIds: cosmosDataPrincipalIds
    tags: tags
  }
}

// Use the in-tenant private endpoint when deployed, otherwise the external one.
var effectiveOpenAiEndpoint = deployOpenAi ? openai.outputs.endpoint : openAiEndpoint
var effectiveOpenAiDeployment = deployOpenAi ? openai.outputs.deploymentName : openAiDeploymentName

// ----------------------------- App Service plans --------------------------

module apiPlan 'modules/app-plan.bicep' = {
  name: 'api-plan'
  params: {
    location: location
    name: '${namePrefix}-api-plan'
    skuName: appServicePlanSku
    tags: tags
  }
}

module jobPlan 'modules/app-plan.bicep' = {
  name: 'job-plan'
  params: {
    location: location
    name: '${namePrefix}-job-plan'
    skuName: appServicePlanSku
    tags: tags
  }
}

// ----------------------------- Shared app settings ------------------------

var commonCosmosSettings = [
  { name: 'CosmosDb__Endpoint', value: cosmos.outputs.endpoint }
  { name: 'CosmosDb__DatabaseName', value: databaseName }
  { name: 'CosmosDb__ManagedIdentityClientId', value: managedIdentityClientId }
  { name: 'OpenAi__Endpoint', value: effectiveOpenAiEndpoint }
  { name: 'OpenAi__DeploymentName', value: effectiveOpenAiDeployment }
  { name: 'OpenAi__ManagedIdentityClientId', value: managedIdentityClientId }
  { name: 'WEBSITES_PORT', value: '8080' }
  { name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE', value: 'false' }
  { name: 'WEBSITE_HTTPLOGGING_RETENTION_DAYS', value: '3' }
]

// ----------------------------- Web apps -----------------------------------

module apiApp 'modules/web-app.bicep' = {
  name: 'api-app'
  params: {
    location: location
    name: apiAppName
    planId: apiPlan.outputs.planId
    dockerImage: apiImage
    vnetIntegrationSubnetId: network.outputs.apiSubnetId
    userAssignedIdentityIds: appIdentityResourceIds
    appSettings: commonCosmosSettings
    tags: tags
  }
}

module jobApp 'modules/web-app.bicep' = {
  name: 'job-app'
  params: {
    location: location
    name: jobAppName
    planId: jobPlan.outputs.planId
    dockerImage: jobImage
    vnetIntegrationSubnetId: network.outputs.jobSubnetId
    userAssignedIdentityIds: appIdentityResourceIds
    appSettings: commonCosmosSettings
    tags: tags
  }
}

// ----------------------------- Outputs ------------------------------------

output cosmosAccountName string = cosmos.outputs.accountName
output cosmosEndpoint string = cosmos.outputs.endpoint
output apiHostName string = apiApp.outputs.defaultHostName
output jobHostName string = jobApp.outputs.defaultHostName
output vnetId string = network.outputs.vnetId

// UAMI details — use these to MANUALLY grant the identity access to the Azure
// OpenAI / AI Foundry resource (Cognitive Services OpenAI User role). That
// resource is owned by another team and is not in scope of this template.
output managedIdentityName string = identity.outputs.name
output managedIdentityResourceId string = identity.outputs.resourceId
output managedIdentityClientId string = identity.outputs.clientId
output managedIdentityPrincipalId string = identity.outputs.principalId

// LLM endpoint the apps are configured to use (in-tenant private endpoint when
// deployOpenAi is true, otherwise the externally provided endpoint).
output openAiEndpointInUse string = effectiveOpenAiEndpoint
output openAiDeploymentInUse string = effectiveOpenAiDeployment
output openAiDeployed bool = deployOpenAi
output openAiAccountName string = deployOpenAi ? openai.outputs.accountName : ''
