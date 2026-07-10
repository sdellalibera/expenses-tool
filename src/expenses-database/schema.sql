-- Creation script for the expenses PoC database.
--
-- This script is executed by the Aspire AppHost as the SQL Server "creation
-- script" for the `expenses-database` resource (physical database `expensesdb`).
-- Because it replaces Aspire's default database-creation script, it must:
--   * create the database itself, and
--   * be idempotent (safe to run every time the SQL container starts).
--
-- Data model follows the "blob reference" approach: receipt/invoice images are
-- stored in Azure Blob Storage and only a reference (ReceiptBlobKey) plus a few
-- integrity/metadata columns are kept in SQL. The image bytes are never stored
-- in the database.

IF DB_ID('expensesdb') IS NULL
    CREATE DATABASE [expensesdb];
GO

USE [expensesdb];
GO

-- An expense report groups the individual expenses submitted by a user.
IF OBJECT_ID('dbo.ExpenseReport', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExpenseReport
    (
        ReportId     INT            IDENTITY (1, 1) NOT NULL,
        UserId       NVARCHAR(128)  NOT NULL,
        Title        NVARCHAR(200)  NOT NULL,
        Status       NVARCHAR(50)   NOT NULL CONSTRAINT DF_ExpenseReport_Status    DEFAULT ('Draft'),
        SubmittedAt  DATETIME2(3)   NULL,
        CreatedAt    DATETIME2(3)   NOT NULL CONSTRAINT DF_ExpenseReport_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ExpenseReport PRIMARY KEY (ReportId)
    );
END
GO

-- A single expense line item extracted from a receipt/invoice image.
-- The receipt image lives in blob storage; only the reference is kept here.
IF OBJECT_ID('dbo.Expense', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Expense
    (
        ExpenseId       INT            IDENTITY (1, 1) NOT NULL,
        ReportId        INT            NOT NULL,
        UserId          NVARCHAR(128)  NOT NULL,
        Vendor          NVARCHAR(200)  NULL,
        Amount          DECIMAL(12, 2) NULL,
        Currency        CHAR(3)        NULL,
        ExpenseDate     DATE           NULL,
        Category        NVARCHAR(100)  NULL,
        Description     NVARCHAR(500)  NULL,
        ReceiptBlobKey  NVARCHAR(400)  NULL,   -- stable blob key, e.g. receipts/{userId}/{reportId}/{expenseId}.jpg
        ReceiptSha256   CHAR(64)       NULL,   -- integrity / dedup
        ReceiptBytes    INT            NULL,
        ReceiptMime     NVARCHAR(100)  NULL,
        CreatedAt       DATETIME2(3)   NOT NULL CONSTRAINT DF_Expense_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Expense PRIMARY KEY (ExpenseId),
        CONSTRAINT FK_Expense_ExpenseReport FOREIGN KEY (ReportId) REFERENCES dbo.ExpenseReport (ReportId)
    );
END
GO
