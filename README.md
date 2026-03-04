# Azure AI Document Toolkit

A comprehensive toolkit and demo repository for working with **Azure AI Content Understanding** and **Azure AI Document Intelligence** using .NET. This repository provides infrastructure-as-code templates, sample applications, and reusable components for building intelligent document processing solutions.

## What is this repository for?

This repository demonstrates how to:
- **Deploy Azure AI infrastructure** using Bicep templates with Azure CLI
- **Integrate Azure AI Content Understanding** for analyzing documents, images, audio, and video
- **Use Azure AI Document Intelligence** (formerly Form Recognizer) for document analysis
- **Work with Azure OpenAI models** (GPT-4.1, GPT-4.1-mini, text-embedding-3-large) for document understanding
- **Configure RBAC permissions** for secure access between Azure services
- **Build .NET applications** that leverage Azure AI services with modern authentication

Perfect for developers who want to quickly spin up Azure AI infrastructure and start building document processing applications.

## Repository Structure

```
├── .infra/                             # Infrastructure as Code (Bicep)
│   ├── main.bicep                      # Main infrastructure template
│   └── main.parameters.json            # Deployment parameters
│
├── content-understanding/              # Content Understanding demos
│   └── AnalyzeFromURL.cs              # Demo: Analyze content from URL
│
├── document-intelligence/              # Document Intelligence demos
│   └── documentIntelligence.cs        # Demo: Document analysis
│
├── analyzers/                          # Custom analyzer definitions
│   ├── CustomAnalyzerMixedData.json   # Example custom analyzer
│   └── README.md
│
├── ContentUnderstanding.slnx           # .NET solution file
└── README.md                           # This file
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- An active Azure subscription

## Deployment

### Quick Start: Deploy Infrastructure

Deploy all required Azure resources using Azure CLI:

```bash
# 1. Login to Azure
az login

# 2. Set your subscription
az account set --subscription "<your-subscription-id>"

# 3. Create a resource group
az group create --name rg-document-toolkit --location swedencentral

# 4. Get your user principal ID for storage access (optional but recommended)
USER_PRINCIPAL_ID=$(az ad signed-in-user show --query id -o tsv)

# 5. Deploy the infrastructure
az deployment group create \
  --resource-group rg-document-toolkit \
  --template-file .infra/main.bicep \
  --parameters principalId="$USER_PRINCIPAL_ID"
```

### What Gets Deployed

The deployment provisions:

| Resource | Description | Location |
|----------|-------------|----------|
| **Azure AI Document Intelligence** | Form Recognizer service for document analysis | Italy North |
| **Azure AI Services** | Multi-service AI resource with OpenAI models | Sweden Central |
| **Azure Storage Account** | Blob storage with 'files' container | Italy North |

**OpenAI Models Deployed:**
- GPT-4.1 (capacity: 100K TPM)
- GPT-4.1-mini (capacity: 250K TPM)
- text-embedding-3-large (capacity: 120K TPM)

**RBAC Permissions Configured:**
- AI Services → Storage Account (**Reader** role)
- Your user → Storage Account (**Storage Blob Data Contributor** role)

All resource names are auto-generated with unique suffixes to avoid conflicts.

### Tear Down Infrastructure

When you're done testing:

```bash
# Delete the resource group and all resources
az group delete --name rg-document-toolkit --yes --no-wait
```

### View Deployed Resources

To see connection details and endpoints:

```bash
# View deployment outputs
az deployment group show \
  --resource-group rg-document-toolkit \
  --name main \
  --query properties.outputs

# Get specific output value (e.g., AI Services endpoint)
az deployment group show \
  --resource-group rg-document-toolkit \
  --name main \
  --query properties.outputs.AZURE_AI_SERVICES_ENDPOINT.value -o tsv
```

## Getting Started

### 1. Deploy Infrastructure

Follow the deployment steps above to provision Azure resources.

### 2. Configure Your Application

After deployment, get your service endpoints:

```bash
# Get AI Services endpoint
az deployment group show \
  --resource-group rg-document-toolkit \
  --name main \
  --query properties.outputs.AZURE_AI_SERVICES_ENDPOINT.value -o tsv

# Get Document Intelligence endpoint
az deployment group show \
  --resource-group rg-document-toolkit \
  --name main \
  --query properties.outputs.AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT.value -o tsv

# Get Storage Account name
az deployment group show \
  --resource-group rg-document-toolkit \
  --name main \
  --query properties.outputs.AZURE_STORAGE_ACCOUNT_NAME.value -o tsv
```

### 3. Run the Demos

#### Content Understanding Demo

```bash
cd content-understanding
dotnet run AnalyzeFromURL.cs
```

This demo shows how to:
- Authenticate with Azure AI Services using DefaultAzureCredential
- Analyze content from a URL using prebuilt analyzers
- Parse and display the analysis results

#### Document Intelligence Demo

```bash
cd document-intelligence
dotnet run documentIntelligence.cs
```

This demo demonstrates:
- Using Azure Document Intelligence for form recognition
- Extracting structured data from documents
- Processing various document types (invoices, receipts, etc.)

### 4. Upload Files to Storage

With the Storage Blob Data Contributor role assigned, you can upload files:

```bash
# Get storage account name
STORAGE_ACCOUNT=$(az deployment group show \
  --resource-group rg-document-toolkit \
  --name main \
  --query properties.outputs.AZURE_STORAGE_ACCOUNT_NAME.value -o tsv)

# Upload file using Azure CLI
az storage blob upload \
  --account-name $STORAGE_ACCOUNT \
  --container-name files \
  --name mydocument.pdf \
  --file ./path/to/mydocument.pdf \
  --auth-mode login
```

Or use Azure Storage Explorer, Azure Portal, or the Azure Storage SDK in your applications.

## Working with Content Understanding

### Using the Azure SDK

The demos use the official `Azure.AI.ContentUnderstanding` SDK package:

```csharp
using Azure.AI.ContentUnderstanding;
using Azure.Identity;

var endpoint = Environment.GetEnvironmentVariable("AZURE_AI_SERVICES_ENDPOINT");
var credential = new DefaultAzureCredential();
var client = new ContentUnderstandingClient(new Uri(endpoint), credential);

// Analyze from URL
var operation = await client.AnalyzeDocumentFromUriAsync(
    WaitUntil.Completed,
    "prebuilt-documentSearch",
    new Uri("https://example.com/document.pdf")
);

var result = operation.Value;
```

### Available Prebuilt Analyzers

- `prebuilt-documentSearch` - Extract searchable text from documents
- `prebuilt-layout` - Extract text, tables, and structure
- `prebuilt-read` - OCR and text extraction
- And more...

## Custom Analyzers

The `analyzers/` folder contains reusable custom analyzer definitions that can be used with Azure AI Content Understanding.

### Example: CustomAnalyzerMixedData.json

This analyzer demonstrates how to:
- Define custom fields for extraction
- Configure field types and schemas
- Handle mixed content types

To use a custom analyzer, deploy it to your Azure AI Content Understanding resource using the SDK or REST API.

## Infrastructure Details

### Bicep Template Overview

The infrastructure is defined in [.infra/main.bicep](.infra/main.bicep):

- **Parameterized**: Simple location parameters, all names auto-generated
- **Secure**: RBAC-based authentication, no hardcoded credentials
- **Modular**: Clear resource definitions with proper dependencies
- **Production-ready**: Includes encryption, TLS 1.2, private access controls

### Cost Considerations

**Estimated monthly costs** (approximate):
- Azure AI Services (S0): ~$1-10 depending on usage
- Document Intelligence (S0): ~$1-10 depending on usage  
- Storage Account (Standard LRS): ~$0.02 per GB stored
- OpenAI model TPM: Pay-per-token pricing

💡 **Tip**: Delete the resource group when not in use to minimize costs: `az group delete --name rg-document-toolkit`

## Troubleshooting

### Authentication Issues

If you see authentication errors:

```bash
# Re-login to Azure
az login

# Verify your identity
az account show
```

### Resource Deployment Failures

If deployment fails:

```bash
# Check deployment status and errors
az deployment group show \
  --resource-group rg-document-toolkit \
  --name main \
  --query properties.error

# Verify subscription and quotas
az account show
az provider show -n Microsoft.CognitiveServices --query "resourceTypes[?resourceType=='accounts'].locations"
```

### Missing Permissions

If you can't upload to storage:

```bash
# Get storage account name
STORAGE_ACCOUNT=$(az deployment group show \
  --resource-group rg-document-toolkit \
  --name main \
  --query properties.outputs.AZURE_STORAGE_ACCOUNT_NAME.value -o tsv)

# Verify role assignment
az role assignment list \
  --assignee $(az ad signed-in-user show --query id -o tsv) \
  --scope /subscriptions/<sub-id>/resourceGroups/rg-document-toolkit/providers/Microsoft.Storage/storageAccounts/$STORAGE_ACCOUNT

# Re-deploy with your principal ID
USER_PRINCIPAL_ID=$(az ad signed-in-user show --query id -o tsv)
az deployment group create \
  --resource-group rg-document-toolkit \
  --template-file .infra/main.bicep \
  --parameters principalId="$USER_PRINCIPAL_ID"
```

## Resources

- [Azure AI Content Understanding Documentation](https://learn.microsoft.com/azure/ai-services/content-understanding/)
- [Azure AI Document Intelligence Documentation](https://learn.microsoft.com/azure/ai-services/document-intelligence/)
- [Azure OpenAI Service Documentation](https://learn.microsoft.com/azure/ai-services/openai/)
- [Azure CLI Documentation](https://learn.microsoft.com/cli/azure/)
- [Bicep Documentation](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)

## Contributing

Contributions are welcome! Feel free to submit issues or pull requests for:
- Additional demo scenarios
- Custom analyzer examples
- Infrastructure improvements
- Documentation updates

## License

This project is provided as-is for educational and development purposes.
