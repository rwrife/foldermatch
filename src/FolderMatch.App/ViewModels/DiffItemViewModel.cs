using Avalonia.Media;
using FolderMatch.Core;

namespace FolderMatch.App.ViewModels;

public sealed class DiffItemViewModel : ObservableObject
{
    private bool _isSelected;

    public DiffItemViewModel(DiffItem item)
    {
        Item = item;
        _isSelected = item.ChangeType != DiffChangeType.Identical;
    }

    public event EventHandler? SelectionChanged;

    public DiffItem Item { get; }

    public string RelativePath => Item.RelativePath;

    public string Status => Item.ChangeType.ToString();

    public string StatusGlyph => Item.ChangeType switch
    {
        DiffChangeType.New => "+",
        DiffChangeType.Updated => "~",
        DiffChangeType.Deleted => "−",
        DiffChangeType.Conflict => "!",
        DiffChangeType.Identical => "=",
        _ => "?",
    };

    public IBrush StatusBrush => Item.ChangeType switch
    {
        DiffChangeType.New => Brushes.SeaGreen,
        DiffChangeType.Updated => Brushes.DarkOrange,
        DiffChangeType.Deleted => Brushes.Crimson,
        DiffChangeType.Conflict => Brushes.MediumPurple,
        _ => Brushes.Gray,
    };

    public string LeftDetails => FormatEntry(Item.LeftInfo);

    public string RightDetails => FormatEntry(Item.RightInfo);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static string FormatEntry(FileEntry? entry)
    {
        if (entry is null)
        {
            return "—";
        }

        if (entry.IsDirectory)
        {
            return "Folder";
        }

        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)entry.Size;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}  {entry.ModifiedUtc.LocalDateTime:g}";
    }
}
