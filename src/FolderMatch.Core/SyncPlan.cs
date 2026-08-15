namespace FolderMatch.Core;

public sealed class SyncPlan
{
    public SyncPlan(IReadOnlyList<SyncAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        Actions = actions;

        foreach (var action in actions)
        {
            switch (action.ActionType)
            {
                case SyncActionType.Copy:
                    CopyCount++;
                    break;
                case SyncActionType.Overwrite:
                    OverwriteCount++;
                    break;
                case SyncActionType.Delete:
                    DeleteCount++;
                    break;
                case SyncActionType.Skip:
                    SkipCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action.ActionType), action.ActionType, "Unsupported sync action type.");
            }
        }
    }

    public IReadOnlyList<SyncAction> Actions { get; }

    public int CopyCount { get; }

    public int OverwriteCount { get; }

    public int DeleteCount { get; }

    public int SkipCount { get; }
}
