using System;

namespace GitMishigeh.Models;

public sealed class RecentRepository
{
    public RecentRepository(string path, string displayName, DateTime lastOpenedUtc)
    {
        Path = path;
        DisplayName = displayName;
        LastOpenedUtc = lastOpenedUtc;
    }

    public string Path { get; }

    public string DisplayName { get; }

    public DateTime LastOpenedUtc { get; }
}
