using CommunityToolkit.Mvvm.ComponentModel;

namespace GitMishigeh.Models;

public partial class GitChangedFile : ObservableObject
{
    public GitChangedFile(string indexStatus, string workingTreeStatus, string path)
    {
        IndexStatus = indexStatus;
        WorkingTreeStatus = workingTreeStatus;
        Path = path;
    }

    public string IndexStatus { get; }

    public string WorkingTreeStatus { get; }

    public string Path { get; }

    public string DiffPath =>
        Path.Contains(" -> ", System.StringComparison.Ordinal)
            ? Path[(Path.LastIndexOf(" -> ", System.StringComparison.Ordinal) + 4)..]
            : Path;

    public string StatusCode => $"{IndexStatus}{WorkingTreeStatus}";

    public bool IsUntracked => IndexStatus == "?" && WorkingTreeStatus == "?";

    public bool IsStaged => IndexStatus is not " " and not "?";

    public bool IsModified => WorkingTreeStatus != " ";

    public string StatusLabel =>
        IsUntracked ? "Untracked" :
        IsStaged && IsModified ? "Staged + Modified" :
        IsStaged ? "Staged" :
        "Modified";

    [ObservableProperty]
    private bool isSelected;
}
