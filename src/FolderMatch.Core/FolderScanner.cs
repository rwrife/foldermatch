namespace FolderMatch.Core;

public sealed class FolderScanner : IFolderScanner
{
    public Task<FolderScanResult> ScanAsync(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        var normalizedRoot = PathNormalization.NormalizeRootPath(rootPath);

        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException($"Directory not found: {normalizedRoot}");
        }

        var entriesByPath = new Dictionary<string, FileEntry>(PathNormalization.RelativePathComparer);
        var warnings = new List<ScanWarning>();

        var pending = new Queue<(string AbsolutePath, string RelativePath)>();
        pending.Enqueue((normalizedRoot, string.Empty));

        var filesScanned = 0;
        var directoriesScanned = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = pending.Dequeue();
            directoriesScanned++;

            IEnumerable<string> children;

            try
            {
                children = Directory.EnumerateFileSystemEntries(current.AbsolutePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                warnings.Add(new ScanWarning(ScanWarningCode.AccessDenied, current.AbsolutePath, ex.Message));
                continue;
            }
            catch (IOException ex)
            {
                warnings.Add(new ScanWarning(ScanWarningCode.IoError, current.AbsolutePath, ex.Message));
                continue;
            }

            foreach (var childAbsolutePath in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string childName;
                FileAttributes attributes;

                try
                {
                    childName = Path.GetFileName(childAbsolutePath);
                    attributes = File.GetAttributes(childAbsolutePath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    warnings.Add(new ScanWarning(ScanWarningCode.AccessDenied, childAbsolutePath, ex.Message));
                    continue;
                }
                catch (IOException ex)
                {
                    warnings.Add(new ScanWarning(ScanWarningCode.IoError, childAbsolutePath, ex.Message));
                    continue;
                }

                if (string.IsNullOrEmpty(childName))
                {
                    continue;
                }

                var relativePath = PathNormalization.NormalizeRelativePath(
                    string.IsNullOrEmpty(current.RelativePath)
                        ? childName
                        : Path.Combine(current.RelativePath, childName));

                var isDirectory = attributes.HasFlag(FileAttributes.Directory);

                try
                {
                    if (isDirectory)
                    {
                        var modifiedUtc = Directory.GetLastWriteTimeUtc(childAbsolutePath);
                        entriesByPath[relativePath] = new FileEntry(
                            relativePath,
                            0,
                            modifiedUtc,
                            attributes,
                            IsDirectory: true);

                        if (!IsSymlinkOrReparsePoint(attributes, childAbsolutePath))
                        {
                            pending.Enqueue((childAbsolutePath, relativePath));
                        }
                    }
                    else
                    {
                        var info = new FileInfo(childAbsolutePath);
                        entriesByPath[relativePath] = new FileEntry(
                            relativePath,
                            info.Length,
                            info.LastWriteTimeUtc,
                            attributes,
                            IsDirectory: false);
                        filesScanned++;
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    warnings.Add(new ScanWarning(ScanWarningCode.AccessDenied, childAbsolutePath, ex.Message));
                    continue;
                }
                catch (IOException ex)
                {
                    warnings.Add(new ScanWarning(ScanWarningCode.IoError, childAbsolutePath, ex.Message));
                    continue;
                }

                progress?.Report(new ScanProgress(entriesByPath.Count, filesScanned, directoriesScanned, relativePath));
            }
        }

        var orderedEntries = entriesByPath.Values
            .OrderBy(static e => e.RelativePath, PathNormalization.RelativePathComparer)
            .ToArray();

        return Task.FromResult(new FolderScanResult(
            orderedEntries,
            new Dictionary<string, FileEntry>(entriesByPath, entriesByPath.Comparer),
            warnings,
            filesScanned,
            directoriesScanned));
    }

    private static bool IsSymlinkOrReparsePoint(FileAttributes attributes, string path)
    {
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        return Directory.ResolveLinkTarget(path, returnFinalTarget: false) is not null;
    }
}
