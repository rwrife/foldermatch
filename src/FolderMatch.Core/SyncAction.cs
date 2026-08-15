namespace FolderMatch.Core;

public sealed record SyncAction(
    string RelativePath,
    SyncActionType ActionType,
    SyncSide? SourceSide,
    SyncSide? TargetSide,
    string Reason)
{
    public static SyncAction CreateSkip(string relativePath, string reason) =>
        new(relativePath, SyncActionType.Skip, null, null, reason);
}

public enum SyncActionType
{
    Copy,
    Overwrite,
    Delete,
    Skip
}

public enum SyncSide
{
    Left,
    Right
}
