targetScope = 'subscription'

@description('Principal id of the dedicated Service Health provisioning UAMI.')
param provisioningPrincipalId string

@description('Name of the subscription Activity Log diagnostic setting.')
param diagnosticSettingName string = 'az-radar-service-health'

@description('Resource id of the Event Hubs authorization rule used by Azure Monitor.')
param eventHubAuthorizationRuleId string

@description('Name of the destination Event Hub.')
param eventHubName string

var diagnosticRoleDefinitionName = guid(subscription().id, 'az-radar-service-health-diagnostic-settings-provisioner')

resource diagnosticRoleDefinition 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: diagnosticRoleDefinitionName
  properties: {
    roleName: 'AzRadar Service Health Diagnostic Settings Provisioner'
    description: 'Allows AzRadar to manage subscription Activity Log diagnostic settings.'
    type: 'CustomRole'
    assignableScopes: [
      subscription().id
    ]
    permissions: [
      {
        actions: [
          'Microsoft.Resources/subscriptions/read'
          'Microsoft.Insights/diagnosticSettings/read'
          'Microsoft.Insights/diagnosticSettings/write'
          'Microsoft.Insights/diagnosticSettings/delete'
        ]
        notActions: []
        dataActions: []
        notDataActions: []
      }
    ]
  }
}

resource diagnosticRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, provisioningPrincipalId, diagnosticRoleDefinition.id)
  properties: {
    roleDefinitionId: diagnosticRoleDefinition.id
    principalId: provisioningPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource serviceHealthDiagnosticSetting 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: subscription()
  name: diagnosticSettingName
  properties: {
    eventHubAuthorizationRuleId: eventHubAuthorizationRuleId
    eventHubName: eventHubName
    logs: [
      {
        category: 'ServiceHealth'
        enabled: true
        retentionPolicy: {
          enabled: false
          days: 0
        }
      }
    ]
  }
}

output diagnosticSettingName string = serviceHealthDiagnosticSetting.name
output diagnosticRoleDefinitionId string = diagnosticRoleDefinition.id
