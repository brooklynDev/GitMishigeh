namespace GitMishigeh.Models;

public sealed class GitBranchItem
{
    public GitBranchItem(
        string name,
        bool isCurrent,
        string tipCommitSubject,
        string tipCommitAuthor,
        string tipCommitRelativeTime)
    {
        Name = name;
        IsCurrent = isCurrent;
        TipCommitSubject = tipCommitSubject;
        TipCommitAuthor = tipCommitAuthor;
        TipCommitRelativeTime = tipCommitRelativeTime;
    }

    public string Name { get; }

    public bool IsCurrent { get; }

    public string TipCommitSubject { get; }

    public string TipCommitAuthor { get; }

    public string TipCommitRelativeTime { get; }

    public bool HasTipCommit => !string.IsNullOrWhiteSpace(TipCommitSubject);

    public string TipCommitMeta =>
        string.IsNullOrWhiteSpace(TipCommitAuthor) && string.IsNullOrWhiteSpace(TipCommitRelativeTime)
            ? string.Empty
            : string.IsNullOrWhiteSpace(TipCommitRelativeTime)
                ? TipCommitAuthor
                : string.IsNullOrWhiteSpace(TipCommitAuthor)
                    ? TipCommitRelativeTime
                    : $"{TipCommitAuthor} • {TipCommitRelativeTime}";

    public string HeadLabel => IsCurrent ? "HEAD" : string.Empty;

    public bool CanCheckout => !IsCurrent;

    public bool CanDelete => !IsCurrent;
}
