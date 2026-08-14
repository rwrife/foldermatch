using System.Text.RegularExpressions;

namespace FolderMatch.Core;

public sealed class DiffEngine : IDiffEngine
{
    private readonly StagedHashPipeline _hashPipeline;

    public DiffEngine(StagedHashPipeline? hashPipeline = null)
    {
        _hashPipeline = hashPipeline ?? new StagedHashPipeline();
    }

    public async Task<DiffResult> ComputeAsync(
        FolderScanResult left,
        FolderScanResult right,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        options ??= new DiffOptions();
        var filter = new GlobFilter(options.IncludeGlobs, options.ExcludeGlobs);

        var allPaths = new HashSet<string>(left.EntriesByPath.Keys, PathNormalization.RelativePathComparer);
        allPaths.UnionWith(right.EntriesByPath.Keys);

        var orderedPaths = allPaths
            .OrderBy(static path => path, PathNormalization.RelativePathComparer)
            .ToArray();

        var fingerprintsById = await ComputeThoroughFingerprintsAsync(left, right, orderedPaths, filter, options, cancellationToken);

        var items = new List<DiffItem>(orderedPaths.Length);

        foreach (var relativePath in orderedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            left.EntriesByPath.TryGetValue(relativePath, out var leftEntry);
            right.EntriesByPath.TryGetValue(relativePath, out var rightEntry);

            if (!filter.IsMatch(relativePath) || !MatchesMetadataFilters(leftEntry, rightEntry, options))
            {
                continue;
            }

            DiffChangeType changeType;

            if (leftEntry is not null && rightEntry is null)
            {
                changeType = DiffChangeType.New;
            }
            else if (leftEntry is null && rightEntry is not null)
            {
                changeType = DiffChangeType.Deleted;
            }
            else
            {
                changeType = AreEntriesIdentical(relativePath, leftEntry!, rightEntry!, options.Mode, fingerprintsById)
                    ? DiffChangeType.Identical
                    : IsConflict(relativePath, leftEntry!, rightEntry!, options)
                        ? DiffChangeType.Conflict
                        : DiffChangeType.Updated;
            }

            items.Add(new DiffItem(relativePath, changeType, leftEntry, rightEntry));
        }

        return new DiffResult(items);
    }

    private async Task<IReadOnlyDictionary<string, HashFingerprint>> ComputeThoroughFingerprintsAsync(
        FolderScanResult left,
        FolderScanResult right,
        IReadOnlyList<string> orderedPaths,
        GlobFilter filter,
        DiffOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Mode != DiffCompareMode.Thorough)
        {
            return new Dictionary<string, HashFingerprint>(StringComparer.Ordinal);
        }

        var candidates = new List<HashCandidate>();

        foreach (var path in orderedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!filter.IsMatch(path))
            {
                continue;
            }

            left.EntriesByPath.TryGetValue(path, out var leftEntry);
            right.EntriesByPath.TryGetValue(path, out var rightEntry);

            if (leftEntry is null || rightEntry is null || !MatchesMetadataFilters(leftEntry, rightEntry, options))
            {
                continue;
            }

            if (leftEntry.IsDirectory || rightEntry.IsDirectory || leftEntry.Size != rightEntry.Size)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(leftEntry.AbsolutePath) || string.IsNullOrWhiteSpace(rightEntry.AbsolutePath))
            {
                continue;
            }

            candidates.Add(new HashCandidate($"L|{path}", leftEntry.AbsolutePath!, leftEntry.Size));
            candidates.Add(new HashCandidate($"R|{path}", rightEntry.AbsolutePath!, rightEntry.Size));
        }

        if (candidates.Count == 0)
        {
            return new Dictionary<string, HashFingerprint>(StringComparer.Ordinal);
        }

        var result = await _hashPipeline.ComputeAsync(candidates, cancellationToken);
        return result.Fingerprints.ToDictionary(static fingerprint => fingerprint.Id, StringComparer.Ordinal);
    }

    private static bool AreEntriesIdentical(
        string relativePath,
        FileEntry left,
        FileEntry right,
        DiffCompareMode mode,
        IReadOnlyDictionary<string, HashFingerprint> fingerprintsById)
    {
        if (left.IsDirectory || right.IsDirectory)
        {
            return left.IsDirectory == right.IsDirectory;
        }

        if (mode == DiffCompareMode.Quick)
        {
            return AreQuickEquivalent(left, right);
        }

        if (left.Size != right.Size)
        {
            return false;
        }

        var leftId = $"L|{relativePath}";
        var rightId = $"R|{relativePath}";

        if (fingerprintsById.TryGetValue(leftId, out var leftFingerprint) &&
            fingerprintsById.TryGetValue(rightId, out var rightFingerprint))
        {
            return string.Equals(leftFingerprint.ComparisonKey, rightFingerprint.ComparisonKey, StringComparison.Ordinal);
        }

        // Fallback when hash candidates are unavailable.
        return AreQuickEquivalent(left, right);
    }

    private static bool IsConflict(string relativePath, FileEntry left, FileEntry right, DiffOptions options)
    {
        if (options.BaselineEntriesByPath is null)
        {
            return false;
        }

        if (!options.BaselineEntriesByPath.TryGetValue(relativePath, out var baseline))
        {
            return false;
        }

        if (baseline.IsDirectory || left.IsDirectory || right.IsDirectory)
        {
            return false;
        }

        var leftChanged = !AreQuickEquivalent(left, baseline);
        var rightChanged = !AreQuickEquivalent(right, baseline);

        return leftChanged && rightChanged && !AreQuickEquivalent(left, right);
    }

    private static bool AreQuickEquivalent(FileEntry left, FileEntry right)
    {
        if (left.IsDirectory || right.IsDirectory)
        {
            return left.IsDirectory == right.IsDirectory;
        }

        return left.Size == right.Size && left.ModifiedUtc == right.ModifiedUtc;
    }

    private static bool MatchesMetadataFilters(FileEntry? left, FileEntry? right, DiffOptions options)
    {
        return EntryPassesMetadataFilters(left, options) && EntryPassesMetadataFilters(right, options);
    }

    private static bool EntryPassesMetadataFilters(FileEntry? entry, DiffOptions options)
    {
        if (entry is null || entry.IsDirectory)
        {
            return true;
        }

        if (options.MinSizeBytes.HasValue && entry.Size < options.MinSizeBytes.Value)
        {
            return false;
        }

        if (options.MaxSizeBytes.HasValue && entry.Size > options.MaxSizeBytes.Value)
        {
            return false;
        }

        if (options.ModifiedAfterUtc.HasValue && entry.ModifiedUtc < options.ModifiedAfterUtc.Value)
        {
            return false;
        }

        if (options.ModifiedBeforeUtc.HasValue && entry.ModifiedUtc > options.ModifiedBeforeUtc.Value)
        {
            return false;
        }

        return true;
    }

    private sealed class GlobFilter
    {
        private readonly IReadOnlyList<Regex> _includes;
        private readonly IReadOnlyList<Regex> _excludes;

        public GlobFilter(IReadOnlyList<string>? includes, IReadOnlyList<string>? excludes)
        {
            _includes = CompilePatterns(includes);
            _excludes = CompilePatterns(excludes);
        }

        public bool IsMatch(string relativePath)
        {
            var normalizedPath = PathNormalization.NormalizeRelativePath(relativePath);

            if (_includes.Count > 0 && !_includes.Any(regex => regex.IsMatch(normalizedPath)))
            {
                return false;
            }

            if (_excludes.Any(regex => regex.IsMatch(normalizedPath)))
            {
                return false;
            }

            return true;
        }

        private static IReadOnlyList<Regex> CompilePatterns(IReadOnlyList<string>? patterns)
        {
            if (patterns is null || patterns.Count == 0)
            {
                return Array.Empty<Regex>();
            }

            var regexes = new List<Regex>(patterns.Count);

            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                regexes.Add(CompileGlob(pattern));
            }

            return regexes;
        }

        private static Regex CompileGlob(string pattern)
        {
            var normalized = PathNormalization.NormalizeRelativePath(pattern.Trim());
            if (normalized.Length == 0 || normalized == "**")
            {
                return new Regex("^.*$", RegexOptionsFromPlatform());
            }

            var escaped = Regex.Escape(normalized)
                .Replace(@"\*\*/", @"(?:.*/)?", StringComparison.Ordinal)
                .Replace(@"/\*\*", @"/.*", StringComparison.Ordinal)
                .Replace(@"\*\*", @".*", StringComparison.Ordinal)
                .Replace(@"\*", @"[^/]*", StringComparison.Ordinal)
                .Replace(@"\?", @"[^/]", StringComparison.Ordinal);

            return new Regex($"^{escaped}$", RegexOptionsFromPlatform());
        }

        private static RegexOptions RegexOptionsFromPlatform()
        {
            var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (OperatingSystem.IsWindows())
            {
                options |= RegexOptions.IgnoreCase;
            }

            return options;
        }
    }
}
