namespace FolderMatch.Core;

public sealed class UndoJournalDocument
{
    public string Version { get; set; } = "1";

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public string LeftRoot { get; set; } = string.Empty;

    public string RightRoot { get; set; } = string.Empty;

    public List<UndoJournalEntry> Entries { get; set; } = new();
}

public sealed class UndoJournalEntry
{
    public SyncActionType ActionType { get; set; }

    public SyncSide TargetSide { get; set; }

    public string RelativePath { get; set; } = string.Empty;

    public bool TargetExistedBefore { get; set; }

    public string? BackupPath { get; set; }
}
