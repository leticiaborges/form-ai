namespace FormAI.Application.AI;

public record AnalysisResult(string Summary, IReadOnlyList<string> Insights);

public interface IAnalysisService
{
    Task<AnalysisResult> AnalyzeResultsAsync(
        Guid formId,
        string aggregatedResponsesJson,
        CancellationToken cancellationToken = default);
}
