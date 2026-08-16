using System.Text.Json;
using FolderMatch.Core;

namespace FolderMatch.Core.Tests;

public sealed class ChangeSummarizerTests
{
    [Fact]
    public async Task SummarizeAsync_UsesRuleBasedSummary_WhenAiIsDisabled()
    {
        var diff = BuildSampleDiff();
        IChangeSummarizer summarizer = new ChangeSummarizer();

        var result = await summarizer.SummarizeAsync(diff);

        Assert.Equal(ChangeSummarySource.RuleBased, result.Source);
        Assert.Contains("Compared", result.Summary, StringComparison.Ordinal);
        Assert.Contains(diff.CountsSummary, result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeAsync_FallsBackToRuleBased_WhenAiServiceIsUnreachable()
    {
        var diff = BuildSampleDiff();
        var ai = new StubDiffAiService
        {
            Enabled = true,
            Reachable = false,
            Summary = "ai summary should not be used"
        };

        IChangeSummarizer summarizer = new ChangeSummarizer(ai);
        var result = await summarizer.SummarizeAsync(diff);

        Assert.Equal(ChangeSummarySource.RuleBased, result.Source);
        Assert.False(ai.SummarizeCalled);
    }

    [Fact]
    public async Task SummarizeAsync_UsesAiSummary_WhenReachableAndNonEmpty()
    {
        var diff = BuildSampleDiff();
        var ai = new StubDiffAiService
        {
            Enabled = true,
            Reachable = true,
            Summary = "Most changes are in docs and src; one conflict needs manual review."
        };

        IChangeSummarizer summarizer = new ChangeSummarizer(ai);
        var result = await summarizer.SummarizeAsync(diff);

        Assert.Equal(ChangeSummarySource.LocalAi, result.Source);
        Assert.Equal(ai.Summary, result.Summary);
    }

    [Fact]
    public void BuildMetadataPayload_IncludesMetadataOnly_NoAbsolutePathsOrContents()
    {
        var now = new DateTimeOffset(2026, 05, 01, 0, 0, 0, TimeSpan.Zero);
        var left = new FileEntry("docs/report.txt", 42, now, FileAttributes.Normal, false, "/left/secret/report.txt");
        var right = new FileEntry("docs/report.txt", 50, now.AddMinutes(1), FileAttributes.Normal, false, "/right/private/report.txt");

        var diff = new DiffResult(new[]
        {
            new DiffItem("docs/report.txt", DiffChangeType.Updated, left, right)
        });

        var payload = DiffMetadataPayloadBuilder.Build(diff);
        var json = JsonSerializer.Serialize(payload);

        Assert.Contains("docs/report.txt", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/left/secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/right/private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AbsolutePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, payload.IncludedEntryCount);
    }

    [Fact]
    public void LocalhostDiffAiService_RejectsNonLocalhostEndpoint()
    {
        Assert.Throws<ArgumentException>(() =>
            new LocalhostDiffAiService(new LocalDiffAiOptions
            {
                Enabled = true,
                Endpoint = "https://example.com/v1/chat/completions"
            }));
    }

    private static DiffResult BuildSampleDiff()
    {
        var now = new DateTimeOffset(2026, 05, 02, 0, 0, 0, TimeSpan.Zero);

        var leftConflict = new FileEntry("src/conflict.cs", 10, now.AddMinutes(2), FileAttributes.Normal, false, "/left/src/conflict.cs");
        var rightConflict = new FileEntry("src/conflict.cs", 12, now.AddMinutes(1), FileAttributes.Normal, false, "/right/src/conflict.cs");
        var leftNew = new FileEntry("docs/new.md", 8, now.AddMinutes(1), FileAttributes.Normal, false, "/left/docs/new.md");
        var rightDeleted = new FileEntry("assets/logo.png", 1_024, now, FileAttributes.Normal, false, "/right/assets/logo.png");
        var leftSame = new FileEntry("README.md", 20, now, FileAttributes.Normal, false, "/left/README.md");
        var rightSame = new FileEntry("README.md", 20, now, FileAttributes.Normal, false, "/right/README.md");

        return new DiffResult(new[]
        {
            new DiffItem("src/conflict.cs", DiffChangeType.Conflict, leftConflict, rightConflict),
            new DiffItem("docs/new.md", DiffChangeType.New, leftNew, null),
            new DiffItem("assets/logo.png", DiffChangeType.Deleted, null, rightDeleted),
            new DiffItem("README.md", DiffChangeType.Identical, leftSame, rightSame)
        });
    }

    private sealed class StubDiffAiService : IDiffAiService
    {
        public bool Enabled { get; init; }

        public bool Reachable { get; init; }

        public string? Summary { get; init; }

        public bool SummarizeCalled { get; private set; }

        public Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Reachable);
        }

        public Task<string?> SummarizeAsync(DiffAiSummaryRequest request, CancellationToken cancellationToken = default)
        {
            SummarizeCalled = true;
            return Task.FromResult(Summary);
        }
    }
}
