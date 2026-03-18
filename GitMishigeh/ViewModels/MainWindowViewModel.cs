using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
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
    private readonly IRecentRepositoryStore _recentRepositoryStore;
    private string? _selectedFolderPath;
    private bool _hasValidRepository;
    private CancellationTokenSource? _diffCancellationTokenSource;

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
        SelectedDiffLines = new ObservableCollection<GitDiffLine>();
        RecentCommits = new ObservableCollection<GitCommitItem>();
        RecentRepositories = new ObservableCollection<RecentRepository>();

        OpenRepoCommand = new AsyncRelayCommand(OpenRepoAsync, CanOpenRepo);
        OpenRecentRepositoryCommand = new AsyncRelayCommand<RecentRepository?>(OpenRecentRepositoryAsync, CanOpenRecentRepository);
        SelectChangedFileCommand = new RelayCommand<GitChangedFile?>(SelectChangedFile);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        StageAllCommand = new AsyncRelayCommand(StageAllAsync, CanStageAll);
        UnstageAllCommand = new AsyncRelayCommand(UnstageAllAsync, CanUnstageAll);
        CommitCommand = new AsyncRelayCommand(CommitAsync, CanCommit);

        OutputMessage = "Open a local Git repository to begin.";
        RepositoryPath = "No repository selected";
        CurrentBranch = "No branch";
        StatusSummary = "Repository details will appear here.";
        SelectedDiffHeader = "Diff Preview";
        SelectedDiff = "Select a changed file to inspect its diff.";
        SyncCollection(SelectedDiffLines, BuildDiffLines(SelectedDiff));

        ChangedFiles.CollectionChanged += (_, _) => NotifyCommandStateChanged();
        _ = LoadRecentRepositoriesAsync();
    }

    public ObservableCollection<GitChangedFile> ChangedFiles { get; }

    public ObservableCollection<GitDiffLine> SelectedDiffLines { get; }

    public ObservableCollection<GitCommitItem> RecentCommits { get; }

    public ObservableCollection<RecentRepository> RecentRepositories { get; }

    public IAsyncRelayCommand OpenRepoCommand { get; }

    public IAsyncRelayCommand<RecentRepository?> OpenRecentRepositoryCommand { get; }

    public IRelayCommand<GitChangedFile?> SelectChangedFileCommand { get; }

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

    [ObservableProperty]
    private GitChangedFile? selectedChangedFile;

    [ObservableProperty]
    private string selectedDiffHeader = string.Empty;

    [ObservableProperty]
    private string selectedDiff = string.Empty;

    public bool HasChangedFiles => ChangedFiles.Count > 0;

    public bool HasRecentCommits => RecentCommits.Count > 0;

    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    private bool CanOpenRepo() => !IsBusy;

    private bool CanOpenRecentRepository(RecentRepository? repository) => !IsBusy && repository is not null;

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
            UpdateSelectedFileAfterRefresh();

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
            ClearDiffPreview("Diff Preview", "Select a valid Git repository to inspect file diffs.");
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
        UpdateSelectedFileAfterRefresh();
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

    private async Task OpenRepositoryAsync(string repositoryPath)
    {
        _selectedFolderPath = repositoryPath;
        RepositoryPath = repositoryPath;
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
        OnPropertyChanged(nameof(HasRecentRepositories));
    }

    private async Task SaveRecentRepositoryAsync(string repositoryPath)
    {
        var repositories = await _recentRepositoryStore.AddOrUpdateAsync(repositoryPath);
        SyncCollection(RecentRepositories, repositories);
        OnPropertyChanged(nameof(HasRecentRepositories));
    }

    private void NotifyCommandStateChanged()
    {
        OpenRepoCommand.NotifyCanExecuteChanged();
        OpenRecentRepositoryCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
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

    partial void OnIsBusyChanged(bool value)
    {
        NotifyCommandStateChanged();
    }

    partial void OnSelectedChangedFileChanged(GitChangedFile? value)
    {
        UpdateSelectedFlags(value);
        _ = LoadDiffAsync(value);
    }

    private void UpdateSelectedFileAfterRefresh()
    {
        if (ChangedFiles.Count == 0)
        {
            SelectedChangedFile = null;
            ClearDiffPreview("Diff Preview", "Working tree is clean.");
            return;
        }

        if (SelectedChangedFile is not null)
        {
            foreach (var changedFile in ChangedFiles)
            {
                if (string.Equals(changedFile.Path, SelectedChangedFile.Path, StringComparison.Ordinal))
                {
                    SelectedChangedFile = changedFile;
                    return;
                }
            }
        }

        SelectedChangedFile = ChangedFiles[0];
    }

    private async Task LoadDiffAsync(GitChangedFile? changedFile)
    {
        _diffCancellationTokenSource?.Cancel();
        _diffCancellationTokenSource?.Dispose();
        _diffCancellationTokenSource = null;

        if (!_hasValidRepository || string.IsNullOrWhiteSpace(_selectedFolderPath) || changedFile is null)
        {
            ClearDiffPreview("Diff Preview", "Select a changed file to inspect its diff.");
            return;
        }

        SelectedDiffHeader = changedFile.DiffPath;
        SelectedDiff = "Loading diff...";
        SyncCollection(SelectedDiffLines, BuildDiffLines(SelectedDiff));

        var cancellationTokenSource = new CancellationTokenSource();
        _diffCancellationTokenSource = cancellationTokenSource;

        try
        {
            SelectedDiff = await _gitService.GetDiffAsync(_selectedFolderPath, changedFile, cancellationTokenSource.Token);
            SyncCollection(SelectedDiffLines, BuildDiffLines(SelectedDiff));
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitServiceException exception)
        {
            SelectedDiff = exception.Message;
            SyncCollection(SelectedDiffLines, BuildDiffLines(SelectedDiff));
        }
    }

    private void ClearDiffPreview(string header, string content)
    {
        SelectedDiffHeader = header;
        SelectedDiff = content;
        SyncCollection(SelectedDiffLines, BuildDiffLines(content));
    }

    private void SelectChangedFile(GitChangedFile? changedFile)
    {
        if (changedFile is not null)
        {
            SelectedChangedFile = changedFile;
        }
    }

    private void UpdateSelectedFlags(GitChangedFile? selectedFile)
    {
        foreach (var changedFile in ChangedFiles)
        {
            changedFile.IsSelected = ReferenceEquals(changedFile, selectedFile);
        }
    }

    private static IReadOnlyList<GitDiffLine> BuildDiffLines(string diffText)
    {
        var lines = diffText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var diffLines = new List<GitDiffLine>(lines.Length);
        var leftLineNumber = 0;
        var rightLineNumber = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                ParseHunkHeader(line, out leftLineNumber, out rightLineNumber);
                diffLines.Add(new GitDiffLine(line, "#7DD3FC", "#10273A", string.Empty, string.Empty, true));
                continue;
            }

            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                diffLines.Add(new GitDiffLine(
                    line,
                    "#DCFCE7",
                    "#10261C",
                    string.Empty,
                    rightLineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    true));
                rightLineNumber++;
                continue;
            }

            if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
            {
                diffLines.Add(new GitDiffLine(
                    line,
                    "#FECACA",
                    "#34131C",
                    leftLineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    string.Empty,
                    true));
                leftLineNumber++;
                continue;
            }

            if (line.StartsWith("diff --git", StringComparison.Ordinal) ||
                line.StartsWith("index ", StringComparison.Ordinal) ||
                line.StartsWith("---", StringComparison.Ordinal) ||
                line.StartsWith("+++", StringComparison.Ordinal) ||
                line.StartsWith("new file mode", StringComparison.Ordinal))
            {
                diffLines.Add(new GitDiffLine(line, "#A5B4CC", "#111827", string.Empty, string.Empty, true));
                continue;
            }

            if (line.StartsWith("\\", StringComparison.Ordinal))
            {
                diffLines.Add(new GitDiffLine(line, "#94A3B8", "#111827", string.Empty, string.Empty));
                continue;
            }

            diffLines.Add(new GitDiffLine(
                line,
                "#D8DEE9",
                "Transparent",
                leftLineNumber > 0 ? leftLineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
                rightLineNumber > 0 ? rightLineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty));

            if (!string.IsNullOrEmpty(line))
            {
                leftLineNumber++;
                rightLineNumber++;
            }
        }

        return diffLines;
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

        return int.TryParse(number, out var value) ? value : 0;
    }
}
