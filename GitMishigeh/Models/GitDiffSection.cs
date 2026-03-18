using System.Collections.Generic;

namespace GitMishigeh.Models;

public sealed class GitDiffSection
{
    public GitDiffSection(string title, IReadOnlyList<GitDiffLine> lines, bool isChunk)
    {
        Title = title;
        Lines = lines;
        IsChunk = isChunk;
    }

    public string Title { get; }

    public IReadOnlyList<GitDiffLine> Lines { get; }

    public bool IsChunk { get; }
}
