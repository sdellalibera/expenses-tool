# Azure AI Document Toolkit

Demos and utilities for **Azure AI Content Understanding** and **Azure AI Document Intelligence** using .NET.

## Repository Structure

```
├── content-understanding/              # Content Understanding demos & client
│   ├── client/                         # Custom .NET client library for the CU REST API
│   │   ├── ContentUnderstandingClient.cs
│   │   ├── ContentUnderstandingException.cs
│   │   ├── BearerTokenHandler.cs
│   │   ├── MimeTypeHelper.cs
│   │   └── Models/
│   │       ├── AnalyzerDefinition.cs
│   │       ├── AnalyzerResponse.cs
│   │       ├── AnalyzeRequest.cs
│   │       ├── AnalyzeResult.cs
│   │       └── FieldSchema.cs
│   └── contentunderstanding.cs         # Single-file demo (dotnet run)
│
├── document-intelligence/              # Document Intelligence demos
│   └── documentIntelligence.cs         # Single-file demo (dotnet run)
│
├── analyzers/                          # Reusable custom analyzer definitions
│   └── README.md
│
├── ContentUnderstanding.slnx           # Solution file
└── README.md                           # This file
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later (single-file demos use .NET 10 `#:` directives)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) (for the client library)
- An Azure subscription with:
  - [Azure AI Content Understanding](https://learn.microsoft.com/en-us/azure/ai-services/content-understanding/) resource
  - [Azure AI Document Intelligence](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/) resource

## Getting Started

### Build the client library

```bash
dotnet build
```

### Run a Content Understanding demo

```bash
cd content-understanding
dotnet run contentunderstanding.cs
```

### Run a Document Intelligence demo

```bash
cd document-intelligence
dotnet run documentIntelligence.cs
```

## Content Understanding Client

The custom client library under `content-understanding/client/` wraps the [Azure AI Content Understanding REST API](https://learn.microsoft.com/en-us/rest/api/content-understanding/) and supports:

- **Create, retrieve, and delete analyzers** for document, image, audio, and video content
- **Analyze content from URLs** or **local files/directories**
- **Automatic result polling** with configurable intervals
- **Analyzer configuration** for OCR, face detection, and detailed results
- Built-in MIME type detection, strongly typed models, and `IDisposable` support

### Quick example

```csharp
using Azure.Identity;
using ContentUnderstanding.Client;

var endpoint = "https://<your-resource>.cognitiveservices.azure.com";
using var client = new ContentUnderstandingClient(endpoint, new DefaultAzureCredential());

var result = await client.AnalyzeContentFromFileAsync("my-analyzer", "/path/to/file.pdf");
```

### API Reference

| Method | Description |
|--------|-------------|
| `CreateOrReplaceAnalyzerAsync` | Creates or replaces an analyzer |
| `GetAnalyzerAsync` | Retrieves an existing analyzer |
| `DeleteAnalyzerAsync` | Deletes an analyzer |
| `AnalyzeContentFromUrlAsync` | Analyzes content at a URL |
| `AnalyzeContentFromFileAsync` | Analyzes a local file |
| `AnalyzeFilesInDirectoryAsync` | Batch-processes files in a directory |

## Custom Analyzers

The `analyzers/` folder contains reusable custom analyzer definitions you can use locally or share across demos.

## License

This project is provided as-is for educational and development purposes.
