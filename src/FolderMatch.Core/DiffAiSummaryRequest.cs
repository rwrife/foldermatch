namespace FolderMatch.Core;

public sealed record DiffAiSummaryRequest(
    string CountsSummary,
    int TotalEntries,
    int IncludedEntryCount,
    IReadOnlyList<DiffAiSummaryEntry> Entries);

public sealed record DiffAiSummaryEntry(
    string RelativePath,
    DiffChangeType ChangeType,
    bool IsDirectory,
    long? LeftSizeBytes,
    long? RightSizeBytes,
    DateTimeOffset? LeftModifiedUtc,
    DateTimeOffset? RightModifiedUtc);
