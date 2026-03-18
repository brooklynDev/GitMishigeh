namespace GitMishigeh.Models;

public sealed class GitCommitItem
{
    public GitCommitItem(string shortHash, string message)
    {
        ShortHash = shortHash;
        Message = message;
    }

    public string ShortHash { get; }

    public string Message { get; }
}
