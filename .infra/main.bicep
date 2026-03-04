
@description('Primary Azure region for resources')
param location string = 'swedencentral'

@description('Secondary location for Document Intelligence')
param docIntelligenceLocation string = 'italynorth'

@description('Principal ID of the user deploying (for storage access)')
param principalId string = ''

// Simple resource names
var resourceToken = toLower(uniqueString(subscription().id, resourceGroup().id))
var storageAccountName = 'st${resourceToken}'
var docIntelligenceName = 'docintel${resourceToken}'
var aiServicesName = 'aiservices${resourceToken}'

// Built-in role definitions
var readerRoleId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource docIntelligence 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: docIntelligenceName
  location: docIntelligenceLocation
  sku: {
    name: 'S0'
  }
  kind: 'FormRecognizer'
  properties: {
    customSubDomainName: docIntelligenceName
    allowProjectManagement: false
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: storageAccountName
  location: docIntelligenceLocation
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    dnsEndpointType: 'Standard'
    defaultToOAuthAuthentication: false
    publicNetworkAccess: 'Enabled'
    allowCrossTenantReplication: false
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    supportsHttpsTrafficOnly: true
    encryption: {
      requireInfrastructureEncryption: false
      services: {
        file: {
          keyType: 'Account'
          enabled: true
        }
        blob: {
          keyType: 'Account'
          enabled: true
        }
      }
      keySource: 'Microsoft.Storage'
    }
    accessTier: 'Hot'
  }
}

resource aiServices 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: aiServicesName
  location: location
  sku: {
    name: 'S0'
  }
  kind: 'AIServices'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: aiServicesName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
  }
}

resource aiServices_Default 'Microsoft.CognitiveServices/accounts/defenderForAISettings@2025-06-01' = {
  parent: aiServices
  name: 'Default'
  properties: {
    state: 'Disabled'
  }
}

// Model deployments on AI Services
resource deployment_gpt_4_1 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: aiServices
  name: 'gpt-4-1'
  sku: {
    name: 'GlobalStandard'
    capacity: 100
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1'
      version: '2025-04-14'
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    currentCapacity: 100
    raiPolicyName: 'Microsoft.DefaultV2'
  }
  dependsOn: [
    aiServices_Default
  ]
}

resource deployment_gpt_4_1_mini 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: aiServices
  name: 'gpt-4-1-mini'
  sku: {
    name: 'GlobalStandard'
    capacity: 250
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1-mini'
      version: '2025-04-14'
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    currentCapacity: 250
    raiPolicyName: 'Microsoft.DefaultV2'
  }
  dependsOn: [
    deployment_gpt_4_1
  ]
}

resource deployment_embedding 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: aiServices
  name: 'text-embedding-3-large'
  sku: {
    name: 'GlobalStandard'
    capacity: 120
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
    versionUpgradeOption: 'NoAutoUpgrade'
    currentCapacity: 120
    raiPolicyName: 'Microsoft.DefaultV2'
  }
  dependsOn: [
    deployment_gpt_4_1_mini
  ]
}

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    cors: {
      corsRules: []
    }
    deleteRetentionPolicy: {
      allowPermanentDelete: false
      enabled: false
    }
  }
}

resource filesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobServices
  name: 'files'
  properties: {
    publicAccess: 'None'
  }
}

// Role assignment: AI Services -> Storage Account (Reader)
resource aiServicesStorageReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, aiServices.id, readerRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRoleId)
    principalId: aiServices.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role assignment: User -> Storage Account (Storage Blob Data Contributor)
resource userStorageBlobContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (principalId != '') {
  name: guid(storageAccount.id, principalId, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: principalId
    principalType: 'User'
  }
}

// Outputs
output AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT string = docIntelligence.properties.endpoint
output AZURE_DOCUMENT_INTELLIGENCE_NAME string = docIntelligence.name
output AZURE_AI_SERVICES_ENDPOINT string = aiServices.properties.endpoint
output AZURE_AI_SERVICES_NAME string = aiServices.name
output AZURE_STORAGE_ACCOUNT_BLOB_ENDPOINT string = storageAccount.properties.primaryEndpoints.blob
output AZURE_STORAGE_ACCOUNT_NAME string = storageAccount.name
output AZURE_LOCATION string = location
