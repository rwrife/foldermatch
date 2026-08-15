namespace FolderMatch.Core;

public sealed class SyncPlanner : ISyncPlanner
{
    public SyncPlan BuildPlan(DiffResult diffResult, SyncOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(diffResult);

        options ??= new SyncOptions();

        var actions = new List<SyncAction>(diffResult.Items.Count);

        foreach (var item in diffResult.Items.OrderBy(static i => i.RelativePath, PathNormalization.RelativePathComparer))
        {
            actions.Add(CreateAction(item, options));
        }

        return new SyncPlan(actions);
    }

    private static SyncAction CreateAction(DiffItem item, SyncOptions options)
    {
        return options.Direction switch
        {
            SyncDirection.MirrorLeftToRight => CreateMirrorLeftToRightAction(item, options),
            SyncDirection.MirrorRightToLeft => CreateMirrorRightToLeftAction(item, options),
            SyncDirection.TwoWay => CreateTwoWayAction(item, options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Direction), options.Direction, "Unsupported sync direction.")
        };
    }

    private static SyncAction CreateMirrorLeftToRightAction(DiffItem item, SyncOptions options)
    {
        return item.ChangeType switch
        {
            DiffChangeType.New => new SyncAction(item.RelativePath, SyncActionType.Copy, SyncSide.Left, SyncSide.Right, "Mirror L→R: copy left-only entry to right."),
            DiffChangeType.Updated => new SyncAction(item.RelativePath, SyncActionType.Overwrite, SyncSide.Left, SyncSide.Right, "Mirror L→R: overwrite right with left."),
            DiffChangeType.Deleted => CreateDeleteAction(item.RelativePath, SyncSide.Right, options, "Mirror L→R: delete right-only entry."),
            DiffChangeType.Conflict => CreateConflictResolutionAction(item, options),
            DiffChangeType.Identical => SyncAction.CreateSkip(item.RelativePath, "Already identical."),
            _ => throw new ArgumentOutOfRangeException(nameof(item.ChangeType), item.ChangeType, "Unsupported diff change type.")
        };
    }

    private static SyncAction CreateMirrorRightToLeftAction(DiffItem item, SyncOptions options)
    {
        return item.ChangeType switch
        {
            DiffChangeType.New => CreateDeleteAction(item.RelativePath, SyncSide.Left, options, "Mirror R→L: delete left-only entry."),
            DiffChangeType.Updated => new SyncAction(item.RelativePath, SyncActionType.Overwrite, SyncSide.Right, SyncSide.Left, "Mirror R→L: overwrite left with right."),
            DiffChangeType.Deleted => new SyncAction(item.RelativePath, SyncActionType.Copy, SyncSide.Right, SyncSide.Left, "Mirror R→L: copy right-only entry to left."),
            DiffChangeType.Conflict => CreateConflictResolutionAction(item, options),
            DiffChangeType.Identical => SyncAction.CreateSkip(item.RelativePath, "Already identical."),
            _ => throw new ArgumentOutOfRangeException(nameof(item.ChangeType), item.ChangeType, "Unsupported diff change type.")
        };
    }

    private static SyncAction CreateTwoWayAction(DiffItem item, SyncOptions options)
    {
        return item.ChangeType switch
        {
            DiffChangeType.New => new SyncAction(item.RelativePath, SyncActionType.Copy, SyncSide.Left, SyncSide.Right, "Two-way: copy left-only entry to right."),
            DiffChangeType.Deleted => new SyncAction(item.RelativePath, SyncActionType.Copy, SyncSide.Right, SyncSide.Left, "Two-way: copy right-only entry to left."),
            DiffChangeType.Updated => CreateConflictResolutionAction(item, options),
            DiffChangeType.Conflict => CreateConflictResolutionAction(item, options),
            DiffChangeType.Identical => SyncAction.CreateSkip(item.RelativePath, "Already identical."),
            _ => throw new ArgumentOutOfRangeException(nameof(item.ChangeType), item.ChangeType, "Unsupported diff change type.")
        };
    }

    private static SyncAction CreateConflictResolutionAction(DiffItem item, SyncOptions options)
    {
        var winner = DetermineWinningSide(item.LeftInfo, item.RightInfo, options.ConflictRule);

        if (!winner.HasValue)
        {
            return SyncAction.CreateSkip(item.RelativePath, "Conflict rule is Ask; manual resolution required.");
        }

        var sourceSide = winner.Value;
        var targetSide = sourceSide == SyncSide.Left ? SyncSide.Right : SyncSide.Left;

        return new SyncAction(
            item.RelativePath,
            SyncActionType.Overwrite,
            sourceSide,
            targetSide,
            $"Conflict rule {options.ConflictRule} selected {sourceSide} as source of truth.");
    }

    private static SyncAction CreateDeleteAction(string relativePath, SyncSide targetSide, SyncOptions options, string reason)
    {
        if (options.DeletePolicy == SyncDeletePolicy.Skip)
        {
            return SyncAction.CreateSkip(relativePath, $"{reason} DeletePolicy=Skip.");
        }

        if (options.EnforceSafetyInvariants && options.DeletePolicy == SyncDeletePolicy.Permanent)
        {
            return SyncAction.CreateSkip(relativePath, $"{reason} blocked by never-delete-without-backup safety invariant.");
        }

        return new SyncAction(relativePath, SyncActionType.Delete, null, targetSide, reason);
    }

    private static SyncSide? DetermineWinningSide(FileEntry? left, FileEntry? right, SyncConflictRule rule)
    {
        if (left is null && right is null)
        {
            return null;
        }

        if (left is null)
        {
            return SyncSide.Right;
        }

        if (right is null)
        {
            return SyncSide.Left;
        }

        return rule switch
        {
            SyncConflictRule.NewerWins => left.ModifiedUtc >= right.ModifiedUtc ? SyncSide.Left : SyncSide.Right,
            SyncConflictRule.LargerWins => ChooseBySizeThenDate(left, right),
            SyncConflictRule.LeftWins => SyncSide.Left,
            SyncConflictRule.RightWins => SyncSide.Right,
            SyncConflictRule.Ask => null,
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unsupported conflict rule.")
        };
    }

    private static SyncSide ChooseBySizeThenDate(FileEntry left, FileEntry right)
    {
        if (left.Size == right.Size)
        {
            return left.ModifiedUtc >= right.ModifiedUtc ? SyncSide.Left : SyncSide.Right;
        }

        return left.Size > right.Size ? SyncSide.Left : SyncSide.Right;
    }
}
