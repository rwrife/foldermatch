namespace FolderMatch.Core;

public sealed record DiffItem(
    string RelativePath,
    DiffChangeType ChangeType,
    FileEntry? LeftInfo,
    FileEntry? RightInfo);
