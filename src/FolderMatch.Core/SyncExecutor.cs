using Microsoft.VisualBasic.FileIO;

namespace FolderMatch.Core;

public sealed class SyncExecutor : ISyncExecutor
{
    private readonly IUndoJournal _undoJournal;

    public SyncExecutor(IUndoJournal? undoJournal = null)
    {
        _undoJournal = undoJournal ?? new JsonUndoJournal();
    }

    public async Task<SyncExecutionResult> ExecuteAsync(
        string leftRoot,
        string rightRoot,
        SyncPlan plan,
        SyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        options ??= new SyncOptions();

        if (string.IsNullOrWhiteSpace(leftRoot))
        {
            throw new ArgumentException("Left root path is required.", nameof(leftRoot));
        }

        if (string.IsNullOrWhiteSpace(rightRoot))
        {
            throw new ArgumentException("Right root path is required.", nameof(rightRoot));
        }

        var normalizedLeftRoot = PathNormalization.NormalizeRootPath(leftRoot);
        var normalizedRightRoot = PathNormalization.NormalizeRootPath(rightRoot);

        var warnings = new List<string>();

        if (options.DryRun)
        {
            return new SyncExecutionResult(
                DryRun: true,
                AppliedCount: 0,
                SkippedCount: plan.Actions.Count,
                JournalPath: null,
                Warnings: warnings);
        }

        var journalDirectory = options.JournalDirectory ??
                               Path.Combine(Path.GetTempPath(), "foldermatch", "undo");

        var backupRoot = Path.Combine(journalDirectory, ".backup", Guid.NewGuid().ToString("N"));
        var managedTrashRoot = options.ManagedTrashDirectory ?? Path.Combine(journalDirectory, ".trash");

        var journal = new UndoJournalDocument
        {
            LeftRoot = normalizedLeftRoot,
            RightRoot = normalizedRightRoot
        };

        var appliedCount = 0;
        var skippedCount = 0;

        for (var actionIndex = 0; actionIndex < plan.Actions.Count; actionIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                skippedCount += plan.Actions.Count - actionIndex;
                warnings.Add("Apply was cancelled. Completed actions were preserved in the undo journal.");
                break;
            }

            var action = plan.Actions[actionIndex];

            if (action.ActionType == SyncActionType.Skip)
            {
                skippedCount++;
                continue;
            }

            var applied = TryApplyAction(
                action,
                normalizedLeftRoot,
                normalizedRightRoot,
                options,
                backupRoot,
                managedTrashRoot,
                out var journalEntry,
                out var warning);

            if (!string.IsNullOrWhiteSpace(warning))
            {
                warnings.Add(warning);
            }

            if (!applied)
            {
                skippedCount++;
                continue;
            }

            appliedCount++;

            if (journalEntry is not null)
            {
                journal.Entries.Add(journalEntry);
            }
        }

        // The journal is the safety boundary for a partially applied plan. Always finish
        // writing it even when cancellation was requested between actions.
        var journalPath = await _undoJournal.WriteAsync(journal, journalDirectory, CancellationToken.None);

        return new SyncExecutionResult(
            DryRun: false,
            AppliedCount: appliedCount,
            SkippedCount: skippedCount,
            JournalPath: journalPath,
            Warnings: warnings);
    }

    public async Task UndoAsync(string journalPath, CancellationToken cancellationToken = default)
    {
        var journal = await _undoJournal.ReadAsync(journalPath, cancellationToken);

        foreach (var entry in journal.Entries.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetRoot = entry.TargetSide == SyncSide.Left ? journal.LeftRoot : journal.RightRoot;
            var targetPath = ResolvePath(targetRoot, entry.RelativePath);

            if (entry.TargetExistedBefore)
            {
                if (string.IsNullOrWhiteSpace(entry.BackupPath))
                {
                    throw new InvalidDataException($"Undo entry for {entry.RelativePath} expected a backup path.");
                }

                RestoreFromBackup(entry.BackupPath, targetPath);
            }
            else
            {
                RemovePathHard(targetPath);
            }
        }
    }

    private static bool TryApplyAction(
        SyncAction action,
        string leftRoot,
        string rightRoot,
        SyncOptions options,
        string backupRoot,
        string managedTrashRoot,
        out UndoJournalEntry? journalEntry,
        out string? warning)
    {
        journalEntry = null;
        warning = null;

        if (action.TargetSide is null)
        {
            warning = $"Skipped {action.RelativePath}: target side is missing for action {action.ActionType}.";
            return false;
        }

        var targetRoot = action.TargetSide == SyncSide.Left ? leftRoot : rightRoot;
        var targetPath = ResolvePath(targetRoot, action.RelativePath);

        var targetExistsBefore = PathExists(targetPath);
        var backupPath = targetExistsBefore ? SnapshotPath(targetPath, backupRoot, action.RelativePath) : null;

        try
        {
            switch (action.ActionType)
            {
                case SyncActionType.Copy:
                case SyncActionType.Overwrite:
                    if (action.SourceSide is null)
                    {
                        warning = $"Skipped {action.RelativePath}: source side is missing for {action.ActionType}.";
                        return false;
                    }

                    var sourceRoot = action.SourceSide == SyncSide.Left ? leftRoot : rightRoot;
                    var sourcePath = ResolvePath(sourceRoot, action.RelativePath);

                    if (!PathExists(sourcePath))
                    {
                        warning = $"Skipped {action.RelativePath}: source path does not exist ({sourcePath}).";
                        return false;
                    }

                    CopyOrOverwrite(sourcePath, targetPath);

                    journalEntry = new UndoJournalEntry
                    {
                        ActionType = action.ActionType,
                        TargetSide = action.TargetSide.Value,
                        RelativePath = action.RelativePath,
                        TargetExistedBefore = targetExistsBefore,
                        BackupPath = backupPath
                    };

                    return true;

                case SyncActionType.Delete:
                    if (!targetExistsBefore)
                    {
                        warning = $"Skipped delete for {action.RelativePath}: target path does not exist.";
                        return false;
                    }

                    if (options.DeletePolicy == SyncDeletePolicy.Skip)
                    {
                        warning = $"Skipped delete for {action.RelativePath}: DeletePolicy=Skip.";
                        return false;
                    }

                    if (options.EnforceSafetyInvariants && options.DeletePolicy == SyncDeletePolicy.Permanent)
                    {
                        warning = $"Skipped delete for {action.RelativePath}: blocked by never-delete-without-backup invariant.";
                        return false;
                    }

                    if (options.DeletePolicy == SyncDeletePolicy.Trash)
                    {
                        if (!TryMoveToTrash(targetPath, action.RelativePath, managedTrashRoot, out var trashError))
                        {
                            warning = $"Failed to move {action.RelativePath} to trash: {trashError}";
                            return false;
                        }
                    }
                    else
                    {
                        RemovePathHard(targetPath);
                    }

                    journalEntry = new UndoJournalEntry
                    {
                        ActionType = action.ActionType,
                        TargetSide = action.TargetSide.Value,
                        RelativePath = action.RelativePath,
                        TargetExistedBefore = true,
                        BackupPath = backupPath
                    };

                    return true;

                default:
                    warning = $"Skipped {action.RelativePath}: unsupported action type {action.ActionType}.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            warning = $"Failed to apply {action.ActionType} for {action.RelativePath}: {ex.Message}";
            return false;
        }
    }

    private static void CopyOrOverwrite(string sourcePath, string targetPath)
    {
        var sourceIsDirectory = Directory.Exists(sourcePath);

        if (sourceIsDirectory)
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            Directory.CreateDirectory(targetPath);
            return;
        }

        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, recursive: true);
        }

        var parent = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var tempPath = targetPath + $".fm-tmp-{Guid.NewGuid():N}";
        File.Copy(sourcePath, tempPath, overwrite: true);
        File.Move(tempPath, targetPath, overwrite: true);
    }

    private static string? SnapshotPath(string path, string backupRoot, string relativePath)
    {
        if (!PathExists(path))
        {
            return null;
        }

        var isDirectory = Directory.Exists(path);
        var backupPath = BuildUniquePathForRelative(backupRoot, relativePath, isDirectory ? ".dir.bak" : ".file.bak");

        var backupParent = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrWhiteSpace(backupParent))
        {
            Directory.CreateDirectory(backupParent);
        }

        if (isDirectory)
        {
            CopyDirectory(path, backupPath);
        }
        else
        {
            File.Copy(path, backupPath, overwrite: true);
        }

        return backupPath;
    }

    private static void RestoreFromBackup(string backupPath, string targetPath)
    {
        if (File.Exists(backupPath))
        {
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }

            var parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(backupPath, targetPath, overwrite: true);
            return;
        }

        if (Directory.Exists(backupPath))
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }

            CopyDirectory(backupPath, targetPath);
            return;
        }

        throw new FileNotFoundException($"Backup path not found for undo: {backupPath}");
    }

    private static bool TryMoveToTrash(string path, string relativePath, string managedTrashRoot, out string? error)
    {
        error = null;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (Directory.Exists(path))
                {
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
                }
                else
                {
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var systemTrashRoot = OperatingSystem.IsMacOS()
                ? Path.Combine(home, ".Trash")
                : Path.Combine(home, ".local", "share", "Trash", "files");

            try
            {
                MovePath(path, BuildUniquePathForRelative(systemTrashRoot, relativePath, ".trashed"));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        try
        {
            MovePath(path, BuildUniquePathForRelative(managedTrashRoot, relativePath, ".trashed"));
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = error is null
                ? ex.Message
                : $"{error}; fallback managed trash failed: {ex.Message}";
            return false;
        }
    }

    private static void MovePath(string sourcePath, string destinationPath)
    {
        var parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    private static string BuildUniquePathForRelative(string root, string relativePath, string suffix)
    {
        var normalizedRelative = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var candidate = Path.Combine(root, normalizedRelative + suffix);
        var parent = Path.GetDirectoryName(candidate);

        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (!PathExists(candidate))
        {
            return candidate;
        }

        var uniqueCandidate = candidate + $".{Guid.NewGuid():N}";
        return uniqueCandidate;
    }

    private static string ResolvePath(string root, string relativePath)
    {
        var normalizedRelative = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        return Path.Combine(root, normalizedRelative);
    }

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    private static void RemovePathHard(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", System.IO.SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            var destination = Path.Combine(targetPath, relative);
            var destinationParent = Path.GetDirectoryName(destination);

            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(file, destination, overwrite: true);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
        }

        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", System.IO.SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relative));
        }
    }
}
