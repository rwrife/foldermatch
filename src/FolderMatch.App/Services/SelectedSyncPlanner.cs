using FolderMatch.Core;

namespace FolderMatch.App.Services;

public static class SelectedSyncPlanner
{
    public static SyncPlan BuildPlan(
        DiffResult diffResult,
        IReadOnlySet<string> selectedPaths,
        SyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(diffResult);
        ArgumentNullException.ThrowIfNull(selectedPaths);
        ArgumentNullException.ThrowIfNull(options);

        var selected = diffResult.Items
            .Where(item => selectedPaths.Contains(item.RelativePath))
            .ToArray();

        return new SyncPlanner().BuildPlan(new DiffResult(selected), options);
    }
}
