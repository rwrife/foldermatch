namespace FolderMatch.Core;

public sealed class StagedHashPipeline
{
    private readonly IHasher _hasher;

    public StagedHashPipeline(IHasher? hasher = null)
    {
        _hasher = hasher ?? new StreamHasher();
    }

    public async Task<HashPipelineResult> ComputeAsync(
        IEnumerable<HashCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var orderedCandidates = candidates
            .OrderBy(static candidate => candidate.Size)
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();

        var fingerprintsById = new Dictionary<string, HashFingerprint>(StringComparer.Ordinal);
        var partialHashesComputed = 0;
        var fullHashesComputed = 0;

        foreach (var sizeBucket in orderedCandidates.GroupBy(static candidate => candidate.Size))
        {
            var bucket = sizeBucket.ToArray();

            if (bucket.Length == 1)
            {
                var single = bucket[0];
                fingerprintsById[single.Id] = new HashFingerprint(single.Id, single.Size, PartialHash: null, FullHash: null);
                continue;
            }

            var partialHashesByCandidate = new Dictionary<HashCandidate, string>();

            foreach (var candidate in bucket)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var stream = File.OpenRead(candidate.AbsolutePath);
                var partialHash = await _hasher.ComputePartialHashAsync(stream, cancellationToken);
                partialHashesByCandidate[candidate] = partialHash;
                partialHashesComputed++;
            }

            foreach (var partialBucket in bucket.GroupBy(candidate => partialHashesByCandidate[candidate], StringComparer.Ordinal))
            {
                var collisions = partialBucket.ToArray();

                if (collisions.Length == 1)
                {
                    var single = collisions[0];
                    fingerprintsById[single.Id] = new HashFingerprint(
                        single.Id,
                        single.Size,
                        partialHashesByCandidate[single],
                        FullHash: null);
                    continue;
                }

                foreach (var candidate in collisions)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await using var stream = File.OpenRead(candidate.AbsolutePath);
                    var fullHash = await _hasher.ComputeFullHashAsync(stream, cancellationToken);
                    fullHashesComputed++;

                    fingerprintsById[candidate.Id] = new HashFingerprint(
                        candidate.Id,
                        candidate.Size,
                        partialHashesByCandidate[candidate],
                        fullHash);
                }
            }
        }

        var orderedFingerprints = fingerprintsById.Values
            .OrderBy(static fingerprint => fingerprint.Id, StringComparer.Ordinal)
            .ToArray();

        return new HashPipelineResult(orderedFingerprints, partialHashesComputed, fullHashesComputed);
    }
}
