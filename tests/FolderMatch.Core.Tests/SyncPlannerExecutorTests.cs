using System.Security.Cryptography;
using FolderMatch.Core;

namespace FolderMatch.Core.Tests;

public sealed class SyncPlannerExecutorTests
{
    [Fact]
    public void BuildPlan_RespectsDirectionsAndConflictRules()
    {
        var now = new DateTimeOffset(2026, 04, 01, 0, 0, 0, TimeSpan.Zero);

        var leftNew = Entry("new.txt", size: 10, now.AddMinutes(1));
        var leftUpdated = Entry("updated.txt", size: 12, now.AddMinutes(10));
        var rightUpdated = Entry("updated.txt", size: 8, now.AddMinutes(2));
        var rightDeleted = Entry("deleted.txt", size: 20, now.AddMinutes(3));
        var leftConflict = Entry("conflict.txt", size: 4, now.AddMinutes(4));
        var rightConflict = Entry("conflict.txt", size: 9, now.AddMinutes(5));

        var diff = new DiffResult(new[]
        {
            new DiffItem("new.txt", DiffChangeType.New, leftNew, null),
            new DiffItem("updated.txt", DiffChangeType.Updated, leftUpdated, rightUpdated),
            new DiffItem("deleted.txt", DiffChangeType.Deleted, null, rightDeleted),
            new DiffItem("conflict.txt", DiffChangeType.Conflict, leftConflict, rightConflict),
            new DiffItem("same.txt", DiffChangeType.Identical, Entry("same.txt", 1, now), Entry("same.txt", 1, now))
        });

        ISyncPlanner planner = new SyncPlanner();

        var mirrorLr = planner.BuildPlan(diff, new SyncOptions
        {
            Direction = SyncDirection.MirrorLeftToRight,
            ConflictRule = SyncConflictRule.LeftWins,
            DeletePolicy = SyncDeletePolicy.Trash
        });

        Assert.Equal(SyncActionType.Copy, mirrorLr.Actions.Single(a => a.RelativePath == "new.txt").ActionType);
        Assert.Equal((SyncSide.Left, SyncSide.Right), Endpoints(mirrorLr.Actions.Single(a => a.RelativePath == "new.txt")));
        Assert.Equal(SyncActionType.Delete, mirrorLr.Actions.Single(a => a.RelativePath == "deleted.txt").ActionType);
        Assert.Equal((SyncSide.Left, SyncSide.Right), Endpoints(mirrorLr.Actions.Single(a => a.RelativePath == "updated.txt")));

        var mirrorRl = planner.BuildPlan(diff, new SyncOptions
        {
            Direction = SyncDirection.MirrorRightToLeft,
            ConflictRule = SyncConflictRule.RightWins,
            DeletePolicy = SyncDeletePolicy.Trash
        });

        Assert.Equal(SyncActionType.Delete, mirrorRl.Actions.Single(a => a.RelativePath == "new.txt").ActionType);
        Assert.Equal(SyncActionType.Copy, mirrorRl.Actions.Single(a => a.RelativePath == "deleted.txt").ActionType);
        Assert.Equal((SyncSide.Right, SyncSide.Left), Endpoints(mirrorRl.Actions.Single(a => a.RelativePath == "updated.txt")));

        var twoWay = planner.BuildPlan(diff, new SyncOptions
        {
            Direction = SyncDirection.TwoWay,
            ConflictRule = SyncConflictRule.NewerWins,
            DeletePolicy = SyncDeletePolicy.Trash
        });

        Assert.Equal((SyncSide.Left, SyncSide.Right), Endpoints(twoWay.Actions.Single(a => a.RelativePath == "new.txt")));
        Assert.Equal((SyncSide.Right, SyncSide.Left), Endpoints(twoWay.Actions.Single(a => a.RelativePath == "deleted.txt")));

        foreach (var rule in Enum.GetValues<SyncConflictRule>())
        {
            var plan = planner.BuildPlan(diff, new SyncOptions
            {
                Direction = SyncDirection.TwoWay,
                ConflictRule = rule,
                DeletePolicy = SyncDeletePolicy.Trash
            });

            var conflictAction = plan.Actions.Single(a => a.RelativePath == "conflict.txt");
            switch (rule)
            {
                case SyncConflictRule.Ask:
                    Assert.Equal(SyncActionType.Skip, conflictAction.ActionType);
                    break;
                case SyncConflictRule.LeftWins:
                    Assert.Equal((SyncSide.Left, SyncSide.Right), Endpoints(conflictAction));
                    break;
                case SyncConflictRule.RightWins:
                    Assert.Equal((SyncSide.Right, SyncSide.Left), Endpoints(conflictAction));
                    break;
                case SyncConflictRule.NewerWins:
                    Assert.Equal((SyncSide.Right, SyncSide.Left), Endpoints(conflictAction));
                    break;
                case SyncConflictRule.LargerWins:
                    Assert.Equal((SyncSide.Right, SyncSide.Left), Endpoints(conflictAction));
                    break;
            }
        }
    }

    [Fact]
    public void BuildPlan_BlocksPermanentDelete_WhenSafetyInvariantEnabled()
    {
        var diff = new DiffResult(new[]
        {
            new DiffItem("orphan-right.txt", DiffChangeType.Deleted, null, Entry("orphan-right.txt", 1, DateTimeOffset.UtcNow))
        });

        ISyncPlanner planner = new SyncPlanner();
        var plan = planner.BuildPlan(diff, new SyncOptions
        {
            Direction = SyncDirection.MirrorLeftToRight,
            DeletePolicy = SyncDeletePolicy.Permanent,
            EnforceSafetyInvariants = true
        });

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SyncActionType.Skip, action.ActionType);
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_TouchesNothing()
    {
        using var left = new TempDir("left-dry");
        using var right = new TempDir("right-dry");

        WriteFile(Path.Combine(left.Path, "new.txt"), "left-new");
        WriteFile(Path.Combine(left.Path, "shared.txt"), "left-shared");

        WriteFile(Path.Combine(right.Path, "shared.txt"), "right-shared");
        WriteFile(Path.Combine(right.Path, "delete-me.txt"), "to-delete");

        var before = SnapshotBoth(left.Path, right.Path);

        var plan = new SyncPlan(new[]
        {
            new SyncAction("new.txt", SyncActionType.Copy, SyncSide.Left, SyncSide.Right, "test"),
            new SyncAction("shared.txt", SyncActionType.Overwrite, SyncSide.Left, SyncSide.Right, "test"),
            new SyncAction("delete-me.txt", SyncActionType.Delete, null, SyncSide.Right, "test")
        });

        ISyncExecutor executor = new SyncExecutor();
        var result = await executor.ExecuteAsync(left.Path, right.Path, plan, new SyncOptions
        {
            DryRun = true,
            DeletePolicy = SyncDeletePolicy.Trash
        });

        var after = SnapshotBoth(left.Path, right.Path);

        Assert.True(result.DryRun);
        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(plan.Actions.Count, result.SkippedCount);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeApply_WritesAnUndoJournalWithoutTouchingFiles()
    {
        using var left = new TempDir("left-cancel");
        using var right = new TempDir("right-cancel");
        using var journalDir = new TempDir("journal-cancel");
        WriteFile(Path.Combine(left.Path, "new.txt"), "left-only");

        var plan = new SyncPlan(new[]
        {
            new SyncAction("new.txt", SyncActionType.Copy, SyncSide.Left, SyncSide.Right, "test")
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ISyncExecutor executor = new SyncExecutor();
        var result = await executor.ExecuteAsync(left.Path, right.Path, plan, new SyncOptions
        {
            DryRun = false,
            JournalDirectory = journalDir.Path
        }, cancellation.Token);

        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(result.JournalPath));
        Assert.False(File.Exists(Path.Combine(right.Path, "new.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_ApplyThenUndo_RestoresOriginalTrees()
    {
        using var left = new TempDir("left-undo");
        using var right = new TempDir("right-undo");
        using var journalDir = new TempDir("journal");

        WriteFile(Path.Combine(left.Path, "new.txt"), "left-only");
        WriteFile(Path.Combine(left.Path, "shared.txt"), "left-version");
        WriteFile(Path.Combine(right.Path, "shared.txt"), "right-version");
        WriteFile(Path.Combine(right.Path, "delete-me.txt"), "right-only");

        var before = SnapshotBoth(left.Path, right.Path);

        var plan = new SyncPlan(new[]
        {
            new SyncAction("new.txt", SyncActionType.Copy, SyncSide.Left, SyncSide.Right, "test"),
            new SyncAction("shared.txt", SyncActionType.Overwrite, SyncSide.Left, SyncSide.Right, "test"),
            new SyncAction("delete-me.txt", SyncActionType.Delete, null, SyncSide.Right, "test")
        });

        ISyncExecutor executor = new SyncExecutor();
        var applyResult = await executor.ExecuteAsync(left.Path, right.Path, plan, new SyncOptions
        {
            DryRun = false,
            DeletePolicy = SyncDeletePolicy.Trash,
            JournalDirectory = journalDir.Path,
            ManagedTrashDirectory = Path.Combine(journalDir.Path, "managed-trash")
        });

        Assert.False(applyResult.DryRun);
        Assert.Equal(3, applyResult.AppliedCount);
        Assert.NotNull(applyResult.JournalPath);

        await executor.UndoAsync(applyResult.JournalPath!);

        var afterUndo = SnapshotBoth(left.Path, right.Path);
        Assert.Equal(before, afterUndo);
    }

    private static (SyncSide? Source, SyncSide? Target) Endpoints(SyncAction action) => (action.SourceSide, action.TargetSide);

    private static FileEntry Entry(string relativePath, long size, DateTimeOffset modifiedUtc) =>
        new(relativePath, size, modifiedUtc, FileAttributes.Normal, IsDirectory: false, AbsolutePath: null);

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string SnapshotBoth(string leftRoot, string rightRoot)
    {
        return $"L:{SnapshotTree(leftRoot)}|R:{SnapshotTree(rightRoot)}";
    }

    private static string SnapshotTree(string root)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var bytes = File.ReadAllBytes(path);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                return $"{relative}:{hash}";
            });

        return string.Join(";", files);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"foldermatch-{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
