using './main.bicep'

// ---------------------------------------------------------------------------
// Scenario: deploy an IN-TENANT, VNet-protected Azure OpenAI (private endpoint)
// and use it for the platform's LLM calls (instead of an external endpoint).
//
// This template creates its own UAMI and grants it BOTH the Cosmos data-plane
// role AND the "Cognitive Services OpenAI User" role on the created OpenAI
// account, so no manual role assignment is needed for the LLM.
// ---------------------------------------------------------------------------

param namePrefix = 'az-radar'

// Turn on the in-tenant, network-isolated Azure OpenAI.
param deployOpenAi = true

// Model deployment (must have quota + be non-deprecated in the target region).
// Validated working in centralus. gpt-4o versions are entering deprecation, so
// this uses gpt-5.1. Any capable chat model with regional Standard quota works;
// check availability with:
//   az cognitiveservices model list --location <region> --query "[?kind=='OpenAI']"
param openAiDeploymentName = 'gpt-5.1'
param openAiModelName = 'gpt-5.1'
param openAiModelVersion = '2025-11-13'
param openAiDeploymentSku = 'Standard'
param openAiDeploymentCapacity = 30

// Docker Hub images (blue/green tags — no ACR). The API image includes the
// /api/health/llm connectivity probe used to validate the deployment.
param apiImage = 'moimhossain/az-radar-api:vnet-llm'
param jobImage = 'moimhossain/az-radar-jobhost:green'
