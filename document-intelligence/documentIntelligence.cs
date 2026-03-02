#:package Azure.AI.DocumentIntelligence@1.0.0
#:package Azure.Identity@1.18.0-beta.3

using Azure.Identity;
using Azure.AI.DocumentIntelligence;

var endpoint = "https://<your-resource>.cognitiveservices.azure.com";

var credential = new DefaultAzureCredential();

/*
    DocumentIntelligenceClient provides operations for:
    - Analyzing input documents using prebuilt and custom models through AnalyzeDocument API.
    - Detect and identify custom input documents with the ClassifyDocument API.
*/

var client = new DocumentIntelligenceClient(new Uri(endpoint),credential);


/*
    DocumentIntelligenceAdministrationClient provides operations for:
    - Building custom models to analyze specific fields you specify by labeling your custom documents.
    - Compose a model from a collection of existing models.
    - Managing models created in your account.
    - Copying a custom model from one Document Intelligence resource to another.
    - Getting or listing operations created within the last 24 hours.
    - Building and managing document classification models to accurately detect and identify documents you process within your application.

*/
var AdministrationClient = new DocumentIntelligenceAdministrationClient(new Uri(endpoint),credential);

