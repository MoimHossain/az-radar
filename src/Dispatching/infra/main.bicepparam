using './main.bicep'

param location = 'centralus'
param namePrefix = 'az-radar'
param cosmosAccountName = 'az-radar-cosmos-ay637nckh3ebc'
param cosmosDatabaseName = 'az-radar-db'
param privateEndpointSubnetId = '/subscriptions/5e22addc-6168-4683-afd0-789a121ca5d3/resourceGroups/az-radar-vnet-rg/providers/Microsoft.Network/virtualNetworks/az-radar-vnet/subnets/snet-private-endpoints'
param gatewayIntegrationSubnetId = '/subscriptions/5e22addc-6168-4683-afd0-789a121ca5d3/resourceGroups/az-radar-vnet-rg/providers/Microsoft.Network/virtualNetworks/az-radar-vnet/subnets/snet-app-api'
param workerIntegrationSubnetId = '/subscriptions/5e22addc-6168-4683-afd0-789a121ca5d3/resourceGroups/az-radar-vnet-rg/providers/Microsoft.Network/virtualNetworks/az-radar-vnet/subnets/snet-app-job'
param apiPrincipalId = '1d009d7d-59a6-489e-929d-2b1a6fe6f97b'
param gatewayImage = 'moimhossain/az-radar-bot-gateway:blue'
param workerImage = 'moimhossain/az-radar-dispatch-worker:blue'
param deployTeamsDispatch = true
