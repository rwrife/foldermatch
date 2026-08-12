namespace FolderMatch.Core;

public sealed record FileEntry(
    string RelativePath,
    long Size,
    DateTimeOffset ModifiedUtc,
    FileAttributes Attributes,
    bool IsDirectory);
