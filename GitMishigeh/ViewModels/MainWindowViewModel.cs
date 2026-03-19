using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitMishigeh.Models;
using GitMishigeh.Services;

namespace GitMishigeh.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IGitService _gitService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IRecentRepositoryStore _recentRepositoryStore;
    private readonly DispatcherTimer _autoRefreshTimer;
    private string? _selectedFolderPath;
    private bool _hasValidRepository;
    private CancellationTokenSource? _diffCancellationTokenSource;
    private bool _isAutoRefreshing;
    private string _repositoryFingerprint = string.Empty;
    private int _aheadCount;
    private int _behindCount;
    private string? _selectedWorkingTreePath;
    private string? _selectedCommitFilePath;
    private bool _suppressCommitLoad;
    private bool _suppressRecentRepositoryOpen;
    private bool _suppressBranchSelectionSync;
    private DateTimeOffset? _lastFetchedAt;

    public MainWindowViewModel() : this(new GitService(), new FolderPickerService(), new RecentRepositoryStore())
    {
    }

    public MainWindowViewModel(
        IGitService gitService,
        IFolderPickerService folderPickerService,
        IRecentRepositoryStore recentRepositoryStore)
    {
        _gitService = gitService;
        _folderPickerService = folderPickerService;
        _recentRepositoryStore = recentRepositoryStore;

        ChangedFiles = new ObservableCollection<GitChangedFile>();
        CommitFiles = new ObservableCollection<GitChangedFile>();
        Branches = new ObservableCollection<GitBranchItem>();
        Remotes = new ObservableCollection<GitRemoteItem>();
        RecentCommits = new ObservableCollection<GitCommitItem>();
        RecentRepositories = new ObservableCollection<RecentRepository>();
        DiffSections = new ObservableCollection<GitDiffSection>();

        OpenRepoCommand = new AsyncRelayCommand(OpenRepoAsync, CanOpenRepo);
        OpenRecentRepositoryCommand = new AsyncRelayCommand<RecentRepository?>(OpenRecentRepositoryAsync, CanOpenRecentRepository);
        ShowWorkingTreeCommand = new RelayCommand(ShowWorkingTree);
        ShowHistoryCommand = new RelayCommand(ShowHistory, CanShowHistory);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        FetchCommand = new AsyncRelayCommand(FetchAsync, CanSyncWithRemote);
        PullCommand = new AsyncRelayCommand(PullAsync, CanSyncWithRemote);
        PushCommand = new AsyncRelayCommand(PushAsync, CanSyncWithRemote);
        OpenBranchManagerCommand = new RelayCommand(OpenBranchManager, CanOpenBranchManager);
        CloseBranchManagerCommand = new RelayCommand(CloseBranchManager);
        CheckoutSelectedBranchCommand = new AsyncRelayCommand(CheckoutSelectedBranchAsync, CanCheckoutSelectedBranch);
        CheckoutBranchCommand = new AsyncRelayCommand<GitBranchItem?>(CheckoutBranchAsync, CanCheckoutBranch);
        CreateBranchCommand = new AsyncRelayCommand(CreateBranchAsync, CanCreateBranch);
        DeleteBranchCommand = new AsyncRelayCommand<GitBranchItem?>(DeleteBranchAsync, CanDeleteBranch);
        StageAllCommand = new AsyncRelayCommand(StageAllAsync, CanStageAll);
        UnstageAllCommand = new AsyncRelayCommand(UnstageAllAsync, CanUnstageAll);
        ToggleStageFileCommand = new AsyncRelayCommand<GitChangedFile?>(ToggleStageFileAsync, CanToggleStageFile);
        DiscardFileCommand = new AsyncRelayCommand<GitChangedFile?>(DiscardFileAsync, CanDiscardFile);
        CommitCommand = new AsyncRelayCommand(CommitAsync, CanCommit);

        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _autoRefreshTimer.Tick += AutoRefreshTimerOnTick;
        _autoRefreshTimer.Start();

        OutputMessage = "Open a local Git repository to begin.";
        RepositoryPath = "No repository selected";
        CurrentBranch = "No branch";
        StatusSummary = "Repository details will appear here.";
        NavigationPaneWidth = 220;
        FilePaneWidth = 540;
        SelectedDiffHeader = "Diff Preview";
        SelectedDiff = "Select a changed file or commit to inspect its diff.";
        ApplyDiffContent(SelectedDiff);

        ChangedFiles.CollectionChanged += (_, _) => NotifyCommandStateChanged();
        Branches.CollectionChanged += (_, _) => NotifyCommandStateChanged();
        CommitFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasVisibleFiles));
        _ = LoadRecentRepositoriesAsync();
    }

    public ObservableCollection<GitChangedFile> ChangedFiles { get; }

    public ObservableCollection<GitChangedFile> CommitFiles { get; }

    public ObservableCollection<GitBranchItem> Branches { get; }

    public ObservableCollection<GitRemoteItem> Remotes { get; }

    public ObservableCollection<GitCommitItem> RecentCommits { get; }

    public ObservableCollection<RecentRepository> RecentRepositories { get; }

    public ObservableCollection<GitDiffSection> DiffSections { get; }

    public ReadOnlyObservableCollection<GitChangedFile>? _visibleFilesReadOnly;

    public IEnumerable<GitChangedFile> VisibleFiles => IsShowingCommitHistory ? CommitFiles : ChangedFiles;

    public IAsyncRelayCommand OpenRepoCommand { get; }

    public IAsyncRelayCommand<RecentRepository?> OpenRecentRepositoryCommand { get; }

    public IRelayCommand ShowWorkingTreeCommand { get; }

    public IRelayCommand ShowHistoryCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand FetchCommand { get; }

    public IAsyncRelayCommand PullCommand { get; }

    public IAsyncRelayCommand PushCommand { get; }

    public IRelayCommand OpenBranchManagerCommand { get; }

    public IRelayCommand CloseBranchManagerCommand { get; }

    public IAsyncRelayCommand CheckoutSelectedBranchCommand { get; }

    public IAsyncRelayCommand<GitBranchItem?> CheckoutBranchCommand { get; }

    public IAsyncRelayCommand CreateBranchCommand { get; }

    public IAsyncRelayCommand<GitBranchItem?> DeleteBranchCommand { get; }

    public IAsyncRelayCommand StageAllCommand { get; }

    public IAsyncRelayCommand UnstageAllCommand { get; }

    public IAsyncRelayCommand<GitChangedFile?> ToggleStageFileCommand { get; }

    public IAsyncRelayCommand<GitChangedFile?> DiscardFileCommand { get; }

    public IAsyncRelayCommand CommitCommand { get; }

    [ObservableProperty]
    private string repositoryPath = string.Empty;

    [ObservableProperty]
    private string currentBranch = string.Empty;

    [ObservableProperty]
    private string statusSummary = string.Empty;

    [ObservableProperty]
    private string commitMessage = string.Empty;

    [ObservableProperty]
    private string outputMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private GitCommitItem? selectedCommit;

    [ObservableProperty]
    private RecentRepository? selectedRecentRepository;

    [ObservableProperty]
    private GitChangedFile? selectedFileEntry;

    [ObservableProperty]
    private GitBranchItem? selectedBranch;

    [ObservableProperty]
    private string newBranchName = string.Empty;

    [ObservableProperty]
    private bool isBranchManagerOpen;

    [ObservableProperty]
    private string selectedDiffHeader = string.Empty;

    [ObservableProperty]
    private string selectedDiff = string.Empty;

    [ObservableProperty]
    private double navigationPaneWidth;

    [ObservableProperty]
    private double filePaneWidth;

    public bool HasChangedFiles => ChangedFiles.Count > 0;

    public bool HasBranches => Branches.Count > 0;

    public bool HasRemotes => Remotes.Count > 0;

    public bool HasRecentCommits => RecentCommits.Count > 0;

    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    public bool HasDiffSections => DiffSections.Count > 0;

    public bool IsShowingCommitHistory => SelectedCommit is not null;

    public bool IsWorkingCopyMode => !IsShowingCommitHistory;

    public bool HasVisibleFiles => IsShowingCommitHistory ? CommitFiles.Count > 0 : ChangedFiles.Count > 0;

    public string PushButtonText => _aheadCount > 0 ? $"Push ({_aheadCount})" : "Push";

    public string FetchStatusText =>
        _lastFetchedAt is null
            ? "Never fetched"
            : BuildRelativeTimeText(_lastFetchedAt.Value);

    public string FilePaneHeaderText => IsShowingCommitHistory ? "Files Changed" : "Working Copy";

    public string FilePaneStatusHeader => IsShowingCommitHistory ? "Change" : "Status";

    public string FilePaneSubtitle =>
        IsShowingCommitHistory && SelectedCommit is not null
            ? $"{SelectedCommit.ShortHash} {SelectedCommit.Message}"
            : StatusSummary;

    private bool CanOpenRepo() => !IsBusy;

    private bool CanOpenRecentRepository(RecentRepository? repository) => !IsBusy && repository is not null;

    private bool CanRefresh() => !IsBusy && _hasValidRepository;

    private bool CanShowHistory() => RecentCommits.Count > 0;

    private bool CanSyncWithRemote() => !IsBusy && _hasValidRepository;

    private bool CanOpenBranchManager() => _hasValidRepository;

    private bool CanCheckoutSelectedBranch() =>
        !IsBusy && _hasValidRepository && SelectedBranch is not null && !SelectedBranch.IsCurrent;

    private bool CanCheckoutBranch(GitBranchItem? branch) =>
        !IsBusy && _hasValidRepository && branch is not null && !branch.IsCurrent;

    private bool CanCreateBranch() =>
        !IsBusy &&
        _hasValidRepository &&
        !string.IsNullOrWhiteSpace(NewBranchName) &&
        Branches.All(branch => !string.Equals(branch.Name, NewBranchName.Trim(), StringComparison.Ordinal));

    private bool CanDeleteBranch(GitBranchItem? branch) =>
        !IsBusy && _hasValidRepository && branch is not null && !branch.IsCurrent;

    private bool CanStageAll() => !IsBusy && _hasValidRepository && ChangedFiles.Count > 0;

    private bool CanUnstageAll() => !IsBusy && _hasValidRepository && ChangedFiles.Count > 0;

    private bool CanToggleStageFile(GitChangedFile? changedFile) =>
        !IsBusy && _hasValidRepository && changedFile is not null && changedFile.CanToggleStage;

    private bool CanDiscardFile(GitChangedFile? changedFile) =>
        !IsBusy && _hasValidRepository && changedFile is not null && changedFile.CanDiscardChanges;

    private bool CanCommit() =>
        !IsBusy &&
        _hasValidRepository &&
        ChangedFiles.Count > 0 &&
        !string.IsNullOrWhiteSpace(CommitMessage);

    private async Task OpenRepoAsync()
    {
        var selectedFolder = await _folderPickerService.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            OutputMessage = "Repository selection cancelled.";
            return;
        }

        await OpenRepositoryAsync(selectedFolder);
    }

    private async Task OpenRecentRepositoryAsync(RecentRepository? repository)
    {
        if (repository is null)
        {
            return;
        }

        await OpenRepositoryAsync(repository.Path);
    }

    private void ShowWorkingTree()
    {
        if (SelectedCommit is null)
        {
            return;
        }

        SelectedCommit = null;
    }

    private void ShowHistory()
    {
        if (RecentCommits.Count == 0)
        {
            return;
        }

        SelectedCommit = RecentCommits[0];
    }

    private Task RefreshAsync() => RunBusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            OutputMessage = "Choose a Git repository first.";
            return;
        }

        try
        {
            var repositoryState = await _gitService.GetRepositoryStateAsync(_selectedFolderPath);
            ApplyRepositoryState(repositoryState);
            OutputMessage = $"Repository refreshed at {DateTime.Now:t}.";
        }
        catch (GitServiceException exception)
        {
            ClearRepositoryStateForInvalidRepo(exception.Message);
        }
        finally
        {
            NotifyCommandStateChanged();
        }
    });

    private Task StageAllAsync() => ExecuteGitActionAsync(
        () => _gitService.StageAllAsync(_selectedFolderPath!),
        clearCommitMessage: false);

    private Task FetchAsync() => ExecuteGitActionAsync(
        () => _gitService.FetchAsync(_selectedFolderPath!),
        clearCommitMessage: false,
        onSuccess: () =>
        {
            _lastFetchedAt = DateTimeOffset.Now;
            OnPropertyChanged(nameof(FetchStatusText));
        });

    private Task PullAsync() => ExecuteGitActionAsync(
        () => _gitService.PullAsync(_selectedFolderPath!),
        clearCommitMessage: false);

    private Task PushAsync() => ExecuteGitActionAsync(
        () => _gitService.PushAsync(_selectedFolderPath!),
        clearCommitMessage: false);

    private void OpenBranchManager()
    {
        if (!_hasValidRepository)
        {
            return;
        }

        IsBranchManagerOpen = true;
    }

    private void CloseBranchManager()
    {
        IsBranchManagerOpen = false;
    }

    private Task CheckoutSelectedBranchAsync()
    {
        if (SelectedBranch is null || SelectedBranch.IsCurrent)
        {
            return Task.CompletedTask;
        }

        return ExecuteGitActionAsync(
            () => _gitService.CheckoutBranchAsync(_selectedFolderPath!, SelectedBranch.Name),
            clearCommitMessage: false,
            onSuccess: CloseBranchManager);
    }

    private Task CheckoutBranchAsync(GitBranchItem? branch)
    {
        if (branch is null || branch.IsCurrent)
        {
            return Task.CompletedTask;
        }

        return ExecuteGitActionAsync(
            () => _gitService.CheckoutBranchAsync(_selectedFolderPath!, branch.Name),
            clearCommitMessage: false,
            onSuccess: CloseBranchManager);
    }

    private Task CreateBranchAsync()
    {
        var branchName = NewBranchName.Trim();
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return Task.CompletedTask;
        }

        return ExecuteGitActionAsync(
            () => _gitService.CreateBranchAsync(_selectedFolderPath!, branchName),
            clearCommitMessage: false,
            onSuccess: () =>
            {
                NewBranchName = string.Empty;
                CloseBranchManager();
            });
    }

    private Task DeleteBranchAsync(GitBranchItem? branch)
    {
        if (branch is null || branch.IsCurrent)
        {
            return Task.CompletedTask;
        }

        return ExecuteGitActionAsync(
            () => _gitService.DeleteBranchAsync(_selectedFolderPath!, branch.Name),
            clearCommitMessage: false);
    }

    private Task UnstageAllAsync() => ExecuteGitActionAsync(
        () => _gitService.UnstageAllAsync(_selectedFolderPath!),
        clearCommitMessage: false);

    private Task CommitAsync() => ExecuteGitActionAsync(
        () => _gitService.CommitAsync(_selectedFolderPath!, CommitMessage),
        clearCommitMessage: true);

    private Task ToggleStageFileAsync(GitChangedFile? changedFile)
    {
        if (changedFile is null || !changedFile.CanToggleStage)
        {
            return Task.CompletedTask;
        }

        return ExecuteGitActionAsync(
            () => changedFile.IsStaged
                ? _gitService.UnstageFileAsync(_selectedFolderPath!, changedFile)
                : _gitService.StageFileAsync(_selectedFolderPath!, changedFile),
            clearCommitMessage: false);
    }

    private Task DiscardFileAsync(GitChangedFile? changedFile)
    {
        if (changedFile is null || !changedFile.CanDiscardChanges)
        {
            return Task.CompletedTask;
        }

        return ExecuteGitActionAsync(
            () => _gitService.DiscardFileAsync(_selectedFolderPath!, changedFile),
            clearCommitMessage: false);
    }

    private async Task ExecuteGitActionAsync(Func<Task<string>> action, bool clearCommitMessage, Action? onSuccess = null)
    {
        await RunBusyAsync(async () =>
        {
            try
            {
                OutputMessage = await action();
                onSuccess?.Invoke();
                if (clearCommitMessage)
                {
                    CommitMessage = string.Empty;
                }

                await RefreshRepositoryStateAsync();
            }
            catch (GitServiceException exception)
            {
                OutputMessage = exception.Message;
            }
        });
    }

    private async Task RefreshRepositoryStateAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            return;
        }

        var repositoryState = await _gitService.GetRepositoryStateAsync(_selectedFolderPath);
        ApplyRepositoryState(repositoryState);
    }

    private void ApplyRepositoryState(GitRepositoryState repositoryState)
    {
        _hasValidRepository = true;
        CurrentBranch = repositoryState.CurrentBranch;
        StatusSummary = repositoryState.StatusSummary;
        _aheadCount = repositoryState.AheadCount;
        _behindCount = repositoryState.BehindCount;
        SyncCollection(Branches, repositoryState.Branches);
        SyncSelectedBranch();
        SyncCollection(Remotes, repositoryState.Remotes);
        SyncCollection(ChangedFiles, repositoryState.ChangedFiles);
        SyncCollection(RecentCommits, repositoryState.RecentCommits);

        UpdateSelectedCommitAfterRefresh();
        if (!IsShowingCommitHistory)
        {
            UpdateSelectedFileForCurrentMode();
        }

        OnPropertyChanged(nameof(HasChangedFiles));
        OnPropertyChanged(nameof(HasBranches));
        OnPropertyChanged(nameof(HasRemotes));
        OnPropertyChanged(nameof(HasRecentCommits));
        OnPropertyChanged(nameof(PushButtonText));
        OnPropertyChanged(nameof(FetchStatusText));
        OnPropertyChanged(nameof(FilePaneSubtitle));
        OnPropertyChanged(nameof(VisibleFiles));
        OnPropertyChanged(nameof(HasVisibleFiles));
        ShowHistoryCommand.NotifyCanExecuteChanged();
        _repositoryFingerprint = BuildRepositoryFingerprint(repositoryState);
    }

    private void UpdateSelectedCommitAfterRefresh()
    {
        if (SelectedCommit is null)
        {
            return;
        }

        foreach (var commit in RecentCommits)
        {
            if (!string.Equals(commit.Hash, SelectedCommit.Hash, StringComparison.Ordinal))
            {
                continue;
            }

            if (!ReferenceEquals(commit, SelectedCommit))
            {
                _suppressCommitLoad = true;
                SelectedCommit = commit;
                _suppressCommitLoad = false;
            }

            return;
        }

        SelectedCommit = null;
    }

    private void ClearRepositoryStateForInvalidRepo(string message)
    {
        _hasValidRepository = false;
        IsBranchManagerOpen = false;
        CurrentBranch = "Unavailable";
        StatusSummary = "The selected folder is not a Git repository.";
        _aheadCount = 0;
        _behindCount = 0;
        SyncCollection(Branches, Array.Empty<GitBranchItem>());
        SelectedBranch = null;
        SyncCollection(Remotes, Array.Empty<GitRemoteItem>());
        SyncCollection(ChangedFiles, Array.Empty<GitChangedFile>());
        SyncCollection(CommitFiles, Array.Empty<GitChangedFile>());
        SyncCollection(RecentCommits, Array.Empty<GitCommitItem>());
        SelectedCommit = null;
        SelectedFileEntry = null;
        ClearDiffPreview("Diff Preview", "Select a valid Git repository to inspect file diffs.");
        OnPropertyChanged(nameof(HasChangedFiles));
        OnPropertyChanged(nameof(HasBranches));
        OnPropertyChanged(nameof(HasRemotes));
        OnPropertyChanged(nameof(HasRecentCommits));
        OnPropertyChanged(nameof(PushButtonText));
        OnPropertyChanged(nameof(FetchStatusText));
        OnPropertyChanged(nameof(FilePaneSubtitle));
        OnPropertyChanged(nameof(VisibleFiles));
        OnPropertyChanged(nameof(HasVisibleFiles));
        ShowHistoryCommand.NotifyCanExecuteChanged();
        OutputMessage = message;
        _repositoryFingerprint = string.Empty;
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await operation();
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task OpenRepositoryAsync(string repositoryPath)
    {
        _selectedFolderPath = repositoryPath;
        RepositoryPath = repositoryPath;
        _repositoryFingerprint = string.Empty;
        SelectedCommit = null;
        await RefreshAsync();

        if (_hasValidRepository)
        {
            await SaveRecentRepositoryAsync(repositoryPath);
        }
    }

    private async Task LoadRecentRepositoriesAsync()
    {
        var repositories = await _recentRepositoryStore.LoadAsync();
        SyncCollection(RecentRepositories, repositories);
        UpdateSelectedRecentRepository();
        OnPropertyChanged(nameof(HasRecentRepositories));
    }

    private async Task SaveRecentRepositoryAsync(string repositoryPath)
    {
        var repositories = await _recentRepositoryStore.AddOrUpdateAsync(repositoryPath);
        SyncCollection(RecentRepositories, repositories);
        UpdateSelectedRecentRepository();
        OnPropertyChanged(nameof(HasRecentRepositories));
    }

    private void UpdateSelectedRecentRepository()
    {
        _suppressRecentRepositoryOpen = true;
        try
        {
            SelectedRecentRepository = null;
            if (string.IsNullOrWhiteSpace(_selectedFolderPath))
            {
                return;
            }

            foreach (var repository in RecentRepositories)
            {
                if (string.Equals(repository.Path, _selectedFolderPath, StringComparison.Ordinal))
                {
                    SelectedRecentRepository = repository;
                    return;
                }
            }
        }
        finally
        {
            _suppressRecentRepositoryOpen = false;
        }
    }

    private void NotifyCommandStateChanged()
    {
        OpenRepoCommand.NotifyCanExecuteChanged();
        OpenRecentRepositoryCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        FetchCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        OpenBranchManagerCommand.NotifyCanExecuteChanged();
        CheckoutSelectedBranchCommand.NotifyCanExecuteChanged();
        CheckoutBranchCommand.NotifyCanExecuteChanged();
        CreateBranchCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
        ShowHistoryCommand.NotifyCanExecuteChanged();
        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        ToggleStageFileCommand.NotifyCanExecuteChanged();
        DiscardFileCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();
    }

    private void SyncCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();

        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    partial void OnCommitMessageChanged(string value)
    {
        CommitCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewBranchNameChanged(string value)
    {
        CreateBranchCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifyCommandStateChanged();
    }

    partial void OnSelectedRecentRepositoryChanged(RecentRepository? value)
    {
        if (_suppressRecentRepositoryOpen || value is null || string.Equals(value.Path, _selectedFolderPath, StringComparison.Ordinal))
        {
            return;
        }

        _ = OpenRecentRepositoryAsync(value);
    }

    partial void OnSelectedCommitChanged(GitCommitItem? value)
    {
        OnPropertyChanged(nameof(IsWorkingCopyMode));
        OnPropertyChanged(nameof(IsShowingCommitHistory));
        OnPropertyChanged(nameof(VisibleFiles));
        OnPropertyChanged(nameof(HasVisibleFiles));
        OnPropertyChanged(nameof(FilePaneHeaderText));
        OnPropertyChanged(nameof(FilePaneStatusHeader));
        OnPropertyChanged(nameof(FilePaneSubtitle));

        if (_suppressCommitLoad)
        {
            return;
        }

        _ = LoadCommitFilesAsync(value);
    }

    partial void OnSelectedFileEntryChanged(GitChangedFile? value)
    {
        if (IsShowingCommitHistory)
        {
            _selectedCommitFilePath = value?.Path;
        }
        else
        {
            _selectedWorkingTreePath = value?.Path;
        }

        _ = LoadDiffAsync(value);
    }

    partial void OnSelectedBranchChanged(GitBranchItem? value)
    {
        if (_suppressBranchSelectionSync)
        {
            return;
        }

        CheckoutSelectedBranchCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadCommitFilesAsync(GitCommitItem? commit)
    {
        SyncCollection(CommitFiles, Array.Empty<GitChangedFile>());
        OnPropertyChanged(nameof(VisibleFiles));
        OnPropertyChanged(nameof(HasVisibleFiles));

        if (commit is null)
        {
            UpdateSelectedFileForCurrentMode();
            return;
        }

        if (!_hasValidRepository || string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            return;
        }

        SelectedDiffHeader = commit.ShortHash;
        SelectedDiff = "Loading commit files...";
        ApplyDiffContent(SelectedDiff);

        try
        {
            var files = await _gitService.GetCommitFilesAsync(_selectedFolderPath, commit);
            SyncCollection(CommitFiles, files);
            OnPropertyChanged(nameof(VisibleFiles));
            OnPropertyChanged(nameof(HasVisibleFiles));
            UpdateSelectedFileForCurrentMode();
        }
        catch (GitServiceException exception)
        {
            SelectedDiffHeader = commit.ShortHash;
            SelectedDiff = exception.Message;
            ApplyDiffContent(SelectedDiff);
        }
    }

    private void UpdateSelectedFileForCurrentMode()
    {
        var files = IsShowingCommitHistory ? CommitFiles : ChangedFiles;
        var rememberedPath = IsShowingCommitHistory ? _selectedCommitFilePath : _selectedWorkingTreePath;

        if (files.Count == 0)
        {
            SelectedFileEntry = null;
            if (IsShowingCommitHistory && SelectedCommit is not null)
            {
                ClearDiffPreview(SelectedCommit.ShortHash, "This commit does not include any file changes.");
            }
            else
            {
                ClearDiffPreview("Diff Preview", "Working tree is clean.");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(rememberedPath))
        {
            foreach (var file in files)
            {
                if (string.Equals(file.Path, rememberedPath, StringComparison.Ordinal))
                {
                    SelectedFileEntry = file;
                    return;
                }
            }
        }

        SelectedFileEntry = files[0];
    }

    private async void AutoRefreshTimerOnTick(object? sender, EventArgs e)
    {
        if (_isAutoRefreshing || IsBusy || !_hasValidRepository || string.IsNullOrWhiteSpace(_selectedFolderPath))
        {
            return;
        }

        _isAutoRefreshing = true;

        try
        {
            var repositoryState = await _gitService.GetRepositoryStateAsync(_selectedFolderPath);
            var nextFingerprint = BuildRepositoryFingerprint(repositoryState);
            if (string.Equals(nextFingerprint, _repositoryFingerprint, StringComparison.Ordinal))
            {
                return;
            }

            ApplyRepositoryState(repositoryState);
            OutputMessage = $"Repository auto-refreshed at {DateTime.Now:t}.";
        }
        catch (GitServiceException exception)
        {
            ClearRepositoryStateForInvalidRepo(exception.Message);
        }
        finally
        {
            _isAutoRefreshing = false;
        }
    }

    private async Task LoadDiffAsync(GitChangedFile? changedFile)
    {
        _diffCancellationTokenSource?.Cancel();
        _diffCancellationTokenSource?.Dispose();
        _diffCancellationTokenSource = null;

        if (!_hasValidRepository || string.IsNullOrWhiteSpace(_selectedFolderPath) || changedFile is null)
        {
            ClearDiffPreview("Diff Preview", "Select a changed file or commit to inspect its diff.");
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        _diffCancellationTokenSource = cancellationTokenSource;

        try
        {
            if (IsShowingCommitHistory && SelectedCommit is not null)
            {
                SelectedDiffHeader = $"{SelectedCommit.ShortHash} • {changedFile.DiffPath}";
                SelectedDiff = "Loading commit diff...";
                ApplyDiffContent(SelectedDiff);
                SelectedDiff = await _gitService.GetCommitFileDiffAsync(
                    _selectedFolderPath,
                    SelectedCommit,
                    changedFile,
                    cancellationTokenSource.Token);
            }
            else
            {
                SelectedDiffHeader = changedFile.DiffPath;
                SelectedDiff = "Loading diff...";
                ApplyDiffContent(SelectedDiff);
                SelectedDiff = await _gitService.GetDiffAsync(_selectedFolderPath, changedFile, cancellationTokenSource.Token);
            }

            ApplyDiffContent(SelectedDiff);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitServiceException exception)
        {
            SelectedDiff = exception.Message;
            ApplyDiffContent(SelectedDiff);
        }
    }

    private void ClearDiffPreview(string header, string content)
    {
        SelectedDiffHeader = header;
        SelectedDiff = content;
        ApplyDiffContent(content);
    }

    private void ApplyDiffContent(string diffText)
    {
        SyncCollection(DiffSections, BuildDiffSections(diffText));
        OnPropertyChanged(nameof(HasDiffSections));
    }

    private static IReadOnlyList<GitDiffSection> BuildDiffSections(string diffText)
    {
        if (string.IsNullOrWhiteSpace(diffText))
        {
            return
            [
                CreateInfoSection("Diff Preview", "Nothing to display yet.")
            ];
        }

        var lines = diffText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var metadataLines = new List<string>();
        var sections = new List<GitDiffSection>();
        List<string>? currentHunkLines = null;
        string? currentHunkHeader = null;
        var chunkIndex = 1;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                FinalizeCurrentHunk(sections, currentHunkHeader, currentHunkLines, chunkIndex);
                if (currentHunkHeader is not null)
                {
                    chunkIndex++;
                }

                currentHunkHeader = line;
                currentHunkLines = new List<string>();
                continue;
            }

            if (currentHunkLines is not null)
            {
                currentHunkLines.Add(line);
                continue;
            }

            metadataLines.Add(line);
        }

        FinalizeCurrentHunk(sections, currentHunkHeader, currentHunkLines, chunkIndex);

        var hasPatchContent = currentHunkHeader is not null || sections.Count > 0;
        var trimmedMetadata = TrimEmptyEdges(metadataLines);
        if (hasPatchContent)
        {
            var headerLines = trimmedMetadata.FindAll(IsDiffMetadataLine);
            if (headerLines.Count > 0)
            {
                sections.Insert(0, CreateMetadataSection(headerLines));
            }

            return sections;
        }

        if (trimmedMetadata.Count == 0)
        {
            return
            [
                CreateInfoSection("Diff Preview", "No textual diff is available for this file.")
            ];
        }

        return
        [
            CreateInfoSection("Diff Preview", string.Join(Environment.NewLine, trimmedMetadata))
        ];
    }

    private static void FinalizeCurrentHunk(
        ICollection<GitDiffSection> sections,
        string? currentHunkHeader,
        IReadOnlyList<string>? currentHunkLines,
        int chunkIndex)
    {
        if (currentHunkHeader is null || currentHunkLines is null)
        {
            return;
        }

        sections.Add(CreateChunkSection(currentHunkHeader, currentHunkLines, chunkIndex));
    }

    private static GitDiffSection CreateMetadataSection(IReadOnlyList<string> lines)
    {
        var leftLineNumber = 0;
        var rightLineNumber = 0;
        var diffLines = new List<GitDiffLine>(lines.Count);
        foreach (var line in lines)
        {
            diffLines.Add(CreateDiffLine(line, ref leftLineNumber, ref rightLineNumber));
        }

        return new GitDiffSection("File Header", "Paths and patch metadata", "FILE", "#334155", diffLines, false);
    }

    private static GitDiffSection CreateInfoSection(string title, string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var diffLines = new List<GitDiffLine>(lines.Length);
        foreach (var line in lines)
        {
            diffLines.Add(new GitDiffLine(line, "#D8E1EE", "Transparent", string.Empty, string.Empty));
        }

        return new GitDiffSection(title, string.Empty, "INFO", "#1D4ED8", diffLines, false);
    }

    private static GitDiffSection CreateChunkSection(string header, IReadOnlyList<string> lines, int chunkIndex)
    {
        ParseHunkHeader(header, out var leftLineNumber, out var rightLineNumber);
        var diffLines = new List<GitDiffLine>(lines.Count);

        foreach (var line in lines)
        {
            diffLines.Add(CreateDiffLine(line, ref leftLineNumber, ref rightLineNumber));
        }

        return new GitDiffSection(
            $"Hunk {chunkIndex}",
            header,
            "HUNK",
            "#2563EB",
            diffLines,
            true);
    }

    private static List<string> TrimEmptyEdges(List<string> lines)
    {
        var start = 0;
        var end = lines.Count - 1;

        while (start <= end && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        return start > end ? [] : lines.GetRange(start, end - start + 1);
    }

    private static bool IsDiffMetadataLine(string line) =>
        line.StartsWith("diff --git", StringComparison.Ordinal) ||
        line.StartsWith("index ", StringComparison.Ordinal) ||
        line.StartsWith("---", StringComparison.Ordinal) ||
        line.StartsWith("+++", StringComparison.Ordinal) ||
        line.StartsWith("new file mode", StringComparison.Ordinal) ||
        line.StartsWith("deleted file mode", StringComparison.Ordinal) ||
        line.StartsWith("similarity index", StringComparison.Ordinal) ||
        line.StartsWith("rename from ", StringComparison.Ordinal) ||
        line.StartsWith("rename to ", StringComparison.Ordinal);

    private static GitDiffLine CreateDiffLine(string line, ref int leftLineNumber, ref int rightLineNumber)
    {
        if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
        {
            var diffLine = new GitDiffLine(
                line,
                "#DCFCE7",
                "#10261C",
                string.Empty,
                rightLineNumber.ToString(CultureInfo.InvariantCulture),
                true);

            rightLineNumber++;
            return diffLine;
        }

        if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
        {
            var diffLine = new GitDiffLine(
                line,
                "#FECACA",
                "#34131C",
                leftLineNumber.ToString(CultureInfo.InvariantCulture),
                string.Empty,
                true);

            leftLineNumber++;
            return diffLine;
        }

        if (line.StartsWith("diff --git", StringComparison.Ordinal) ||
            line.StartsWith("index ", StringComparison.Ordinal) ||
            line.StartsWith("---", StringComparison.Ordinal) ||
            line.StartsWith("+++", StringComparison.Ordinal) ||
            line.StartsWith("new file mode", StringComparison.Ordinal))
        {
            return new GitDiffLine(line, "#A5B4CC", "#111827", string.Empty, string.Empty, true);
        }

        if (line.StartsWith("\\", StringComparison.Ordinal))
        {
            return new GitDiffLine(line, "#94A3B8", "#111827", string.Empty, string.Empty);
        }

        var contextLine = new GitDiffLine(
            line,
            "#D8DEE9",
            "Transparent",
            leftLineNumber > 0 ? leftLineNumber.ToString(CultureInfo.InvariantCulture) : string.Empty,
            rightLineNumber > 0 ? rightLineNumber.ToString(CultureInfo.InvariantCulture) : string.Empty);

        if (!string.IsNullOrEmpty(line))
        {
            leftLineNumber++;
            rightLineNumber++;
        }

        return contextLine;
    }

    private static void ParseHunkHeader(string line, out int leftStart, out int rightStart)
    {
        leftStart = 0;
        rightStart = 0;

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return;
        }

        leftStart = ParseRangeStart(parts[1]);
        rightStart = ParseRangeStart(parts[2]);
    }

    private static int ParseRangeStart(string part)
    {
        var trimmed = part.TrimStart('-', '+');
        var commaIndex = trimmed.IndexOf(',');
        var number = commaIndex >= 0 ? trimmed[..commaIndex] : trimmed;

        return int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string BuildRepositoryFingerprint(GitRepositoryState repositoryState)
    {
        var builder = new StringBuilder();
        builder.Append(repositoryState.CurrentBranch).Append('|');
        builder.Append(repositoryState.StatusSummary).Append('|');
        builder.Append(repositoryState.AheadCount).Append('|');
        builder.Append(repositoryState.BehindCount).Append('|');

        foreach (var branch in repositoryState.Branches)
        {
            builder
                .Append(branch.Name)
                .Append(':')
                .Append(branch.IsCurrent)
                .Append('|');
        }

        foreach (var changedFile in repositoryState.ChangedFiles)
        {
            builder
                .Append(changedFile.IndexStatus)
                .Append(changedFile.WorkingTreeStatus)
                .Append(':')
                .Append(changedFile.Path)
                .Append('|');
        }

        foreach (var commit in repositoryState.RecentCommits)
        {
            builder
                .Append(commit.Hash)
                .Append(':')
                .Append(commit.AuthorName)
                .Append(':')
                .Append(commit.Message)
                .Append(':')
                .Append(commit.Refs)
                .Append('|');
        }

        return builder.ToString();
    }

    private static string BuildRelativeTimeText(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.Now - timestamp;
        if (elapsed.TotalMinutes < 1)
        {
            return "Fetched just now";
        }

        if (elapsed.TotalHours < 1)
        {
            var minutes = Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes));
            return $"Last fetched {minutes} minute{(minutes == 1 ? string.Empty : "s")} ago";
        }

        if (elapsed.TotalDays < 1)
        {
            var hours = Math.Max(1, (int)Math.Floor(elapsed.TotalHours));
            return $"Last fetched {hours} hour{(hours == 1 ? string.Empty : "s")} ago";
        }

        var days = Math.Max(1, (int)Math.Floor(elapsed.TotalDays));
        return $"Last fetched {days} day{(days == 1 ? string.Empty : "s")} ago";
    }

    private void SyncSelectedBranch()
    {
        _suppressBranchSelectionSync = true;
        try
        {
            SelectedBranch = null;
            foreach (var branch in Branches)
            {
                if (!branch.IsCurrent)
                {
                    continue;
                }

                SelectedBranch = branch;
                return;
            }
        }
        finally
        {
            _suppressBranchSelectionSync = false;
        }

        CheckoutSelectedBranchCommand.NotifyCanExecuteChanged();
    }

    public Task AutomationOpenRepositoryAsync(string repositoryPath) => OpenRepositoryAsync(repositoryPath);

    public Task AutomationRefreshAsync() => RefreshAsync();

    public Task AutomationShowWorkingTreeAsync()
    {
        ShowWorkingTree();
        return Task.CompletedTask;
    }

    public Task AutomationShowHistoryAsync()
    {
        ShowHistory();
        return Task.CompletedTask;
    }

    public void AutomationSetCommitMessage(string message)
    {
        CommitMessage = message;
    }

    public Task AutomationStageAllAsync() => StageAllAsync();

    public Task AutomationCommitAsync() => CommitAsync();

    public Task AutomationPushAsync() => PushAsync();

    public async Task<bool> AutomationSelectVisibleFileAsync(string path)
    {
        foreach (var file in VisibleFiles)
        {
            if (!string.Equals(file.Path, path, StringComparison.Ordinal))
            {
                continue;
            }

            SelectedFileEntry = file;
            await LoadDiffAsync(file);
            return true;
        }

        return false;
    }

    public async Task<bool> AutomationSelectCommitAsync(string shortHash)
    {
        foreach (var commit in RecentCommits)
        {
            if (!string.Equals(commit.ShortHash, shortHash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _suppressCommitLoad = true;
            try
            {
                SelectedCommit = commit;
            }
            finally
            {
                _suppressCommitLoad = false;
            }

            await LoadCommitFilesAsync(commit);
            return true;
        }

        return false;
    }

    public async Task<bool> AutomationSelectRecentRepositoryAsync(string value)
    {
        foreach (var repository in RecentRepositories)
        {
            if (!string.Equals(repository.Path, value, StringComparison.Ordinal) &&
                !string.Equals(repository.DisplayName, value, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await OpenRecentRepositoryAsync(repository);
            return true;
        }

        return false;
    }

    public void AutomationSetPaneWidths(double? leftPaneWidth, double? middlePaneWidth)
    {
        if (leftPaneWidth is { } left)
        {
            NavigationPaneWidth = Math.Max(180, left);
        }

        if (middlePaneWidth is { } middle)
        {
            FilePaneWidth = Math.Max(320, middle);
        }
    }
}
