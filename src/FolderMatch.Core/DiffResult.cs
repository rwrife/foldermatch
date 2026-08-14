namespace FolderMatch.Core;

public sealed class DiffResult
{
    public DiffResult(IReadOnlyList<DiffItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items;

        foreach (var item in Items)
        {
            switch (item.ChangeType)
            {
                case DiffChangeType.Identical:
                    IdenticalCount++;
                    break;
                case DiffChangeType.New:
                    NewCount++;
                    break;
                case DiffChangeType.Updated:
                    UpdatedCount++;
                    break;
                case DiffChangeType.Deleted:
                    DeletedCount++;
                    break;
                case DiffChangeType.Conflict:
                    ConflictCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(item.ChangeType), item.ChangeType, "Unsupported diff change type.");
            }
        }
    }

    public IReadOnlyList<DiffItem> Items { get; }

    public int IdenticalCount { get; private set; }

    public int NewCount { get; private set; }

    public int UpdatedCount { get; private set; }

    public int DeletedCount { get; private set; }

    public int ConflictCount { get; private set; }

    public string CountsSummary => $"={IdenticalCount} +{NewCount} ~{UpdatedCount} -{DeletedCount} !{ConflictCount}";
}
