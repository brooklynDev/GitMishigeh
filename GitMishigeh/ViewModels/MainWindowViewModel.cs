using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
        RecentCommits = new ObservableCollection<GitCommitItem>();
        RecentRepositories = new ObservableCollection<RecentRepository>();
        DiffSections = new ObservableCollection<GitDiffSection>();

        OpenRepoCommand = new AsyncRelayCommand(OpenRepoAsync, CanOpenRepo);
        OpenRecentRepositoryCommand = new AsyncRelayCommand<RecentRepository?>(OpenRecentRepositoryAsync, CanOpenRecentRepository);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        PullCommand = new AsyncRelayCommand(PullAsync, CanSyncWithRemote);
        PushCommand = new AsyncRelayCommand(PushAsync, CanSyncWithRemote);
        StageAllCommand = new AsyncRelayCommand(StageAllAsync, CanStageAll);
        UnstageAllCommand = new AsyncRelayCommand(UnstageAllAsync, CanUnstageAll);
        ToggleStageFileCommand = new AsyncRelayCommand<GitChangedFile?>(ToggleStageFileAsync, CanToggleStageFile);
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
        SelectedDiffHeader = "Diff Preview";
        SelectedDiff = "Select a changed file to inspect its diff.";
        ApplyDiffContent(SelectedDiff);

        ChangedFiles.CollectionChanged += (_, _) => NotifyCommandStateChanged();
        _ = LoadRecentRepositoriesAsync();
    }

    public ObservableCollection<GitChangedFile> ChangedFiles { get; }

    public ObservableCollection<GitCommitItem> RecentCommits { get; }

    public ObservableCollection<RecentRepository> RecentRepositories { get; }

    public ObservableCollection<GitDiffSection> DiffSections { get; }

    public IAsyncRelayCommand OpenRepoCommand { get; }

    public IAsyncRelayCommand<RecentRepository?> OpenRecentRepositoryCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand PullCommand { get; }

    public IAsyncRelayCommand PushCommand { get; }

    public IAsyncRelayCommand StageAllCommand { get; }

    public IAsyncRelayCommand UnstageAllCommand { get; }

    public IAsyncRelayCommand<GitChangedFile?> ToggleStageFileCommand { get; }

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

    public bool HasDiffSections => DiffSections.Count > 0;

    private bool CanOpenRepo() => !IsBusy;

    private bool CanOpenRecentRepository(RecentRepository? repository) => !IsBusy && repository is not null;

    private bool CanRefresh() => !IsBusy && _hasValidRepository;

    private bool CanSyncWithRemote() => !IsBusy && _hasValidRepository;

    private bool CanStageAll() => !IsBusy && _hasValidRepository && ChangedFiles.Count > 0;

    private bool CanUnstageAll() => !IsBusy && _hasValidRepository && ChangedFiles.Count > 0;

    private bool CanToggleStageFile(GitChangedFile? changedFile) => !IsBusy && _hasValidRepository && changedFile is not null;

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

    private Task PullAsync() => ExecuteGitActionAsync(
        () => _gitService.PullAsync(_selectedFolderPath!),
        clearCommitMessage: false);

    private Task PushAsync() => ExecuteGitActionAsync(
        () => _gitService.PushAsync(_selectedFolderPath!),
        clearCommitMessage: false);

    private Task UnstageAllAsync() => ExecuteGitActionAsync(
        () => _gitService.UnstageAllAsync(_selectedFolderPath!),
        clearCommitMessage: false);

    private Task CommitAsync() => ExecuteGitActionAsync(
        () => _gitService.CommitAsync(_selectedFolderPath!, CommitMessage),
        clearCommitMessage: true);

    private Task ToggleStageFileAsync(GitChangedFile? changedFile)
    {
        if (changedFile is null)
        {
            return Task.CompletedTask;
        }

        return ExecuteGitActionAsync(
            () => changedFile.IsStaged
                ? _gitService.UnstageFileAsync(_selectedFolderPath!, changedFile)
                : _gitService.StageFileAsync(_selectedFolderPath!, changedFile),
            clearCommitMessage: false);
    }

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
        ApplyRepositoryState(repositoryState);
    }

    private void ApplyRepositoryState(GitRepositoryState repositoryState)
    {
        _hasValidRepository = true;
        CurrentBranch = repositoryState.CurrentBranch;
        StatusSummary = repositoryState.StatusSummary;
        SyncCollection(ChangedFiles, repositoryState.ChangedFiles);
        SyncCollection(RecentCommits, repositoryState.RecentCommits);
        UpdateSelectedFileAfterRefresh();
        OnPropertyChanged(nameof(HasChangedFiles));
        OnPropertyChanged(nameof(HasRecentCommits));
        _repositoryFingerprint = BuildRepositoryFingerprint(repositoryState);
    }

    private void ClearRepositoryStateForInvalidRepo(string message)
    {
        _hasValidRepository = false;
        CurrentBranch = "Unavailable";
        StatusSummary = "The selected folder is not a Git repository.";
        SyncCollection(ChangedFiles, Array.Empty<GitChangedFile>());
        SyncCollection(RecentCommits, Array.Empty<GitCommitItem>());
        ClearDiffPreview("Diff Preview", "Select a valid Git repository to inspect file diffs.");
        OnPropertyChanged(nameof(HasChangedFiles));
        OnPropertyChanged(nameof(HasRecentCommits));
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
        PullCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        ToggleStageFileCommand.NotifyCanExecuteChanged();
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
            ClearDiffPreview("Diff Preview", "Select a changed file to inspect its diff.");
            return;
        }

        SelectedDiffHeader = changedFile.DiffPath;
        SelectedDiff = "Loading diff...";
        ApplyDiffContent(SelectedDiff);

        var cancellationTokenSource = new CancellationTokenSource();
        _diffCancellationTokenSource = cancellationTokenSource;

        try
        {
            SelectedDiff = await _gitService.GetDiffAsync(_selectedFolderPath, changedFile, cancellationTokenSource.Token);
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
                .Append(commit.ShortHash)
                .Append(':')
                .Append(commit.Message)
                .Append('|');
        }

        return builder.ToString();
    }
}
