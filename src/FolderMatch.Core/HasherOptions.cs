namespace FolderMatch.Core;

public sealed record HasherOptions
{
    public HashAlgorithmKind Algorithm { get; init; } = HashAlgorithmKind.XxHash64;

    public int PartialBlockSizeBytes { get; init; } = 64 * 1024;
}
