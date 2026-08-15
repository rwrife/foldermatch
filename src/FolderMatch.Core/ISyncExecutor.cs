namespace FolderMatch.Core;

public interface ISyncExecutor
{
    Task<SyncExecutionResult> ExecuteAsync(
        string leftRoot,
        string rightRoot,
        SyncPlan plan,
        SyncOptions? options = null,
        CancellationToken cancellationToken = default);

    Task UndoAsync(string journalPath, CancellationToken cancellationToken = default);
}
