namespace FolderMatch.Core;

public interface IDiffAiService
{
    bool Enabled { get; }

    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);

    Task<string?> SummarizeAsync(DiffAiSummaryRequest request, CancellationToken cancellationToken = default);
}
