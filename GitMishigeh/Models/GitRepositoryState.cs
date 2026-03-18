using System.Collections.Generic;

namespace GitMishigeh.Models;

public sealed class GitRepositoryState
{
    public GitRepositoryState(
        string currentBranch,
        string statusSummary,
        IReadOnlyList<GitChangedFile> changedFiles,
        IReadOnlyList<GitCommitItem> recentCommits)
    {
        CurrentBranch = currentBranch;
        StatusSummary = statusSummary;
        ChangedFiles = changedFiles;
        RecentCommits = recentCommits;
    }

    public string CurrentBranch { get; }

    public string StatusSummary { get; }

    public IReadOnlyList<GitChangedFile> ChangedFiles { get; }

    public IReadOnlyList<GitCommitItem> RecentCommits { get; }
}
