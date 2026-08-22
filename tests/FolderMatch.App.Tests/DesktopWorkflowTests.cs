using FolderMatch.App.Models;
using FolderMatch.App.Services;
using FolderMatch.Core;
using Xunit;

namespace FolderMatch.App.Tests;

public sealed class DesktopWorkflowTests
{
    [Fact]
    public void BuildPlan_OnlyIncludesCheckedDiffItems()
    {
        var diff = new DiffResult(
        [
            Item("keep.txt", DiffChangeType.New),
            Item("skip.txt", DiffChangeType.New),
        ]);

        var plan = SelectedSyncPlanner.BuildPlan(
            diff,
            new HashSet<string>(StringComparer.Ordinal) { "keep.txt" },
            new SyncOptions { Direction = SyncDirection.MirrorLeftToRight });

        var action = Assert.Single(plan.Actions);
        Assert.Equal("keep.txt", action.RelativePath);
        Assert.Equal(SyncActionType.Copy, action.ActionType);
    }

    [Theory]
    [InlineData(DiffChangeType.New, true, false, false, false, false, true)]
    [InlineData(DiffChangeType.Updated, true, false, false, false, false, false)]
    [InlineData(DiffChangeType.Conflict, false, false, false, true, false, true)]
    [InlineData(DiffChangeType.Identical, false, false, false, false, true, true)]
    public void DiffFilter_RespectsStatusToggles(
        DiffChangeType changeType,
        bool showNew,
        bool showUpdated,
        bool showDeleted,
        bool showConflict,
        bool showIdentical,
        bool expected)
    {
        var filter = new DiffFilterOptions(showNew, showUpdated, showDeleted, showConflict, showIdentical);

        Assert.Equal(expected, filter.Includes(changeType));
    }

    [Fact]
    public async Task ProfileStore_PersistsAndReplacesNamedProfiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"foldermatch-profile-test-{Guid.NewGuid():N}");

        try
        {
            var store = new CompareProfileStore(root);
            await store.SaveAsync(new CompareProfile("Backup", "/left", "/right", DiffCompareMode.Quick, SyncDirection.MirrorLeftToRight));
            await store.SaveAsync(new CompareProfile("Backup", "/new-left", "/right", DiffCompareMode.Thorough, SyncDirection.TwoWay));

            var profile = Assert.Single(await new CompareProfileStore(root).LoadAsync());
            Assert.Equal("Backup", profile.Name);
            Assert.Equal("/new-left", profile.LeftPath);
            Assert.Equal(DiffCompareMode.Thorough, profile.CompareMode);
            Assert.Equal(SyncDirection.TwoWay, profile.SyncDirection);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static DiffItem Item(string path, DiffChangeType changeType)
    {
        var left = new FileEntry(path, 1, DateTimeOffset.UnixEpoch, FileAttributes.Normal, false, $"/left/{path}");
        return new DiffItem(path, changeType, left, null);
    }
}
