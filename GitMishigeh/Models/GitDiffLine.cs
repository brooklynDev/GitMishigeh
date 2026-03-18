namespace GitMishigeh.Models;

public sealed class GitDiffLine
{
    public GitDiffLine(
        string text,
        string foreground,
        string background,
        string leftLineNumber,
        string rightLineNumber,
        bool isEmphasized = false)
    {
        Text = text;
        Foreground = foreground;
        Background = background;
        LeftLineNumber = leftLineNumber;
        RightLineNumber = rightLineNumber;
        IsEmphasized = isEmphasized;
    }

    public string Text { get; }

    public string Foreground { get; }

    public string Background { get; }

    public string LeftLineNumber { get; }

    public string RightLineNumber { get; }

    public bool IsEmphasized { get; }
}
