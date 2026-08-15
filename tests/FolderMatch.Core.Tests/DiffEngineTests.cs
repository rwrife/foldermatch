using FolderMatch.Core;

namespace FolderMatch.Core.Tests;

public sealed class DiffEngineTests
{
    [Fact]
    public async Task ComputeAsync_ClassifiesAllChangeTypes_AndBuildsSummaryCounts()
    {
        using var baselineDir = new TempDir("baseline");
        using var leftDir = new TempDir("left");
        using var rightDir = new TempDir("right");

        var t0 = new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(1);
        var t2 = t0.AddHours(2);

        WriteFile(Path.Combine(baselineDir.Path, "same.txt"), "same", t0);
        WriteFile(Path.Combine(leftDir.Path, "same.txt"), "same", t0);
        WriteFile(Path.Combine(rightDir.Path, "same.txt"), "same", t0);

        WriteFile(Path.Combine(leftDir.Path, "new.txt"), "new-left", t1);

        WriteFile(Path.Combine(rightDir.Path, "deleted.txt"), "right-only", t1);

        WriteFile(Path.Combine(baselineDir.Path, "updated.txt"), "right-unchanged", t0);
        WriteFile(Path.Combine(rightDir.Path, "updated.txt"), "right-unchanged", t0);
        WriteFile(Path.Combine(leftDir.Path, "updated.txt"), "left-updated", t1);

        WriteFile(Path.Combine(baselineDir.Path, "conflict.txt"), "base", t0);
        WriteFile(Path.Combine(leftDir.Path, "conflict.txt"), "left-change", t1);
        WriteFile(Path.Combine(rightDir.Path, "conflict.txt"), "right-change!!", t2);

        IFolderScanner scanner = new FolderScanner();
        var baselineScan = await scanner.ScanAsync(baselineDir.Path);
        var leftScan = await scanner.ScanAsync(leftDir.Path);
        var rightScan = await scanner.ScanAsync(rightDir.Path);

        IDiffEngine diffEngine = new DiffEngine();
        var result = await diffEngine.ComputeAsync(leftScan, rightScan, new DiffOptions
        {
            Mode = DiffCompareMode.Quick,
            BaselineEntriesByPath = baselineScan.EntriesByPath
        });

        Assert.Equal(5, result.Items.Count);
        Assert.Equal(1, result.IdenticalCount);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(1, result.ConflictCount);
        Assert.Equal("=1 +1 ~1 -1 !1", result.CountsSummary);

        Assert.Equal(DiffChangeType.Identical, result.Items.Single(i => i.RelativePath == "same.txt").ChangeType);
        Assert.Equal(DiffChangeType.New, result.Items.Single(i => i.RelativePath == "new.txt").ChangeType);
        Assert.Equal(DiffChangeType.Deleted, result.Items.Single(i => i.RelativePath == "deleted.txt").ChangeType);
        Assert.Equal(DiffChangeType.Updated, result.Items.Single(i => i.RelativePath == "updated.txt").ChangeType);
        Assert.Equal(DiffChangeType.Conflict, result.Items.Single(i => i.RelativePath == "conflict.txt").ChangeType);
    }

    [Fact]
    public async Task ComputeAsync_ThoroughModeDetectsSameSizeSameMtimeContentDifferences()
    {
        using var leftDir = new TempDir("left-thorough");
        using var rightDir = new TempDir("right-thorough");

        var fixedTimestamp = new DateTimeOffset(2026, 02, 02, 0, 0, 0, TimeSpan.Zero);

        var leftBytes = CreateBytes(16_384, seed: 100);
        var rightBytes = CreateBytes(16_384, seed: 101);

        var leftPath = Path.Combine(leftDir.Path, "hash-diff.bin");
        var rightPath = Path.Combine(rightDir.Path, "hash-diff.bin");

        WriteFile(leftPath, leftBytes, fixedTimestamp);
        WriteFile(rightPath, rightBytes, fixedTimestamp);

        IFolderScanner scanner = new FolderScanner();
        var leftScan = await scanner.ScanAsync(leftDir.Path);
        var rightScan = await scanner.ScanAsync(rightDir.Path);

        IDiffEngine diffEngine = new DiffEngine();

        var quickResult = await diffEngine.ComputeAsync(leftScan, rightScan, new DiffOptions { Mode = DiffCompareMode.Quick });
        var thoroughResult = await diffEngine.ComputeAsync(leftScan, rightScan, new DiffOptions { Mode = DiffCompareMode.Thorough });

        Assert.Equal(DiffChangeType.Identical, quickResult.Items.Single(i => i.RelativePath == "hash-diff.bin").ChangeType);
        Assert.Equal(DiffChangeType.Updated, thoroughResult.Items.Single(i => i.RelativePath == "hash-diff.bin").ChangeType);
    }

    [Fact]
    public async Task ComputeAsync_HonorsGlobSizeAndDateFilters()
    {
        using var leftDir = new TempDir("left-filter");
        using var rightDir = new TempDir("right-filter");

        var now = new DateTimeOffset(2026, 03, 03, 0, 0, 0, TimeSpan.Zero);
        var cutoff = now.AddDays(-1);

        WriteFile(Path.Combine(leftDir.Path, "keep.txt"), "keep-content", now);
        WriteFile(Path.Combine(rightDir.Path, "keep.txt"), "keep-content", now);

        Directory.CreateDirectory(Path.Combine(leftDir.Path, "logs"));
        Directory.CreateDirectory(Path.Combine(rightDir.Path, "logs"));
        WriteFile(Path.Combine(leftDir.Path, "logs", "keep.log"), "left-log", now);
        WriteFile(Path.Combine(rightDir.Path, "logs", "keep.log"), "right-log", now);

        Directory.CreateDirectory(Path.Combine(leftDir.Path, "skip"));
        Directory.CreateDirectory(Path.Combine(rightDir.Path, "skip"));
        WriteFile(Path.Combine(leftDir.Path, "skip", "me.txt"), "left-skip", now);
        WriteFile(Path.Combine(rightDir.Path, "skip", "me.txt"), "right-skip", now);

        WriteFile(Path.Combine(leftDir.Path, "tiny.txt"), "x", now);
        WriteFile(Path.Combine(leftDir.Path, "old.txt"), "old-file", cutoff.AddMinutes(-10));
        WriteFile(Path.Combine(leftDir.Path, "window.txt"), "fresh-file", now);

        IFolderScanner scanner = new FolderScanner();
        var leftScan = await scanner.ScanAsync(leftDir.Path);
        var rightScan = await scanner.ScanAsync(rightDir.Path);

        IDiffEngine diffEngine = new DiffEngine();
        var result = await diffEngine.ComputeAsync(leftScan, rightScan, new DiffOptions
        {
            Mode = DiffCompareMode.Quick,
            IncludeGlobs = ["**/*.txt"],
            ExcludeGlobs = ["**/skip/**"],
            MinSizeBytes = 2,
            ModifiedAfterUtc = cutoff
        });

        var paths = result.Items.Select(i => i.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        Assert.Equal(["keep.txt", "window.txt"], paths);
        Assert.Equal(DiffChangeType.Identical, result.Items.Single(i => i.RelativePath == "keep.txt").ChangeType);
        Assert.Equal(DiffChangeType.New, result.Items.Single(i => i.RelativePath == "window.txt").ChangeType);
    }

    private static byte[] CreateBytes(int length, int seed)
    {
        var random = new Random(seed);
        var data = new byte[length];
        random.NextBytes(data);
        return data;
    }

    private static void WriteFile(string path, string content, DateTimeOffset modifiedUtc)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        WriteFile(path, bytes, modifiedUtc);
    }

    private static void WriteFile(string path, byte[] bytes, DateTimeOffset modifiedUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, modifiedUtc.UtcDateTime);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"foldermatch-{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
