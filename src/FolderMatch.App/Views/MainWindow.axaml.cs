using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FolderMatch.App.ViewModels;

namespace FolderMatch.App.Views;

public sealed partial class MainWindow : Window
{
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_initialized || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _initialized = true;
        await viewModel.InitializeAsync();
    }

    private async void BrowseLeft_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var path = await PickFolderAsync("Choose left folder");
            if (path is not null)
            {
                viewModel.LeftPath = path;
            }
        }
    }

    private async void BrowseRight_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var path = await PickFolderAsync("Choose right folder");
            if (path is not null)
            {
                viewModel.RightPath = path;
            }
        }
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
