using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitMishigeh.Models;
using GitMishigeh.Services;

namespace GitMishigeh.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IGitService _gitService;
    private readonly IFolderPickerService _folderPickerService;
    private string? _selectedFolderPath;
    private bool _hasValidRepository;

    public MainWindowViewModel() : this(new GitService(), new FolderPickerService())
    {
    }

    public MainWindowViewModel(IGitService gitService, IFolderPickerService folderPickerService)
    {
        _gitService = gitService;
        _folderPickerService = folderPickerService;

        ChangedFiles = new ObservableCollection<GitChangedFile>();
        RecentCommits = new ObservableCollection<GitCommitItem>();

        OpenRepoCommand = new AsyncRelayCommand(OpenRepoAsync, CanOpenRepo);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        StageAllCommand = new AsyncRelayCommand(StageAllAsync, CanStageAll);
        UnstageAllCommand = new AsyncRelayCommand(UnstageAllAsync, CanUnstageAll);
        CommitCommand = new AsyncRelayCommand(CommitAsync, CanCommit);

        OutputMessage = "Open a local Git repository to begin.";
        RepositoryPath = "No repository selected";
        CurrentBranch = "No branch";
        StatusSummary = "Repository details will appear here.";

        ChangedFiles.CollectionChanged += (_, _) => NotifyCommandStateChanged();
    }

    public ObservableCollection<GitChangedFile> ChangedFiles { get; }

    public ObservableCollection<GitCommitItem> RecentCommits { get; }

    public IAsyncRelayCommand OpenRepoCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand StageAllCommand { get; }

    public IAsyncRelayCommand UnstageAllCommand { get; }

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

    public bool HasChangedFiles => ChangedFiles.Count > 0;

    public bool HasRecentCommits => RecentCommits.Count > 0;

    private bool CanOpenRepo() => !IsBusy;

    private bool CanRefresh() => !IsBusy && _hasValidRepository;

    private bool CanStageAll() => !IsBusy && _hasValidRepository && ChangedFiles.Count > 0;

    private bool CanUnstageAll() => !IsBusy && _hasValidRepository && ChangedFiles.Count > 0;

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

        _selectedFolderPath = selectedFolder;
        RepositoryPath = selectedFolder;

        await RefreshAsync();
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

            _hasValidRepository = true;
            CurrentBranch = repositoryState.CurrentBranch;
            StatusSummary = repositoryState.StatusSummary;

            SyncCollection(ChangedFiles, repositoryState.ChangedFiles);
            SyncCollection(RecentCommits, repositoryState.RecentCommits);

            OnPropertyChanged(nameof(HasChangedFiles));
            OnPropertyChanged(nameof(HasRecentCommits));

            OutputMessage = $"Repository refreshed at {DateTime.Now:t}.";
        }
        catch (GitServiceException exception)
        {
            _hasValidRepository = false;
            CurrentBranch = "Unavailable";
            StatusSummary = "The selected folder is not a Git repository.";
            SyncCollection(ChangedFiles, Array.Empty<GitChangedFile>());
            SyncCollection(RecentCommits, Array.Empty<GitCommitItem>());
            OnPropertyChanged(nameof(HasChangedFiles));
            OnPropertyChanged(nameof(HasRecentCommits));
            OutputMessage = exception.Message;
        }
        finally
        {
            NotifyCommandStateChanged();
        }
    });

    private Task StageAllAsync() => ExecuteGitActionAsync(
        () => _gitService.StageAllAsync(_selectedFolderPath!),
        clearCommitMessage: false);

    private Task UnstageAllAsync() => ExecuteGitActionAsync(
        () => _gitService.UnstageAllAsync(_selectedFolderPath!),
        clearCommitMessage: false);

    private Task CommitAsync() => ExecuteGitActionAsync(
        () => _gitService.CommitAsync(_selectedFolderPath!, CommitMessage),
        clearCommitMessage: true);

    private async Task ExecuteGitActionAsync(Func<Task<string>> action, bool clearCommitMessage)
    {
        await RunBusyAsync(async () =>
        {
            try
            {
                OutputMessage = await action();
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
        _hasValidRepository = true;
        CurrentBranch = repositoryState.CurrentBranch;
        StatusSummary = repositoryState.StatusSummary;
        SyncCollection(ChangedFiles, repositoryState.ChangedFiles);
        SyncCollection(RecentCommits, repositoryState.RecentCommits);
        OnPropertyChanged(nameof(HasChangedFiles));
        OnPropertyChanged(nameof(HasRecentCommits));
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

    private void NotifyCommandStateChanged()
    {
        OpenRepoCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();
    }

    private void SyncCollection<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
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

    partial void OnIsBusyChanged(bool value)
    {
        NotifyCommandStateChanged();
    }
}
