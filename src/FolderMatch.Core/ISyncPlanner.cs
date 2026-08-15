namespace FolderMatch.Core;

public interface ISyncPlanner
{
    SyncPlan BuildPlan(DiffResult diffResult, SyncOptions? options = null);
}
