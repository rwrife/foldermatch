using FolderMatch.Core;

namespace FolderMatch.App.Models;

public sealed record DiffFilterOptions(
    bool ShowNew = true,
    bool ShowUpdated = true,
    bool ShowDeleted = true,
    bool ShowConflict = true,
    bool ShowIdentical = false)
{
    public bool Includes(DiffChangeType changeType) => changeType switch
    {
        DiffChangeType.New => ShowNew,
        DiffChangeType.Updated => ShowUpdated,
        DiffChangeType.Deleted => ShowDeleted,
        DiffChangeType.Conflict => ShowConflict,
        DiffChangeType.Identical => ShowIdentical,
        _ => false,
    };
}
