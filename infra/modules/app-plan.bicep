// ---------------------------------------------------------------------------
// App Service Plan module (Linux, container-capable)
// ---------------------------------------------------------------------------

@description('Azure region for the plan.')
param location string

@description('App Service Plan name.')
param name string

@description('SKU name (e.g. B1). Must be Basic or higher for VNet integration.')
param skuName string = 'B1'

@description('Tags applied to all resources.')
param tags object = {}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: name
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: skuName
  }
  properties: {
    reserved: true // Linux
  }
}

output planId string = plan.id
output planName string = plan.name
