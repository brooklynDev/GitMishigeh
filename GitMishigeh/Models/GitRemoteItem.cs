namespace GitMishigeh.Models;

public sealed class GitRemoteItem
{
    public GitRemoteItem(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
