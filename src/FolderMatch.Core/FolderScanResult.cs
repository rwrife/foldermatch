namespace FolderMatch.Core;

public sealed class FolderScanResult
{
    public FolderScanResult(
        IReadOnlyList<FileEntry> entries,
        IReadOnlyDictionary<string, FileEntry> entriesByPath,
        IReadOnlyList<ScanWarning> warnings,
        int filesScanned,
        int directoriesScanned)
    {
        Entries = entries;
        EntriesByPath = entriesByPath;
        Warnings = warnings;
        FilesScanned = filesScanned;
        DirectoriesScanned = directoriesScanned;
    }

    public IReadOnlyList<FileEntry> Entries { get; }

    public IReadOnlyDictionary<string, FileEntry> EntriesByPath { get; }

    public IReadOnlyList<ScanWarning> Warnings { get; }

    public int FilesScanned { get; }

    public int DirectoriesScanned { get; }
}
