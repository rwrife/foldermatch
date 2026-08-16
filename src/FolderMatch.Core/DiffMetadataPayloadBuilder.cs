namespace FolderMatch.Core;

public static class DiffMetadataPayloadBuilder
{
    public static DiffAiSummaryRequest Build(DiffResult diffResult, int maxItems = 500)
    {
        ArgumentNullException.ThrowIfNull(diffResult);

        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), maxItems, "Max metadata items must be greater than zero.");
        }

        var changeEntries = diffResult.Items
            .Where(static item => item.ChangeType != DiffChangeType.Identical)
            .OrderBy(GetPriority)
            .ThenBy(static item => item.RelativePath, PathNormalization.RelativePathComparer)
            .Take(maxItems)
            .Select(static item => new DiffAiSummaryEntry(
                RelativePath: item.RelativePath,
                ChangeType: item.ChangeType,
                IsDirectory: item.LeftInfo?.IsDirectory ?? item.RightInfo?.IsDirectory ?? false,
                LeftSizeBytes: item.LeftInfo?.IsDirectory == true ? null : item.LeftInfo?.Size,
                RightSizeBytes: item.RightInfo?.IsDirectory == true ? null : item.RightInfo?.Size,
                LeftModifiedUtc: item.LeftInfo?.ModifiedUtc,
                RightModifiedUtc: item.RightInfo?.ModifiedUtc))
            .ToArray();

        return new DiffAiSummaryRequest(
            CountsSummary: diffResult.CountsSummary,
            TotalEntries: diffResult.Items.Count,
            IncludedEntryCount: changeEntries.Length,
            Entries: changeEntries);
    }

    private static int GetPriority(DiffItem item)
    {
        return item.ChangeType switch
        {
            DiffChangeType.Conflict => 0,
            DiffChangeType.Updated => 1,
            DiffChangeType.New => 2,
            DiffChangeType.Deleted => 3,
            DiffChangeType.Identical => 4,
            _ => 5
        };
    }
}
