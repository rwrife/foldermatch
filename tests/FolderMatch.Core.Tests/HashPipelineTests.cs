using System.Text;
using FolderMatch.Core;

namespace FolderMatch.Core.Tests;

public sealed class HashPipelineTests
{
    [Fact]
    public async Task ComputeAsync_IdenticalFiles_UsesPartialThenFullHash()
    {
        using var fixture = new TempDir();

        var left = Path.Combine(fixture.Path, "left.bin");
        var right = Path.Combine(fixture.Path, "right.bin");

        var content = CreateBytes(32_768, seed: 11);
        await File.WriteAllBytesAsync(left, content);
        await File.WriteAllBytesAsync(right, content);

        var pipeline = new StagedHashPipeline(new StreamHasher(new HasherOptions { PartialBlockSizeBytes = 4_096 }));

        var result = await pipeline.ComputeAsync(
        [
            new HashCandidate("left", left, new FileInfo(left).Length),
            new HashCandidate("right", right, new FileInfo(right).Length)
        ]);

        Assert.Equal(2, result.PartialHashesComputed);
        Assert.Equal(2, result.FullHashesComputed);

        var leftFingerprint = result.Fingerprints.Single(f => f.Id == "left");
        var rightFingerprint = result.Fingerprints.Single(f => f.Id == "right");

        Assert.Equal(leftFingerprint.PartialHash, rightFingerprint.PartialHash);
        Assert.Equal(leftFingerprint.FullHash, rightFingerprint.FullHash);
    }

    [Fact]
    public async Task ComputeAsync_SameSizeDifferentContent_AvoidsFullHashWhenPartialDiffers()
    {
        using var fixture = new TempDir();

        var a = Path.Combine(fixture.Path, "a.bin");
        var b = Path.Combine(fixture.Path, "b.bin");

        var bytesA = CreateBytes(8_192, seed: 21);
        var bytesB = CreateBytes(8_192, seed: 22);

        await File.WriteAllBytesAsync(a, bytesA);
        await File.WriteAllBytesAsync(b, bytesB);

        var pipeline = new StagedHashPipeline(new StreamHasher(new HasherOptions { PartialBlockSizeBytes = 2_048 }));

        var result = await pipeline.ComputeAsync(
        [
            new HashCandidate("a", a, bytesA.Length),
            new HashCandidate("b", b, bytesB.Length)
        ]);

        Assert.Equal(2, result.PartialHashesComputed);
        Assert.Equal(0, result.FullHashesComputed);

        var aFingerprint = result.Fingerprints.Single(f => f.Id == "a");
        var bFingerprint = result.Fingerprints.Single(f => f.Id == "b");

        Assert.NotEqual(aFingerprint.PartialHash, bFingerprint.PartialHash);
        Assert.Null(aFingerprint.FullHash);
        Assert.Null(bFingerprint.FullHash);
    }

    [Fact]
    public async Task ComputeAsync_LargeFiles_OnlyFullHashesPartialCollisions()
    {
        using var fixture = new TempDir();

        const int partialBlockSize = 4_096;
        const int fileSize = partialBlockSize * 4;

        var baseline = CreateBytes(fileSize, seed: 123);

        var sameHeadTailDifferentMiddle = baseline.ToArray();
        for (var i = partialBlockSize; i < (fileSize - partialBlockSize); i++)
        {
            sameHeadTailDifferentMiddle[i] = (byte)(sameHeadTailDifferentMiddle[i] ^ 0x5A);
        }

        var differentTail = baseline.ToArray();
        differentTail[^1] = (byte)(differentTail[^1] ^ 0xFF);

        var fileA = Path.Combine(fixture.Path, "a.bin");
        var fileB = Path.Combine(fixture.Path, "b.bin");
        var fileC = Path.Combine(fixture.Path, "c.bin");

        await File.WriteAllBytesAsync(fileA, baseline);
        await File.WriteAllBytesAsync(fileB, sameHeadTailDifferentMiddle);
        await File.WriteAllBytesAsync(fileC, differentTail);

        var pipeline = new StagedHashPipeline(new StreamHasher(new HasherOptions { PartialBlockSizeBytes = partialBlockSize }));

        var result = await pipeline.ComputeAsync(
        [
            new HashCandidate("a", fileA, fileSize),
            new HashCandidate("b", fileB, fileSize),
            new HashCandidate("c", fileC, fileSize)
        ]);

        Assert.Equal(3, result.PartialHashesComputed);
        Assert.Equal(2, result.FullHashesComputed);

        var a = result.Fingerprints.Single(f => f.Id == "a");
        var b = result.Fingerprints.Single(f => f.Id == "b");
        var c = result.Fingerprints.Single(f => f.Id == "c");

        Assert.Equal(a.PartialHash, b.PartialHash);
        Assert.NotEqual(a.FullHash, b.FullHash);

        Assert.NotEqual(a.PartialHash, c.PartialHash);
        Assert.Null(c.FullHash);
    }

    [Fact]
    public async Task StreamHasher_SupportsConfiguredSha256AndStreamOperations()
    {
        IHasher hasher = new StreamHasher(new HasherOptions
        {
            Algorithm = HashAlgorithmKind.Sha256,
            PartialBlockSizeBytes = 8
        });

        await using var fullStream = new MemoryStream(Encoding.UTF8.GetBytes("foldermatch"));
        var fullHash = await hasher.ComputeFullHashAsync(fullStream);

        await using var partialStream = new MemoryStream(Encoding.UTF8.GetBytes("foldermatch"));
        var partialHash = await hasher.ComputePartialHashAsync(partialStream);

        Assert.Equal(HashAlgorithmKind.Sha256, hasher.Algorithm);
        Assert.Equal(64, fullHash.Length);
        Assert.Equal(64, partialHash.Length);
    }

    private static byte[] CreateBytes(int length, int seed)
    {
        var random = new Random(seed);
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"foldermatch-hash-tests-{Guid.NewGuid():N}");
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
                // best-effort cleanup
            }
        }
    }
}
