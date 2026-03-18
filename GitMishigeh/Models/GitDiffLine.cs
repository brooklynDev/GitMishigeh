namespace GitMishigeh.Models;

public sealed class GitDiffLine
{
    public GitDiffLine(string text, string foreground, string background)
    {
        Text = text;
        Foreground = foreground;
        Background = background;
    }

    public string Text { get; }

    public string Foreground { get; }

    public string Background { get; }
}
