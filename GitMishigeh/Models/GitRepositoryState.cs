using System.Collections.Generic;

namespace GitMishigeh.Models;

public sealed class GitRepositoryState
{
    public GitRepositoryState(
        string currentBranch,
        string statusSummary,
        int aheadCount,
        int behindCount,
        IReadOnlyList<GitChangedFile> changedFiles,
        IReadOnlyList<GitCommitItem> recentCommits)
    {
        CurrentBranch = currentBranch;
        StatusSummary = statusSummary;
        AheadCount = aheadCount;
        BehindCount = behindCount;
        ChangedFiles = changedFiles;
        RecentCommits = recentCommits;
    }

    public string CurrentBranch { get; }

    public string StatusSummary { get; }

    public int AheadCount { get; }

    public int BehindCount { get; }

    public IReadOnlyList<GitChangedFile> ChangedFiles { get; }

    public IReadOnlyList<GitCommitItem> RecentCommits { get; }
}
