// Azure resources VideoCreator needs: one Azure AI Services account (Speech Fast Transcription +
// Azure OpenAI chat/embeddings) with Entra ID access. Deploy at resource-group scope:
//   az deployment group create -g <rg> -f infra/main.bicep -p accountName=<globally-unique-name>
// See users-guide/azure-access.md.

@description('Azure AI Services account name; also used as the custom subdomain (required for Entra ID tokens).')
param accountName string

@description('Region with Speech Fast Transcription and the chosen models.')
param location string = resourceGroup().location

@description('true = Entra ID only (recommended). Organization policy may force true regardless of this value.')
param disableLocalAuth bool = true

param chatDeploymentName string = 'gpt-chat-latest'
param chatModelName string = 'gpt-chat-latest'
param chatModelVersion string = '2026-10-07'
@description('Capacity in thousands of tokens per minute.')
param chatCapacity int = 100

@description('Empty string skips the embedding deployment (topic split falls back to lexical).')
param embeddingDeploymentName string = 'text-embedding-3-large'
param embeddingModelName string = 'text-embedding-3-large'
param embeddingModelVersion string = '1'
param embeddingCapacity int = 100

param deploymentSku string = 'GlobalStandard'

@description('Entra object IDs of users that may call Speech and the models (granted at resource-group scope).')
param userObjectIds array = []

param tags object = { app: 'VideoCreator' }

var speechUserRoleId = 'f2dc8367-1007-4938-bd23-fe263f013447' // Cognitive Services Speech User
var openAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' // Cognitive Services OpenAI User

resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  tags: tags
  properties: {
    customSubDomainName: accountName
    disableLocalAuth: disableLocalAuth
    publicNetworkAccess: 'Enabled'
    allowProjectManagement: true
  }
}

resource chat 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: account
  name: chatDeploymentName
  sku: { name: deploymentSku, capacity: chatCapacity }
  properties: {
    model: { format: 'OpenAI', name: chatModelName, version: chatModelVersion }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

// Deployments on one account must be created one at a time.
resource embedding 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = if (!empty(embeddingDeploymentName)) {
  parent: account
  name: empty(embeddingDeploymentName) ? 'unused' : embeddingDeploymentName
  dependsOn: [chat]
  sku: { name: deploymentSku, capacity: embeddingCapacity }
  properties: {
    model: { format: 'OpenAI', name: embeddingModelName, version: embeddingModelVersion }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

resource speechUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for id in userObjectIds: {
  name: guid(resourceGroup().id, id, speechUserRoleId)
  properties: {
    principalId: id
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', speechUserRoleId)
  }
}]

resource openAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for id in userObjectIds: {
  name: guid(resourceGroup().id, id, openAiUserRoleId)
  properties: {
    principalId: id
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', openAiUserRoleId)
  }
}]

output tenantId string = tenant().tenantId
output speechEndpoint string = account.properties.endpoint
output foundryEndpoint string = '${account.properties.endpoint}openai/v1/'
output chatDeployment string = chat.name
output embeddingDeployment string = empty(embeddingDeploymentName) ? '' : embeddingDeploymentName
output localAuthDisabled bool = account.properties.disableLocalAuth
