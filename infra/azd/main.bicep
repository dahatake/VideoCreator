// azd entry point (subscription scope): creates the resource group, then deploys ../main.bicep into it.
//   azd provision        (or: run.cmd --provision-azure)
targetScope = 'subscription'

@minLength(1)
@maxLength(32)
@description('azd environment name (AZURE_ENV_NAME).')
param environmentName string

@description('Region with Speech Fast Transcription (AZURE_LOCATION).')
param location string

@description('Object ID of the signed-in user; granted the Speech and OpenAI user roles (AZURE_PRINCIPAL_ID).')
param principalId string = ''

var resourceGroupName = 'rg-${environmentName}'
var accountName = 'videocreator-${uniqueString(subscription().id, environmentName)}'
var tags = { app: 'VideoCreator', 'azd-env-name': environmentName }

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module ai '../main.bicep' = {
  scope: rg
  name: 'videocreator-ai'
  params: {
    accountName: accountName
    location: location
    userObjectIds: empty(principalId) ? [] : [principalId]
    tags: tags
  }
}

output AZURE_TENANT_ID string = ai.outputs.tenantId
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_SPEECH_ENDPOINT string = ai.outputs.speechEndpoint
output AZURE_FOUNDRY_ENDPOINT string = ai.outputs.foundryEndpoint
output AZURE_CHAT_DEPLOYMENT string = ai.outputs.chatDeployment
output AZURE_EMBEDDING_DEPLOYMENT string = ai.outputs.embeddingDeployment
