#:project client/ContentUnderstanding.Client.csproj
#:package Azure.Identity@1.18.0-beta.3

using Azure.Identity;
using ContentUnderstanding.Client;

var endpoint = "https://<your-resource>.cognitiveservices.azure.com";

// Option 1: Authenticate with DefaultAzureCredential (recommended)
using var client = new ContentUnderstandingClient(endpoint, new DefaultAzureCredential());

// Option 2: Authenticate with an API key
// var apiKey = "<your-api-key>";
// using var client = new ContentUnderstandingClient(endpoint, apiKey);