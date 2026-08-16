namespace FolderMatch.Core;

public interface IChangeSummarizer
{
    Task<ChangeSummaryResult> SummarizeAsync(DiffResult diffResult, CancellationToken cancellationToken = default);
}
