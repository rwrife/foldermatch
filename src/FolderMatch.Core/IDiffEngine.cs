namespace FolderMatch.Core;

public interface IDiffEngine
{
    Task<DiffResult> ComputeAsync(
        FolderScanResult left,
        FolderScanResult right,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);
}
