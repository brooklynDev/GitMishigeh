namespace GitMishigeh.Models;

public sealed class GitDiffDisplayRow
{
    public GitDiffDisplayRow(
        bool isSectionHeader,
        bool isSpacer,
        bool isChunk,
        string sectionTitle,
        string text,
        string foreground,
        string background,
        string leftLineNumber,
        string rightLineNumber)
    {
        IsSectionHeader = isSectionHeader;
        IsSpacer = isSpacer;
        IsChunk = isChunk;
        SectionTitle = sectionTitle;
        Text = text;
        Foreground = foreground;
        Background = background;
        LeftLineNumber = leftLineNumber;
        RightLineNumber = rightLineNumber;
    }

    public bool IsSectionHeader { get; }

    public bool IsSpacer { get; }

    public bool IsDiffLine => !IsSectionHeader && !IsSpacer;

    public bool IsChunk { get; }

    public string SectionTitle { get; }

    public string Text { get; }

    public string Foreground { get; }

    public string Background { get; }

    public string LeftLineNumber { get; }

    public string RightLineNumber { get; }
}
