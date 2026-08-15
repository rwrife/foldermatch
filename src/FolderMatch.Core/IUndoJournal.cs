namespace FolderMatch.Core;

public interface IUndoJournal
{
    Task<string> WriteAsync(UndoJournalDocument document, string journalDirectory, CancellationToken cancellationToken = default);

    Task<UndoJournalDocument> ReadAsync(string journalPath, CancellationToken cancellationToken = default);
}
