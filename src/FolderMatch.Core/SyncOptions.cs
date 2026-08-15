namespace FolderMatch.Core;

public sealed record SyncOptions
{
    public SyncDirection Direction { get; init; } = SyncDirection.MirrorLeftToRight;

    public SyncConflictRule ConflictRule { get; init; } = SyncConflictRule.NewerWins;

    public SyncDeletePolicy DeletePolicy { get; init; } = SyncDeletePolicy.Trash;

    public bool DryRun { get; init; } = true;

    public bool EnforceSafetyInvariants { get; init; } = true;

    public string? JournalDirectory { get; init; }

    public string? ManagedTrashDirectory { get; init; }
}

public enum SyncDirection
{
    MirrorLeftToRight,
    MirrorRightToLeft,
    TwoWay
}

public enum SyncConflictRule
{
    NewerWins,
    LargerWins,
    LeftWins,
    RightWins,
    Ask
}

public enum SyncDeletePolicy
{
    Trash,
    Permanent,
    Skip
}
