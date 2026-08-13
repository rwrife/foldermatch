namespace FolderMatch.Core;

public interface IHasher
{
    HashAlgorithmKind Algorithm { get; }

    ValueTask<string> ComputePartialHashAsync(Stream source, CancellationToken cancellationToken = default);

    ValueTask<string> ComputeFullHashAsync(Stream source, CancellationToken cancellationToken = default);
}
