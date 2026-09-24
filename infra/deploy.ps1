<#
.SYNOPSIS
    Deploys the VNet-protected AzRadar platform to a resource group.

.DESCRIPTION
    Creates (if needed) the target resource group and deploys infra/main.bicep
    using infra/main.bicepparam. The Azure OpenAI / AI Foundry resource is NOT
    created here — it is owned by another team and reached over the public
    internet using the same user-assigned managed identity.

.PARAMETER ResourceGroup
    Target resource group name.

.PARAMETER Location
    Azure region (default: centralus).

.PARAMETER ParameterFile
    Bicep parameter file (default: infra/main.bicepparam).

.EXAMPLE
    ./deploy.ps1 -ResourceGroup az-radar-vnet-rg -Location centralus
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [string]$Location = 'centralus',

    [string]$ParameterFile = "$PSScriptRoot/main.bicepparam"
)

$ErrorActionPreference = 'Stop'
$template = "$PSScriptRoot/main.bicep"

Write-Host "==> Ensuring resource group '$ResourceGroup' ($Location)" -ForegroundColor Cyan
az group create --name $ResourceGroup --location $Location --output none

Write-Host "==> Validating template" -ForegroundColor Cyan
az deployment group validate `
    --resource-group $ResourceGroup `
    --template-file $template `
    --parameters $ParameterFile `
    --output none

Write-Host "==> Previewing changes" -ForegroundColor Cyan
az deployment group what-if `
    --resource-group $ResourceGroup `
    --template-file $template `
    --parameters $ParameterFile `
    --no-pretty-print `
    --output json | Out-Null

$deploymentName = "az-radar-$(Get-Date -Format 'yyyyMMddHHmmss')"
Write-Host "==> Deploying ($deploymentName)" -ForegroundColor Cyan
az deployment group create `
    --name $deploymentName `
    --resource-group $ResourceGroup `
    --template-file $template `
    --parameters $ParameterFile `
    --output json | Out-Null

Write-Host "==> Deployment outputs" -ForegroundColor Cyan
az deployment group show `
    --name $deploymentName `
    --resource-group $ResourceGroup `
    --query properties.outputs `
    --output json

Write-Host "==> Done. Restart the web apps if they started before VNet/RBAC was ready." -ForegroundColor Green
