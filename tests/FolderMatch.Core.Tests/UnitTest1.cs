using System.Diagnostics;
using FolderMatch.Core;

namespace FolderMatch.Core.Tests;

public sealed class FolderScannerTests
{
    [Fact]
    public async Task ScanAsync_ReturnsDeterministicNormalizedEntries_ForFixtureTree()
    {
        using var fixture = new TempDir();

        Directory.CreateDirectory(Path.Combine(fixture.Path, "SubDir"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "SubDir", "child.txt"), "child");

        IFolderScanner scanner = new FolderScanner();
        FolderScanResult result = await scanner.ScanAsync(fixture.Path);

        var orderedPaths = result.Entries.Select(e => e.RelativePath).ToArray();

        Assert.Equal(orderedPaths.OrderBy(p => p, StringComparer.Ordinal).ToArray(), orderedPaths);
        Assert.Contains("root.txt", orderedPaths);
        Assert.Contains("SubDir", orderedPaths);
        Assert.Contains("SubDir/child.txt", orderedPaths);

        var child = result.Entries.Single(e => e.RelativePath == "SubDir/child.txt");
        Assert.False(child.IsDirectory);
        Assert.True(child.Size > 0);
    }

    [Fact]
    public async Task ScanAsync_SkipsAccessDeniedEntries_AndReportsWarning()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TempDir();

        var readableFile = Path.Combine(fixture.Path, "ok.txt");
        await File.WriteAllTextAsync(readableFile, "ok");

        var restrictedDir = Path.Combine(fixture.Path, "restricted");
        Directory.CreateDirectory(restrictedDir);
        await File.WriteAllTextAsync(Path.Combine(restrictedDir, "hidden.txt"), "nope");

        File.SetUnixFileMode(restrictedDir, UnixFileMode.None);

        try
        {
            IFolderScanner scanner = new FolderScanner();
            FolderScanResult result = await scanner.ScanAsync(fixture.Path);

            Assert.Contains(result.Entries, e => e.RelativePath == "ok.txt");
            Assert.DoesNotContain(result.Entries, e => e.RelativePath.Contains("hidden.txt", StringComparison.Ordinal));
            Assert.Contains(result.Warnings, w =>
                w.Code == ScanWarningCode.AccessDenied &&
                w.Path.Contains("restricted", StringComparison.Ordinal));
        }
        finally
        {
            File.SetUnixFileMode(restrictedDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task ScanAsync_DoesNotFollowSymlinkCycles()
    {
        using var fixture = new TempDir();

        var dirA = Path.Combine(fixture.Path, "A");
        Directory.CreateDirectory(dirA);
        await File.WriteAllTextAsync(Path.Combine(dirA, "a.txt"), "a");

        var loopPath = Path.Combine(dirA, "loop");

        try
        {
            Directory.CreateSymbolicLink(loopPath, fixture.Path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is PlatformNotSupportedException || ex is IOException)
        {
            return;
        }

        IFolderScanner scanner = new FolderScanner();

        var sw = Stopwatch.StartNew();
        FolderScanResult result = await scanner.ScanAsync(fixture.Path);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(result.Entries, e => e.RelativePath.StartsWith("A/loop/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_HonorsCancellation_AndReportsProgress()
    {
        using var fixture = new TempDir();

        for (var i = 0; i < 400; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.Path, $"file-{i:000}.txt"), "x");
        }

        using var cts = new CancellationTokenSource();

        var progressCount = 0;
        var progress = new Progress<ScanProgress>(_ =>
        {
            progressCount++;
            if (progressCount >= 10)
            {
                cts.Cancel();
            }
        });

        IFolderScanner scanner = new FolderScanner();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(fixture.Path, progress, cts.Token));
        Assert.True(progressCount >= 1);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"foldermatch-tests-{Guid.NewGuid():N}");
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
                // Best effort cleanup for tests.
            }
        }
    }
}
