namespace GitMishigeh.Models;

public sealed class GitCommitItem
{
    public GitCommitItem(string hash, string shortHash, string message, string refs, bool showConnector)
    {
        Hash = hash;
        ShortHash = shortHash;
        Message = message;
        Refs = refs;
        ShowConnector = showConnector;
    }

    public string Hash { get; }

    public string ShortHash { get; }

    public string Message { get; }

    public string Refs { get; }

    public bool ShowConnector { get; }

    public bool HasRefs => !string.IsNullOrWhiteSpace(Refs);
}
