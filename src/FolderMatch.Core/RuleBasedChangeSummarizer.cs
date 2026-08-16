namespace FolderMatch.Core;

public sealed class RuleBasedChangeSummarizer : IChangeSummarizer
{
    public Task<ChangeSummaryResult> SummarizeAsync(DiffResult diffResult, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var summary = RuleBasedSummaryFormatter.Build(diffResult);
        return Task.FromResult(new ChangeSummaryResult(summary, ChangeSummarySource.RuleBased));
    }
}
