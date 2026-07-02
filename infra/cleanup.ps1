<#
.SYNOPSIS
    Deletes an AzRadar resource group (used to tear down test deployments).

.EXAMPLE
    ./cleanup.ps1 -ResourceGroup az-radar-iac-test-rg
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'

Write-Host "==> Deleting resource group '$ResourceGroup'" -ForegroundColor Yellow
if ($NoWait) {
    az group delete --name $ResourceGroup --yes --no-wait
} else {
    az group delete --name $ResourceGroup --yes
}
Write-Host "==> Delete request submitted." -ForegroundColor Green
