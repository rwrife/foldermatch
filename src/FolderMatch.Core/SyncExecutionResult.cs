namespace FolderMatch.Core;

public sealed record SyncExecutionResult(
    bool DryRun,
    int AppliedCount,
    int SkippedCount,
    string? JournalPath,
    IReadOnlyList<string> Warnings);
