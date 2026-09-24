<#
.SYNOPSIS
    Builds AzRadar images in Azure Container Registry without registry credentials.

.DESCRIPTION
    Uses the signed-in Azure CLI identity and ACR Tasks to build all runtime images.
    The registry must temporarily allow public network access for the hosted builders.
    The final platform deployment disables public access again.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RegistryName,

    [string]$ApiTag = 'blue',

    [string]$JobTag = 'green',

    [string]$BotGatewayTag = 'blue',

    [string]$DispatchWorkerTag = 'blue'
)

$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$sourceRoot = Split-Path -Parent $PSScriptRoot

function Invoke-AcrBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string]$Tag,

        [Parameter(Mandatory = $true)]
        [string]$Dockerfile
    )

    $existingTag = az acr repository show-tags `
        --name $RegistryName `
        --repository $Repository `
        --query "[?@ == '$Tag'] | [0]" `
        --output tsv `
        --only-show-errors 2>$null
    if ($LASTEXITCODE -eq 0 -and $existingTag -eq $Tag) {
        Write-Host "==> Reusing existing $Repository`:$Tag" -ForegroundColor Green
        return
    }

    Write-Host "==> Building $Repository`:$Tag" -ForegroundColor Cyan
    $runId = az acr build `
        --registry $RegistryName `
        --image "$Repository`:$Tag" `
        --file $Dockerfile `
        --no-logs `
        --query runId `
        --output tsv `
        $sourceRoot `
        --only-show-errors

    if ($LASTEXITCODE -ne 0) {
        throw "ACR build submission failed for $Repository`:$Tag."
    }
    if ([string]::IsNullOrWhiteSpace($runId)) {
        throw "ACR build submission returned no run ID for $Repository`:$Tag."
    }

    $deadline = (Get-Date).AddMinutes(20)
    do {
        $status = az acr task show-run `
            --registry $RegistryName `
            --run-id $runId `
            --query status `
            --output tsv `
            --only-show-errors

        if ($LASTEXITCODE -ne 0) {
            throw "Could not read ACR build status for $Repository`:$Tag ($runId)."
        }

        if ($status -eq 'Succeeded') {
            Write-Host "==> Built $Repository`:$Tag ($runId)" -ForegroundColor Green
            return
        }
        if ($status -in @('Failed', 'Canceled', 'Error', 'Timeout')) {
            throw "ACR build $runId for $Repository`:$Tag finished with status $status."
        }

        Start-Sleep -Seconds 10
    } while ((Get-Date) -lt $deadline)

    if ((Get-Date) -ge $deadline) {
        throw "Timed out waiting for ACR build $runId for $Repository`:$Tag."
    }
}

Invoke-AcrBuild -Repository 'az-radar-api' -Tag $ApiTag -Dockerfile 'Dockerfile.api'
Invoke-AcrBuild -Repository 'az-radar-jobhost' -Tag $JobTag -Dockerfile 'Dockerfile.jobhost'
Invoke-AcrBuild -Repository 'az-radar-bot-gateway' -Tag $BotGatewayTag -Dockerfile 'src/Dispatching/Dockerfile.bot-gateway'
Invoke-AcrBuild -Repository 'az-radar-dispatch-worker' -Tag $DispatchWorkerTag -Dockerfile 'src/Dispatching/Dockerfile.worker'

Write-Host '==> Published all AzRadar images to ACR.' -ForegroundColor Green
