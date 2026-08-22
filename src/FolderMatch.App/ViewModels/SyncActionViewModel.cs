using FolderMatch.Core;

namespace FolderMatch.App.ViewModels;

public sealed record SyncActionViewModel(
    string Action,
    string RelativePath,
    string Direction,
    string Reason)
{
    public static SyncActionViewModel FromAction(SyncAction action)
    {
        var direction = action.SourceSide.HasValue && action.TargetSide.HasValue
            ? $"{action.SourceSide} → {action.TargetSide}"
            : action.TargetSide?.ToString() ?? "—";

        return new SyncActionViewModel(action.ActionType.ToString(), action.RelativePath, direction, action.Reason);
    }
}
