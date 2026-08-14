namespace FolderMatch.Core;

public sealed record HashPipelineResult(
    IReadOnlyList<HashFingerprint> Fingerprints,
    int PartialHashesComputed,
    int FullHashesComputed);
