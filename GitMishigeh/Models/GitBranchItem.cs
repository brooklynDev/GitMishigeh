namespace GitMishigeh.Models;

public sealed class GitBranchItem
{
    public GitBranchItem(string name, bool isCurrent)
    {
        Name = name;
        IsCurrent = isCurrent;
    }

    public string Name { get; }

    public bool IsCurrent { get; }

    public string HeadLabel => IsCurrent ? "HEAD" : string.Empty;

    public bool CanCheckout => !IsCurrent;

    public bool CanDelete => !IsCurrent;
}
