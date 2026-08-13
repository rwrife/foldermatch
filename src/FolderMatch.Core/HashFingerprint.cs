namespace FolderMatch.Core;

public sealed record HashFingerprint(
    string Id,
    long Size,
    string? PartialHash,
    string? FullHash)
{
    public string ComparisonKey => FullHash ?? PartialHash ?? $"size:{Size}";
}
