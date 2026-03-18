using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace GitMishigeh.Controls;

public sealed class GitDiffColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush AddedForeground = Brush.Parse("#A7F3C0");
    private static readonly IBrush AddedBackground = Brush.Parse("#0D2017");
    private static readonly IBrush RemovedForeground = Brush.Parse("#F9A8B8");
    private static readonly IBrush RemovedBackground = Brush.Parse("#241218");
    private static readonly IBrush HunkForeground = Brush.Parse("#9FC1F3");
    private static readonly IBrush HunkBackground = Brush.Parse("#0E1827");
    private static readonly IBrush MetadataForeground = Brush.Parse("#7F93B2");
    private static readonly IBrush PlainForeground = Brush.Parse("#D8E1EE");

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        var (foreground, background) = GetStyle(text);

        ChangeLinePart(line.Offset, line.EndOffset, visualLineElement =>
        {
            visualLineElement.TextRunProperties.SetForegroundBrush(foreground);

            if (background is not null)
            {
                visualLineElement.TextRunProperties.SetBackgroundBrush(background);
            }
        });
    }

    private static (IBrush foreground, IBrush? background) GetStyle(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (PlainForeground, null);
        }

        if (text.StartsWith("@@", System.StringComparison.Ordinal))
        {
            return (HunkForeground, HunkBackground);
        }

        if (text.StartsWith("+", System.StringComparison.Ordinal) &&
            !text.StartsWith("+++", System.StringComparison.Ordinal))
        {
            return (AddedForeground, AddedBackground);
        }

        if (text.StartsWith("-", System.StringComparison.Ordinal) &&
            !text.StartsWith("---", System.StringComparison.Ordinal))
        {
            return (RemovedForeground, RemovedBackground);
        }

        if (text.StartsWith("diff --git", System.StringComparison.Ordinal) ||
            text.StartsWith("index ", System.StringComparison.Ordinal) ||
            text.StartsWith("---", System.StringComparison.Ordinal) ||
            text.StartsWith("+++", System.StringComparison.Ordinal) ||
            text.StartsWith("new file mode", System.StringComparison.Ordinal) ||
            text.StartsWith("deleted file mode", System.StringComparison.Ordinal))
        {
            return (MetadataForeground, null);
        }

        if (text.StartsWith("\\", System.StringComparison.Ordinal))
        {
            return (MetadataForeground, null);
        }

        return (PlainForeground, null);
    }
}
