<#
.SYNOPSIS
    Publishes AzRadar images to private ACR and switches the existing App Services.

.DESCRIPTION
    Provisions ACR through Bicep with temporary public access, builds four images
    through ACR Tasks, configures keyless managed-identity pulls over VNet
    integration, disables ACR public access, restarts the apps, and smoke-tests
    the API health endpoint.
#>
[CmdletBinding()]
param(
    [string]$SubscriptionId = '5e22addc-6168-4683-afd0-789a121ca5d3',

    [string]$ResourceGroup = 'az-radar-vnet-rg',

    [string]$Location = 'centralus',

    [string]$ApiAppName = 'azr-api-x8c5i2',

    [string]$JobAppName = 'azr-job-x8c5i2',

    [string]$BotGatewayAppName = 'az-radar-bot-ay637nckh3ebc',

    [string]$DispatchWorkerAppName = 'az-radar-dispatch-ay637nckh3ebc',

    [string]$RuntimeIdentityName = 'az-radar-uami',

    [string]$BotGatewayIdentityName = 'az-radar-bot-gateway-uami',

    [string]$DispatchWorkerIdentityName = 'az-radar-dispatch-worker-uami',

    [string]$ApiTag = 'blue',

    [string]$JobTag = 'green',

    [string]$BotGatewayTag = 'blue',

    [string]$DispatchWorkerTag = 'blue'
)

$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$rolloutTemplate = Join-Path $PSScriptRoot 'acr-rollout.bicep'
$publishScript = Join-Path $PSScriptRoot 'publish-acr-images.ps1'

function Assert-LastExitCode {
    param([string]$Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Set-AppContainer {
    param(
        [string]$AppName,
        [string]$Repository,
        [string]$Tag,
        [string]$IdentityClientId,
        [string]$RegistryLoginServer
    )

    $image = "$RegistryLoginServer/$Repository`:$Tag"
    Write-Host "==> Configuring $AppName with $image" -ForegroundColor Cyan

    az webapp config container set `
        --subscription $SubscriptionId `
        --resource-group $ResourceGroup `
        --name $AppName `
        --container-image-name $image `
        --enable-app-service-storage false `
        --output none
    Assert-LastExitCode "Container update for $AppName"

    $configFile = New-TemporaryFile
    try {
        @{
            acrUseManagedIdentityCreds = $true
            acrUserManagedIdentityID = $IdentityClientId
        } | ConvertTo-Json | Set-Content -Path $configFile -Encoding utf8

        az webapp config set `
            --subscription $SubscriptionId `
            --resource-group $ResourceGroup `
            --name $AppName `
            --always-on true `
            --generic-configurations "@$configFile" `
            --output none
        Assert-LastExitCode "Managed identity registry configuration for $AppName"
    }
    finally {
        Remove-Item -LiteralPath $configFile -Force -ErrorAction SilentlyContinue
    }

    $siteId = az webapp show `
        --subscription $SubscriptionId `
        --resource-group $ResourceGroup `
        --name $AppName `
        --query id `
        --output tsv
    Assert-LastExitCode "App Service lookup for $AppName"

    az resource update `
        --ids $siteId `
        --api-version 2024-11-01 `
        --set properties.outboundVnetRouting.allTraffic=true `
              properties.outboundVnetRouting.imagePullTraffic=true `
        --output none
    Assert-LastExitCode "VNet image-pull routing for $AppName"
}

az account set --subscription $SubscriptionId
Assert-LastExitCode 'Azure subscription selection'

$bootstrapDeployment = "az-radar-acr-bootstrap-$(Get-Date -Format 'yyyyMMddHHmmss')"
Write-Host '==> Provisioning ACR for image publication' -ForegroundColor Cyan
az deployment group create `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $bootstrapDeployment `
    --template-file $rolloutTemplate `
    --parameters location=$Location publicNetworkAccess=Enabled `
    --output none
Assert-LastExitCode 'ACR bootstrap deployment'

$registryName = az deployment group show `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $bootstrapDeployment `
    --query properties.outputs.name.value `
    --output tsv
Assert-LastExitCode 'ACR name lookup'

$registryLoginServer = az deployment group show `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $bootstrapDeployment `
    --query properties.outputs.loginServer.value `
    --output tsv
Assert-LastExitCode 'ACR login server lookup'

$registryResourceId = az deployment group show `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $bootstrapDeployment `
    --query properties.outputs.resourceId.value `
    --output tsv
Assert-LastExitCode 'ACR resource ID lookup'

& $publishScript `
    -RegistryName $registryName `
    -ApiTag $ApiTag `
    -JobTag $JobTag `
    -BotGatewayTag $BotGatewayTag `
    -DispatchWorkerTag $DispatchWorkerTag

$runtimeClientId = az identity show `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $RuntimeIdentityName `
    --query clientId `
    --output tsv
Assert-LastExitCode 'Runtime identity lookup'

$botGatewayClientId = az identity show `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $BotGatewayIdentityName `
    --query clientId `
    --output tsv
Assert-LastExitCode 'Bot gateway identity lookup'

$dispatchWorkerClientId = az identity show `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $DispatchWorkerIdentityName `
    --query clientId `
    --output tsv
Assert-LastExitCode 'Dispatch worker identity lookup'

$expectedPrincipalIds = @(
    (az identity show --subscription $SubscriptionId --resource-group $ResourceGroup `
        --name $RuntimeIdentityName --query principalId --output tsv)
    (az identity show --subscription $SubscriptionId --resource-group $ResourceGroup `
        --name $BotGatewayIdentityName --query principalId --output tsv)
    (az identity show --subscription $SubscriptionId --resource-group $ResourceGroup `
        --name $DispatchWorkerIdentityName --query principalId --output tsv)
)

Write-Host '==> Waiting for AcrPull role assignments' -ForegroundColor Cyan
$roleDeadline = (Get-Date).AddMinutes(5)
do {
    $assignedPrincipalIds = @(
        az role assignment list `
            --subscription $SubscriptionId `
            --scope $registryResourceId `
            --role AcrPull `
            --query '[].principalId' `
            --output tsv
    )
    Assert-LastExitCode 'AcrPull role assignment lookup'

    $missingPrincipalIds = @($expectedPrincipalIds | Where-Object {
        $_ -notin $assignedPrincipalIds
    })
    if ($missingPrincipalIds.Count -eq 0) {
        break
    }

    Start-Sleep -Seconds 10
} while ((Get-Date) -lt $roleDeadline)

if ($missingPrincipalIds.Count -gt 0) {
    throw "AcrPull did not propagate for principal IDs: $($missingPrincipalIds -join ', ')"
}

Set-AppContainer -AppName $ApiAppName -Repository 'az-radar-api' -Tag $ApiTag `
    -IdentityClientId $runtimeClientId -RegistryLoginServer $registryLoginServer
Set-AppContainer -AppName $JobAppName -Repository 'az-radar-jobhost' -Tag $JobTag `
    -IdentityClientId $runtimeClientId -RegistryLoginServer $registryLoginServer
Set-AppContainer -AppName $BotGatewayAppName -Repository 'az-radar-bot-gateway' -Tag $BotGatewayTag `
    -IdentityClientId $botGatewayClientId -RegistryLoginServer $registryLoginServer
Set-AppContainer -AppName $DispatchWorkerAppName -Repository 'az-radar-dispatch-worker' `
    -Tag $DispatchWorkerTag -IdentityClientId $dispatchWorkerClientId `
    -RegistryLoginServer $registryLoginServer

$lockdownDeployment = "az-radar-acr-private-$(Get-Date -Format 'yyyyMMddHHmmss')"
Write-Host '==> Disabling ACR public access' -ForegroundColor Cyan
az deployment group create `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $lockdownDeployment `
    --template-file $rolloutTemplate `
    --parameters location=$Location publicNetworkAccess=Disabled `
    --output none
Assert-LastExitCode 'ACR private lockdown deployment'

$registrySecurity = az acr show `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $registryName `
    --query '{publicNetworkAccess:publicNetworkAccess,adminUserEnabled:adminUserEnabled,anonymousPullEnabled:anonymousPullEnabled}' `
    --output json | ConvertFrom-Json
Assert-LastExitCode 'ACR security verification'

if ($registrySecurity.publicNetworkAccess -ne 'Disabled' -or
    $registrySecurity.adminUserEnabled -ne $false -or
    $registrySecurity.anonymousPullEnabled -ne $false) {
    throw 'ACR did not reach the required private, keyless final state.'
}

foreach ($appName in @($ApiAppName, $JobAppName, $BotGatewayAppName, $DispatchWorkerAppName)) {
    az webapp restart `
        --subscription $SubscriptionId `
        --resource-group $ResourceGroup `
        --name $appName `
        --output none
    Assert-LastExitCode "Restart for $appName"
}

$apiHostName = az webapp show `
    --subscription $SubscriptionId `
    --resource-group $ResourceGroup `
    --name $ApiAppName `
    --query defaultHostName `
    --output tsv
Assert-LastExitCode 'API hostname lookup'

$healthUri = "https://$apiHostName/api/health"
Write-Host "==> Waiting for $healthUri" -ForegroundColor Cyan
$deadline = (Get-Date).AddMinutes(8)
do {
    try {
        $response = Invoke-RestMethod -Uri $healthUri -TimeoutSec 30
        if ($response.status -eq 'healthy') {
            Write-Host "==> Deployment healthy: $healthUri" -ForegroundColor Green
            exit 0
        }
    }
    catch {
        Write-Host "Waiting for API startup: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    Start-Sleep -Seconds 15
} while ((Get-Date) -lt $deadline)

throw "API health check did not succeed before the timeout: $healthUri"
