namespace FolderMatch.Core;

public sealed class ChangeSummarizer : IChangeSummarizer
{
    private readonly IChangeSummarizer _ruleBasedSummarizer;
    private readonly IDiffAiService? _diffAiService;

    public ChangeSummarizer(IDiffAiService? diffAiService = null, IChangeSummarizer? ruleBasedSummarizer = null)
    {
        _diffAiService = diffAiService;
        _ruleBasedSummarizer = ruleBasedSummarizer ?? new RuleBasedChangeSummarizer();
    }

    public async Task<ChangeSummaryResult> SummarizeAsync(DiffResult diffResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diffResult);

        var fallback = await _ruleBasedSummarizer.SummarizeAsync(diffResult, cancellationToken);

        if (_diffAiService is null || !_diffAiService.Enabled)
        {
            return fallback;
        }

        var reachable = await _diffAiService.IsReachableAsync(cancellationToken);
        if (!reachable)
        {
            return fallback;
        }

        try
        {
            var request = DiffMetadataPayloadBuilder.Build(diffResult);
            var aiSummary = await _diffAiService.SummarizeAsync(request, cancellationToken);

            if (string.IsNullOrWhiteSpace(aiSummary))
            {
                return fallback;
            }

            return new ChangeSummaryResult(aiSummary.Trim(), ChangeSummarySource.LocalAi);
        }
        catch
        {
            return fallback;
        }
    }
}
