using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderMatch.Core;

public sealed class JsonUndoJournal : IUndoJournal
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public async Task<string> WriteAsync(UndoJournalDocument document, string journalDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(journalDirectory))
        {
            throw new ArgumentException("Journal directory is required.", nameof(journalDirectory));
        }

        Directory.CreateDirectory(journalDirectory);

        var fileName = $"undo-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(journalDirectory, fileName);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);

        return path;
    }

    public async Task<UndoJournalDocument> ReadAsync(string journalPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(journalPath))
        {
            throw new ArgumentException("Journal path is required.", nameof(journalPath));
        }

        await using var stream = File.OpenRead(journalPath);
        var document = await JsonSerializer.DeserializeAsync<UndoJournalDocument>(stream, SerializerOptions, cancellationToken);

        if (document is null)
        {
            throw new InvalidDataException($"Undo journal was empty or invalid JSON: {journalPath}");
        }

        return document;
    }
}
