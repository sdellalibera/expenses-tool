#:package Aspire.Hosting.Azure.CosmosDB@13.3.5
#:package Aspire.Hosting.Foundry@13.3.0-preview.1.26256.5
#:package Aspire.Hosting.SqlServer@13.3.5

#:sdk Aspire.AppHost.Sdk@13.3.0

#:project ../expenses-agent/expenses-agent.csproj
#:project ../content-understanding-mcpserver/content-understanding-mcpserver.csproj

using Projects;
using Aspire.Hosting.Azure.CosmosDB;
using Aspire.Hosting.Foundry;

var builder = DistributedApplication.CreateBuilder(args);

var existingFoundryName = builder.AddParameter("existingFoundryName");
var existingFoundryResourceGroup = builder.AddParameter("existingFoundryResourceGroup");
var foundry = builder.AddFoundry("foundry").RunAsExisting(existingFoundryName,existingFoundryResourceGroup);

//subject to removal or change in future, requires pragma
#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmos-db")
    .RunAsPreviewEmulator(
        emulator =>
        {
            emulator.WithDataExplorer();
            emulator.WithLifetime(ContainerLifetime.Persistent);
        });

var db = cosmos.AddCosmosDatabase("db");
var sessions = db.AddContainer("sessions","/sessionsId");
var conversations = db.AddContainer("conversations","/conversationsId");

// SQL Server container hosting the expenses database. The DAB-powered SQL MCP
// server below exposes this database to agents via the Model Context Protocol.
// The creation script provisions a schema that matches the Content Understanding
// "MultipleInvoices" custom analyzer output (one analyzed document yields many
// invoices, and each invoice has many line items).
var sqlDb = builder.AddSqlServer("sql-server")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("sql-database", "expensesdb")
    .WithCreationScript("""
        IF DB_ID('expensesdb') IS NULL CREATE DATABASE expensesdb;
        GO
        USE expensesdb;
        GO

        IF OBJECT_ID('dbo.InvoiceLineItems', 'U') IS NOT NULL DROP TABLE dbo.InvoiceLineItems;
        IF OBJECT_ID('dbo.Invoices', 'U') IS NOT NULL DROP TABLE dbo.Invoices;
        IF OBJECT_ID('dbo.Documents', 'U') IS NOT NULL DROP TABLE dbo.Documents;

        CREATE TABLE dbo.Documents (
            Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Documents PRIMARY KEY,
            SourceName    NVARCHAR(400)     NULL,
            AnalyzerId    NVARCHAR(200)     NULL,
            AnalyzedAt    DATETIME2(0)      NOT NULL CONSTRAINT DF_Documents_AnalyzedAt DEFAULT SYSUTCDATETIME()
        );

        CREATE TABLE dbo.Invoices (
            Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Invoices PRIMARY KEY,
            DocumentId      INT               NOT NULL,
            VendorName      NVARCHAR(400)     NULL,
            InvoiceDate     DATE              NULL,
            PaymentMethod   NVARCHAR(100)     NULL,
            TotalAmount     DECIMAL(18,2)     NULL,
            Currency        NVARCHAR(10)      NULL,
            VendorNameConfidence    FLOAT     NULL,
            DateConfidence          FLOAT     NULL,
            PaymentMethodConfidence FLOAT     NULL,
            TotalAmountConfidence   FLOAT     NULL,
            CurrencyConfidence      FLOAT     NULL,
            CONSTRAINT FK_Invoices_Documents FOREIGN KEY (DocumentId)
                REFERENCES dbo.Documents(Id) ON DELETE CASCADE
        );

        CREATE TABLE dbo.InvoiceLineItems (
            Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InvoiceLineItems PRIMARY KEY,
            InvoiceId     INT               NOT NULL,
            ItemName      NVARCHAR(400)     NULL,
            Price         DECIMAL(18,2)     NULL,
            ItemNameConfidence FLOAT        NULL,
            PriceConfidence    FLOAT        NULL,
            CONSTRAINT FK_LineItems_Invoices FOREIGN KEY (InvoiceId)
                REFERENCES dbo.Invoices(Id) ON DELETE CASCADE
        );

        CREATE INDEX IX_Invoices_DocumentId ON dbo.Invoices(DocumentId);
        CREATE INDEX IX_LineItems_InvoiceId ON dbo.InvoiceLineItems(InvoiceId);
        """);

// SQL MCP server powered by Data API builder. Exposes the SQL database as MCP
// tools. Configuration is supplied via the bind-mounted dab-config.json file
// located next to this AppHost.
var sqlMcp = builder.AddContainer("sql-mcp-server", "azure-databases/data-api-builder", "2.0.1-rc")
    .WithImageRegistry("mcr.microsoft.com")
    .WithHttpEndpoint(targetPort: 5000, name: "http")
    .WithEnvironment("MSSQL_CONNECTION_STRING", sqlDb)
    .WithBindMount("dab-config.json", "/App/dab-config.json", true)
    .WaitFor(sqlDb);

var mcpserver = builder.AddProject<Projects.content_understanding_mcpserver>("mcpserver")
    .WithHttpEndpoint()
    .WithReference(foundry).WaitFor(foundry);


var expensesAgent = builder.AddProject<Projects.expenses_agent>("expenses-agent")
    .WithReference(foundry).WaitFor(foundry)
    .WithReference(conversations).WaitFor(conversations)
    .WithReference(mcpserver).WaitFor(mcpserver)
    .WithEnvironment("MCPSERVER_HTTP", mcpserver.GetEndpoint("http"))
    .WithEnvironment("SQL_MCP_HTTP", sqlMcp.GetEndpoint("http"))
    .WaitFor(sqlMcp);

builder.Build().Run();
