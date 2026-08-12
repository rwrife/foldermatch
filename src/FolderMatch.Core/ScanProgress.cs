namespace FolderMatch.Core;

public sealed record ScanProgress(
    int EntriesScanned,
    int FilesScanned,
    int DirectoriesScanned,
    string RelativePath);
