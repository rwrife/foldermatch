namespace FolderMatch.Core;

public sealed record DiffOptions
{
    public DiffCompareMode Mode { get; init; } = DiffCompareMode.Quick;

    public IReadOnlyList<string> IncludeGlobs { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExcludeGlobs { get; init; } = Array.Empty<string>();

    public long? MinSizeBytes { get; init; }

    public long? MaxSizeBytes { get; init; }

    public DateTimeOffset? ModifiedAfterUtc { get; init; }

    public DateTimeOffset? ModifiedBeforeUtc { get; init; }

    public IReadOnlyDictionary<string, FileEntry>? BaselineEntriesByPath { get; init; }
}
