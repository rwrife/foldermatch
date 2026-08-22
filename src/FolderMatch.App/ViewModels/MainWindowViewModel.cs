using System.Collections.ObjectModel;
using FolderMatch.App.Models;
using FolderMatch.App.Services;
using FolderMatch.Core;

namespace FolderMatch.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IFolderScanner _scanner;
    private readonly IDiffEngine _diffEngine;
    private readonly ISyncExecutor _syncExecutor;
    private readonly CompareProfileStore _profileStore;
    private readonly List<DiffItemViewModel> _allItems = [];

    private CancellationTokenSource? _operationCancellation;
    private DiffResult? _lastDiff;
    private SyncPlan? _previewPlan;
    private string? _lastJournalPath;
    private string _leftPath = string.Empty;
    private string _rightPath = string.Empty;
    private string _profileName = string.Empty;
    private CompareProfile? _selectedProfile;
    private DiffCompareMode _compareMode = DiffCompareMode.Quick;
    private SyncDirection _syncDirection = SyncDirection.MirrorLeftToRight;
    private bool _showNew = true;
    private bool _showUpdated = true;
    private bool _showDeleted = true;
    private bool _showConflict = true;
    private bool _showIdentical;
    private bool _isBusy;
    private bool _hasPlan;
    private bool _canApply;
    private bool _canUndo;
    private string _statusText = "Choose two folders to begin.";
    private string _progressText = string.Empty;
    private string _diffSummary = "No comparison yet";
    private string _planSummary = string.Empty;

    public MainWindowViewModel(
        IFolderScanner? scanner = null,
        IDiffEngine? diffEngine = null,
        ISyncExecutor? syncExecutor = null,
        CompareProfileStore? profileStore = null)
    {
        _scanner = scanner ?? new FolderScanner();
        _diffEngine = diffEngine ?? new DiffEngine();
        _syncExecutor = syncExecutor ?? new SyncExecutor();
        _profileStore = profileStore ?? new CompareProfileStore();

        CompareCommand = new AsyncCommand(CompareAsync, () => !IsBusy);
        PreviewCommand = new DelegateCommand(BuildPreview, () => !IsBusy && _lastDiff is not null);
        ApplyCommand = new AsyncCommand(ApplyAsync, () => !IsBusy && CanApply);
        UndoCommand = new AsyncCommand(UndoAsync, () => !IsBusy && CanUndo);
        CancelCommand = new DelegateCommand(Cancel, () => IsBusy);
        SelectAllCommand = new DelegateCommand(() => SetAllSelections(true), () => !IsBusy && _allItems.Count > 0);
        SelectNoneCommand = new DelegateCommand(() => SetAllSelections(false), () => !IsBusy && _allItems.Count > 0);
        SaveProfileCommand = new AsyncCommand(SaveProfileAsync, () => !IsBusy);
        LoadProfileCommand = new DelegateCommand(LoadSelectedProfile, () => SelectedProfile is not null && !IsBusy);
        DeleteProfileCommand = new AsyncCommand(DeleteProfileAsync, () => SelectedProfile is not null && !IsBusy);
    }

    public IReadOnlyList<DiffCompareMode> CompareModes { get; } = Enum.GetValues<DiffCompareMode>();

    public IReadOnlyList<SyncDirection> SyncDirections { get; } = Enum.GetValues<SyncDirection>();

    public ObservableCollection<DiffItemViewModel> Items { get; } = [];

    public ObservableCollection<SyncActionViewModel> PlanActions { get; } = [];

    public ObservableCollection<CompareProfile> Profiles { get; } = [];

    public AsyncCommand CompareCommand { get; }

    public DelegateCommand PreviewCommand { get; }

    public AsyncCommand ApplyCommand { get; }

    public AsyncCommand UndoCommand { get; }

    public DelegateCommand CancelCommand { get; }

    public DelegateCommand SelectAllCommand { get; }

    public DelegateCommand SelectNoneCommand { get; }

    public AsyncCommand SaveProfileCommand { get; }

    public DelegateCommand LoadProfileCommand { get; }

    public AsyncCommand DeleteProfileCommand { get; }

    public string LeftPath
    {
        get => _leftPath;
        set
        {
            if (SetProperty(ref _leftPath, value))
            {
                InvalidateComparison();
            }
        }
    }

    public string RightPath
    {
        get => _rightPath;
        set
        {
            if (SetProperty(ref _rightPath, value))
            {
                InvalidateComparison();
            }
        }
    }

    public string ProfileName
    {
        get => _profileName;
        set => SetProperty(ref _profileName, value);
    }

    public CompareProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                LoadProfileCommand.RaiseCanExecuteChanged();
                DeleteProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public DiffCompareMode CompareMode
    {
        get => _compareMode;
        set
        {
            if (SetProperty(ref _compareMode, value))
            {
                InvalidateComparison();
            }
        }
    }

    public SyncDirection SyncDirection
    {
        get => _syncDirection;
        set
        {
            if (SetProperty(ref _syncDirection, value))
            {
                InvalidatePreview();
            }
        }
    }

    public bool ShowNew
    {
        get => _showNew;
        set { if (SetProperty(ref _showNew, value)) ApplyFilter(); }
    }

    public bool ShowUpdated
    {
        get => _showUpdated;
        set { if (SetProperty(ref _showUpdated, value)) ApplyFilter(); }
    }

    public bool ShowDeleted
    {
        get => _showDeleted;
        set { if (SetProperty(ref _showDeleted, value)) ApplyFilter(); }
    }

    public bool ShowConflict
    {
        get => _showConflict;
        set { if (SetProperty(ref _showConflict, value)) ApplyFilter(); }
    }

    public bool ShowIdentical
    {
        get => _showIdentical;
        set { if (SetProperty(ref _showIdentical, value)) ApplyFilter(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool HasPlan
    {
        get => _hasPlan;
        private set => SetProperty(ref _hasPlan, value);
    }

    public bool CanApply
    {
        get => _canApply;
        private set
        {
            if (SetProperty(ref _canApply, value))
            {
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanUndo
    {
        get => _canUndo;
        private set
        {
            if (SetProperty(ref _canUndo, value))
            {
                UndoCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string DiffSummary
    {
        get => _diffSummary;
        private set => SetProperty(ref _diffSummary, value);
    }

    public string PlanSummary
    {
        get => _planSummary;
        private set => SetProperty(ref _planSummary, value);
    }

    public int SelectedCount => _allItems.Count(item => item.IsSelected);

    public int VisibleCount => Items.Count;

    public async Task InitializeAsync()
    {
        try
        {
            await ReloadProfilesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load profiles: {ex.Message}";
        }
    }

    private async Task CompareAsync()
    {
        if (!Directory.Exists(LeftPath) || !Directory.Exists(RightPath))
        {
            StatusText = "Choose two existing folders before comparing.";
            return;
        }

        BeginOperation("Scanning folders…");
        var token = _operationCancellation!.Token;
        var progress = new Progress<ScanProgress>(report =>
        {
            ProgressText = $"Scanned {report.EntriesScanned:N0} entries · {report.RelativePath}";
        });

        try
        {
            var leftTask = Task.Run(() => _scanner.ScanAsync(LeftPath, progress, token), token);
            var rightTask = Task.Run(() => _scanner.ScanAsync(RightPath, progress, token), token);
            await Task.WhenAll(leftTask, rightTask);

            ProgressText = "Comparing entries…";
            var options = new DiffOptions { Mode = CompareMode };
            var result = await Task.Run(
                () => _diffEngine.ComputeAsync(leftTask.Result, rightTask.Result, options, token),
                token);

            ReplaceDiff(result);
            var warningCount = leftTask.Result.Warnings.Count + rightTask.Result.Warnings.Count;
            StatusText = warningCount == 0
                ? $"Comparison complete: {result.Items.Count:N0} entries."
                : $"Comparison complete with {warningCount:N0} scan warning(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operation cancelled. No files were changed.";
        }
        catch (Exception ex)
        {
            StatusText = $"Compare failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private void ReplaceDiff(DiffResult result)
    {
        foreach (var oldItem in _allItems)
        {
            oldItem.SelectionChanged -= OnSelectionChanged;
        }

        _allItems.Clear();
        foreach (var item in result.Items)
        {
            var row = new DiffItemViewModel(item);
            row.SelectionChanged += OnSelectionChanged;
            _allItems.Add(row);
        }

        _lastDiff = result;
        DiffSummary = result.CountsSummary;
        InvalidatePreview();
        ApplyFilter();
        RaiseCommandStates();
    }

    private void ApplyFilter()
    {
        var filter = new DiffFilterOptions(ShowNew, ShowUpdated, ShowDeleted, ShowConflict, ShowIdentical);
        Items.Clear();
        foreach (var item in _allItems.Where(item => filter.Includes(item.Item.ChangeType)))
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(VisibleCount));
    }

    private void BuildPreview()
    {
        if (_lastDiff is null)
        {
            return;
        }

        var selectedPaths = _allItems
            .Where(item => item.IsSelected)
            .Select(item => item.RelativePath)
            .ToHashSet(StringComparer.Ordinal);
        var options = CreateSyncOptions(dryRun: true);
        _previewPlan = SelectedSyncPlanner.BuildPlan(_lastDiff, selectedPaths, options);

        PlanActions.Clear();
        foreach (var action in _previewPlan.Actions)
        {
            PlanActions.Add(SyncActionViewModel.FromAction(action));
        }

        HasPlan = true;
        CanApply = _previewPlan.Actions.Any(action => action.ActionType != SyncActionType.Skip);
        PlanSummary = $"Dry run · {_previewPlan.CopyCount} copy · {_previewPlan.OverwriteCount} overwrite · " +
                      $"{_previewPlan.DeleteCount} trash · {_previewPlan.SkipCount} skip";
        StatusText = CanApply
            ? "Dry-run plan ready. Review every action before applying."
            : "Dry-run plan contains no file changes.";
    }

    private async Task ApplyAsync()
    {
        if (_previewPlan is null || !CanApply)
        {
            StatusText = "Build and review a dry-run plan before applying changes.";
            return;
        }

        BeginOperation("Applying reviewed plan…");
        try
        {
            var result = await _syncExecutor.ExecuteAsync(
                LeftPath,
                RightPath,
                _previewPlan,
                CreateSyncOptions(dryRun: false),
                _operationCancellation!.Token);

            _lastJournalPath = result.JournalPath;
            CanUndo = !string.IsNullOrWhiteSpace(_lastJournalPath) && File.Exists(_lastJournalPath);
            StatusText = result.Warnings.Count == 0
                ? $"Applied {result.AppliedCount:N0} action(s); skipped {result.SkippedCount:N0}. Undo is available."
                : $"Applied {result.AppliedCount:N0} action(s) with {result.Warnings.Count:N0} warning(s).";
            InvalidatePreview();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Apply cancelled. Completed actions remain undoable from the written journal.";
        }
        catch (Exception ex)
        {
            StatusText = $"Apply failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task UndoAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastJournalPath) || !File.Exists(_lastJournalPath))
        {
            StatusText = "No undo journal is available for this session.";
            CanUndo = false;
            return;
        }

        BeginOperation("Restoring from undo journal…");
        try
        {
            await _syncExecutor.UndoAsync(_lastJournalPath, _operationCancellation!.Token);
            CanUndo = false;
            StatusText = "Undo complete. Compare again to refresh the diff.";
            InvalidateComparison();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Undo cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Undo failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private SyncOptions CreateSyncOptions(bool dryRun)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FolderMatch");

        return new SyncOptions
        {
            Direction = SyncDirection,
            ConflictRule = SyncConflictRule.NewerWins,
            DeletePolicy = SyncDeletePolicy.Trash,
            DryRun = dryRun,
            EnforceSafetyInvariants = true,
            JournalDirectory = Path.Combine(appData, "undo"),
            ManagedTrashDirectory = Path.Combine(appData, "trash"),
        };
    }

    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(ProfileName) || string.IsNullOrWhiteSpace(LeftPath) || string.IsNullOrWhiteSpace(RightPath))
        {
            StatusText = "Enter a profile name and choose both folders before saving.";
            return;
        }

        try
        {
            await _profileStore.SaveAsync(new CompareProfile(ProfileName, LeftPath, RightPath, CompareMode, SyncDirection));
            await ReloadProfilesAsync(ProfileName.Trim());
            StatusText = $"Saved profile “{ProfileName.Trim()}”.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not save profile: {ex.Message}";
        }
    }

    private void LoadSelectedProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        ProfileName = SelectedProfile.Name;
        LeftPath = SelectedProfile.LeftPath;
        RightPath = SelectedProfile.RightPath;
        CompareMode = SelectedProfile.CompareMode;
        SyncDirection = SelectedProfile.SyncDirection;
        StatusText = $"Loaded profile “{SelectedProfile.Name}”.";
    }

    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var name = SelectedProfile.Name;
        try
        {
            await _profileStore.DeleteAsync(name);
            await ReloadProfilesAsync();
            StatusText = $"Deleted profile “{name}”.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not delete profile: {ex.Message}";
        }
    }

    private async Task ReloadProfilesAsync(string? selectName = null)
    {
        var loaded = await _profileStore.LoadAsync();
        Profiles.Clear();
        foreach (var profile in loaded)
        {
            Profiles.Add(profile);
        }

        SelectedProfile = selectName is null
            ? null
            : Profiles.FirstOrDefault(profile => string.Equals(profile.Name, selectName, StringComparison.OrdinalIgnoreCase));
    }

    private void SetAllSelections(bool isSelected)
    {
        foreach (var item in _allItems)
        {
            item.IsSelected = isSelected;
        }

        OnSelectionChanged(this, EventArgs.Empty);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedCount));
        InvalidatePreview();
    }

    private void InvalidatePreview()
    {
        _previewPlan = null;
        PlanActions.Clear();
        PlanSummary = string.Empty;
        HasPlan = false;
        CanApply = false;
        PreviewCommand.RaiseCanExecuteChanged();
    }

    private void InvalidateComparison()
    {
        if (_lastDiff is null)
        {
            return;
        }

        _lastDiff = null;
        foreach (var item in _allItems)
        {
            item.SelectionChanged -= OnSelectionChanged;
        }
        _allItems.Clear();
        Items.Clear();
        DiffSummary = "Paths or compare mode changed — compare again";
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(VisibleCount));
        InvalidatePreview();
        RaiseCommandStates();
    }

    private void BeginOperation(string progressText)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        ProgressText = progressText;
        IsBusy = true;
    }

    private void EndOperation()
    {
        IsBusy = false;
        ProgressText = string.Empty;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    private void Cancel() => _operationCancellation?.Cancel();

    private void RaiseCommandStates()
    {
        CompareCommand.RaiseCanExecuteChanged();
        PreviewCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        SelectNoneCommand.RaiseCanExecuteChanged();
        SaveProfileCommand.RaiseCanExecuteChanged();
        LoadProfileCommand.RaiseCanExecuteChanged();
        DeleteProfileCommand.RaiseCanExecuteChanged();
    }
}
