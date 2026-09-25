// =============================================================================
// Microsoft Foundry for the Agent Squad software factory.
//
// Deliberately minimal: one Foundry account, one project, and the model
// deployments the orchestrator uses. No keys — access is Microsoft Entra ID
// with RBAC scoped to the account (SEC-002, SEC-003).
//
//   az deployment group create -g <rg> -f infra/main.bicep \
//      -p infra/main.parameters.json -p principalId=$(az ad signed-in-user show --query id -o tsv)
// =============================================================================

targetScope = 'resourceGroup'

@description('Base name for the Foundry account. Must be globally unique because it becomes the custom subdomain.')
@minLength(3)
@maxLength(24)
param accountName string = 'fdy${uniqueString(resourceGroup().id)}'

@description('Azure region. Must support the Responses API, which is what gates agent operations.')
@allowed([
  'eastus'
  'eastus2'
  'westus'
  'westus3'
  'swedencentral'
  'northcentralus'
  'southcentralus'
  'francecentral'
  'uksouth'
])
param location string = 'eastus2'

@description('Name of the Foundry project that holds agents, connections and telemetry.')
@minLength(2)
@maxLength(32)
param projectName string = 'agent-squad'

@description('Display name shown in the Foundry portal.')
param projectDisplayName string = 'Agent Squad — TDC São Paulo 2026'

@description('Object id of the principal that operates the factory. Receives the Foundry User role on the account.')
param principalId string

@description('Principal type of principalId. Use ServicePrincipal in CI.')
@allowed([ 'User', 'Group', 'ServicePrincipal' ])
param principalType string = 'User'

// -----------------------------------------------------------------------------
// Model deployments.
//
// Capacity is thousands of tokens per minute, and it is a real constraint: a
// subscription can be entitled to a model and still hold zero quota for it, in
// which case the deployment fails at preflight with InsufficientQuota rather
// than at runtime. Check before you deploy — and before you rehearse a talk:
//
//   az cognitiveservices usage list -l <region> -o table
//
// Three deployments, one per job, so the cost profile of the pipeline is
// legible: a strong reasoner for planning, a cheap workhorse for the
// high-volume structured calls, and a code-tuned model for review.
//
// All three are OpenAI models, which Azure sells directly. Partner models —
// Anthropic, xAI, Mistral, Cohere — are Marketplace purchases, and a deployment
// fails with:
//
//   Marketplace Subscription purchase eligibility check failed ... there is no
//   valid payment method associated with this Azure subscription
//
// on any subscription without an active payment instrument, which includes most
// internal, sponsored and credit-based subscriptions. See §Partner models in
// docs/demo-runbook.md before adding one.
// -----------------------------------------------------------------------------
@description('Model deployments to create. Capacity is in thousands of tokens per minute.')
param modelDeployments array = [
  {
    name: 'gpt-5.5'
    modelName: 'gpt-5.5'
    version: '2026-04-24'
    format: 'OpenAI'
    skuName: 'GlobalStandard'
    capacity: 50
  }
  {
    name: 'gpt-5.4-mini'
    modelName: 'gpt-5.4-mini'
    version: '2026-03-17'
    format: 'OpenAI'
    skuName: 'GlobalStandard'
    capacity: 100
  }
  {
    name: 'gpt-5.3-codex'
    modelName: 'gpt-5.3-codex'
    version: '2026-02-24'
    format: 'OpenAI'
    skuName: 'GlobalStandard'
    capacity: 50
  }
]

@description('Tags applied to every resource.')
param tags object = {
  project: 'agent-squad'
  event: 'tdc-sp-2026'
  owner: 'alexandre-costa'
}

// -----------------------------------------------------------------------------
// Partner-model attestation.
//
// Anthropic deployments are rejected at preflight without this:
//   InvalidModelProviderData: ModelProviderData is required for Anthropic model
//   deployments but was not provided. Please provide all required fields:
//   industry, organizationName, and countryCode.
//
// It is a commercial attestation the partner requires, not a technical setting,
// and it is absent from most published samples. Fill it in honestly.
// -----------------------------------------------------------------------------
@description('Industry of the organization deploying partner models, e.g. Technology.')
param providerIndustry string = 'Technology'

@description('Legal or trading name of the organization deploying partner models.')
param providerOrganizationName string = 'TDC Sao Paulo 2026 Demo'

@description('ISO 3166-1 alpha-2 country code of the organization, e.g. BR.')
@minLength(2)
@maxLength(2)
param providerCountryCode string = 'BR'

// -----------------------------------------------------------------------------
// Role definition ids.
//
// GUIDs rather than names on purpose: the Foundry roles were renamed from the
// "Azure AI *" family, and Microsoft's own guidance is to bind by id so a rename
// cannot break a deployment.
// -----------------------------------------------------------------------------
var roles = {
  foundryUser: '53ca6127-db72-4b80-b1b0-d745d6d5456d'
  foundryProjectManager: 'eadc314b-1a2d-4efa-be10-5d325db5065e'
}

// -----------------------------------------------------------------------------
// Foundry account. The ARM type is still Microsoft.CognitiveServices/accounts
// with kind AIServices — there is no Microsoft.Foundry resource provider.
// -----------------------------------------------------------------------------
resource account 'Microsoft.CognitiveServices/accounts@2026-07-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // Required before any accounts/projects child can be created.
    allowProjectManagement: true

    // Required for Entra ID token authentication against the account.
    customSubDomainName: accountName

    // No account keys at all. This is what makes SEC-002 enforceable rather than
    // merely recommended: there is no key path to fall back to.
    disableLocalAuth: true

    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2026-07-01' = {
  parent: account
  name: projectName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: projectDisplayName
    description: 'Agentes de requisitos, planejamento e revisão da fábrica de software autônoma.'
  }
}

// @batchSize(1): the resource provider rejects concurrent writes to one account,
// so deployments are created one at a time.
@batchSize(1)
resource deployments 'Microsoft.CognitiveServices/accounts/deployments@2026-07-01' = [for deployment in modelDeployments: {
  parent: account
  name: deployment.name
  sku: {
    name: deployment.skuName
    capacity: deployment.capacity
  }
  properties: union(
    {
      model: {
        format: deployment.format
        name: deployment.modelName
        version: deployment.version
      }
      raiPolicyName: 'Microsoft.DefaultV2'
      versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    },
    // Only partner models carry the attestation; sending it for an OpenAI
    // deployment is unnecessary noise.
    deployment.format == 'OpenAI' || deployment.format == 'OpenAI-OSS' ? {} : {
      modelProviderData: {
        industry: providerIndustry
        organizationName: providerOrganizationName
        countryCode: providerCountryCode
      }
    })
  dependsOn: [
    project
  ]
}]

// -----------------------------------------------------------------------------
// RBAC, scoped to the account rather than the subscription (SEC-003).
// -----------------------------------------------------------------------------
resource operatorIsFoundryUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, principalId, roles.foundryUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.foundryUser)
    principalId: principalId
    principalType: principalType
  }
}

resource operatorIsProjectManager 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, principalId, roles.foundryProjectManager)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.foundryProjectManager)
    principalId: principalId
    principalType: principalType
  }
}

// The project's own managed identity needs Foundry User at account scope for
// agents inside the project to reach the account's model deployments.
resource projectIdentityIsFoundryUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, project.id, roles.foundryUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.foundryUser)
    principalId: project.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// -----------------------------------------------------------------------------
// Outputs. No keys are emitted, by design.
// -----------------------------------------------------------------------------

@description('Paste this into Foundry:ProjectEndpoint.')
output projectEndpoint string = 'https://${accountName}.services.ai.azure.com/api/projects/${projectName}'

@description('The Foundry account name.')
output accountName string = account.name

@description('The Foundry project name.')
output projectName string = project.name

@description('The deployment names the orchestrator can use as model identifiers.')
output deploymentNames array = [for (deployment, i) in modelDeployments: deployment.name]
