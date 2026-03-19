using System.Collections.Generic;

namespace GitMishigeh.Models;

public sealed class GitDiffSection
{
    public GitDiffSection(
        string title,
        string subtitle,
        string kindLabel,
        string accentBrush,
        IReadOnlyList<GitDiffLine> lines,
        bool isChunk)
    {
        Title = title;
        Subtitle = subtitle;
        KindLabel = kindLabel;
        AccentBrush = accentBrush;
        Lines = lines;
        IsChunk = isChunk;
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string KindLabel { get; }

    public string AccentBrush { get; }

    public IReadOnlyList<GitDiffLine> Lines { get; }

    public bool IsChunk { get; }

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
}
