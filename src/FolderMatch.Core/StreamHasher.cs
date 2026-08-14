using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace FolderMatch.Core;

public sealed class StreamHasher : IHasher
{
    private readonly int _partialBlockSizeBytes;

    public StreamHasher(HasherOptions? options = null)
    {
        options ??= new HasherOptions();

        if (options.PartialBlockSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Partial block size must be greater than zero.");
        }

        Algorithm = options.Algorithm;
        _partialBlockSizeBytes = options.PartialBlockSizeBytes;
    }

    public HashAlgorithmKind Algorithm { get; }

    public async ValueTask<string> ComputePartialHashAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanSeek)
        {
            return await ComputeFullHashAsync(source, cancellationToken);
        }

        source.Position = 0;
        var length = source.Length;

        if (length <= (_partialBlockSizeBytes * 2L))
        {
            var allBytes = new byte[(int)length];
            await ReadExactlyAsync(source, allBytes, cancellationToken);
            return ComputePartialPayloadHash(length, allBytes, ReadOnlyMemory<byte>.Empty);
        }

        var head = new byte[_partialBlockSizeBytes];
        await ReadExactlyAsync(source, head, cancellationToken);

        source.Position = length - _partialBlockSizeBytes;
        var tail = new byte[_partialBlockSizeBytes];
        await ReadExactlyAsync(source, tail, cancellationToken);

        return ComputePartialPayloadHash(length, head, tail);
    }

    public async ValueTask<string> ComputeFullHashAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);

        return ComputeHashHex(memory.ToArray());
    }

    private string ComputePartialPayloadHash(long length, ReadOnlyMemory<byte> head, ReadOnlyMemory<byte> tail)
    {
        var payload = new byte[sizeof(long) + head.Length + tail.Length];
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(0, sizeof(long)), length);

        head.Span.CopyTo(payload.AsSpan(sizeof(long)));
        tail.Span.CopyTo(payload.AsSpan(sizeof(long) + head.Length));

        return ComputeHashHex(payload);
    }

    private string ComputeHashHex(ReadOnlySpan<byte> bytes)
    {
        return Algorithm switch
        {
            HashAlgorithmKind.XxHash64 => Convert.ToHexString(XxHash64.Hash(bytes)).ToLowerInvariant(),
            HashAlgorithmKind.Sha256 => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            _ => throw new InvalidOperationException($"Unsupported hash algorithm: {Algorithm}")
        };
    }

    private static async Task ReadExactlyAsync(Stream source, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var bytesRead = 0;

        while (bytesRead < destination.Length)
        {
            var read = await source.ReadAsync(destination[bytesRead..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while computing hash.");
            }

            bytesRead += read;
        }
    }
}
