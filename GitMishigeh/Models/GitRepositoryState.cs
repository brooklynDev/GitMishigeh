using System.Collections.Generic;

namespace GitMishigeh.Models;

public sealed class GitRepositoryState
{
    public GitRepositoryState(
        string currentBranch,
        string statusSummary,
        int aheadCount,
        int behindCount,
        IReadOnlyList<GitBranchItem> branches,
        IReadOnlyList<GitRemoteItem> remotes,
        IReadOnlyList<GitChangedFile> changedFiles,
        IReadOnlyList<GitCommitItem> recentCommits)
    {
        CurrentBranch = currentBranch;
        StatusSummary = statusSummary;
        AheadCount = aheadCount;
        BehindCount = behindCount;
        Branches = branches;
        Remotes = remotes;
        ChangedFiles = changedFiles;
        RecentCommits = recentCommits;
    }

    public string CurrentBranch { get; }

    public string StatusSummary { get; }

    public int AheadCount { get; }

    public int BehindCount { get; }

    public IReadOnlyList<GitBranchItem> Branches { get; }

    public IReadOnlyList<GitRemoteItem> Remotes { get; }

    public IReadOnlyList<GitChangedFile> ChangedFiles { get; }

    public IReadOnlyList<GitCommitItem> RecentCommits { get; }
}
