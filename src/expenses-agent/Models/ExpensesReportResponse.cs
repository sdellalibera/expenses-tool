namespace agents.models;

/// <summary>
/// Result of creating an expenses report from a captured image.
/// </summary>
/// <param name="ConversationId">The conversation thread that owns this analysis.</param>
/// <param name="BlobName">The name of the uploaded image in blob storage.</param>
/// <param name="AnalysisSummary">The agent's natural-language summary of the extracted expense.</param>
public record ExpensesReportResponse(string ConversationId, string BlobName, string AnalysisSummary);
