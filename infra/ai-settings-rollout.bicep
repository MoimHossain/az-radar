targetScope = 'resourceGroup'

@description('Existing JobHost. This rollout creates no hosting resources.')
param jobAppName string

@description('Existing private Key Vault URI used by the API.')
param keyVaultUri string

@secure()
@description('Current JobHost app settings captured immediately before this scoped update.')
param currentAppSettings object

resource job 'Microsoft.Web/sites@2023-12-01' existing = {
  name: jobAppName
}

resource jobSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: job
  name: 'appsettings'
  properties: union(currentAppSettings, {
    AzureDevOpsWiki__KeyVaultUri: keyVaultUri
  })
}
