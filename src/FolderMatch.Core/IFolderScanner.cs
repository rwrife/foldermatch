namespace FolderMatch.Core;

public interface IFolderScanner
{
    Task<FolderScanResult> ScanAsync(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
