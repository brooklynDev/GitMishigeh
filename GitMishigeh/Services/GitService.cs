using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GitMishigeh.Models;

namespace GitMishigeh.Services;

public sealed class GitService : IGitService
{
    public async Task<GitRepositoryState> GetRepositoryStateAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);

        var statusTask = RunGitCommandAsync(repositoryPath, cancellationToken, "status", "--short", "--branch");
        var logTask = GetRecentCommitsAsync(repositoryPath, cancellationToken);

        await Task.WhenAll(statusTask, logTask);

        var statusOutput = await statusTask;
        var recentCommits = await logTask;
        var changedFiles = ParseChangedFiles(statusOutput.StandardOutput);
        ParseRemoteTrackingCounts(statusOutput.StandardOutput, out var aheadCount, out var behindCount);

        return new GitRepositoryState(
            ParseCurrentBranch(statusOutput.StandardOutput),
            BuildStatusSummary(changedFiles),
            aheadCount,
            behindCount,
            changedFiles,
            recentCommits);
    }

    public async Task<string> StageAllAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "add", ".");
        return BuildMutationMessage(result, "Staged all changes.");
    }

    public async Task<string> UnstageAllAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "reset");
        return BuildMutationMessage(result, "Unstaged all changes.");
    }

    public async Task<string> StageFileAsync(string repositoryPath, GitChangedFile changedFile, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "add", "--", changedFile.DiffPath);
        return BuildMutationMessage(result, $"Staged {changedFile.DiffPath}.");
    }

    public async Task<string> UnstageFileAsync(string repositoryPath, GitChangedFile changedFile, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "restore", "--staged", "--", changedFile.DiffPath);
        return BuildMutationMessage(result, $"Unstaged {changedFile.DiffPath}.");
    }

    public async Task<string> PullAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "pull", "--ff-only");
        return BuildMutationMessage(result, "Pulled latest changes.");
    }

    public async Task<string> PushAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "push");
        return BuildMutationMessage(result, "Pushed current branch.");
    }

    public async Task<string> CommitAsync(string repositoryPath, string commitMessage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            throw new GitServiceException("Enter a commit message before committing.");
        }

        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "commit", "-m", commitMessage.Trim());
        return BuildMutationMessage(result, "Commit created successfully.");
    }

    public async Task<string> GetDiffAsync(string repositoryPath, GitChangedFile changedFile, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);

        if (changedFile.IsUntracked)
        {
            return await BuildUntrackedFileDiffAsync(repositoryPath, changedFile, cancellationToken);
        }

        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "diff", "HEAD", "--", changedFile.DiffPath);
        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? "No textual diff is available for this file."
            : result.StandardOutput;
    }

    private async Task EnsureGitRepositoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            throw new GitServiceException("Choose a local folder before running Git commands.");
        }

        try
        {
            var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "rev-parse", "--is-inside-work-tree");
            if (!string.Equals(result.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new GitServiceException("The selected folder is not a Git repository.");
            }
        }
        catch (GitServiceException exception) when (exception.Message.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
        {
            throw new GitServiceException("The selected folder is not a Git repository.");
        }
    }

    private async Task<IReadOnlyList<GitCommitItem>> GetRecentCommitsAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "log", "--oneline", "-n", "20");
            return ParseCommitLog(result.StandardOutput);
        }
        catch (GitServiceException exception) when (exception.Message.Contains("does not have any commits yet", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<GitCommitItem>();
        }
    }

    private static async Task<string> BuildUntrackedFileDiffAsync(
        string repositoryPath,
        GitChangedFile changedFile,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(repositoryPath, changedFile.DiffPath);
        if (!File.Exists(filePath))
        {
            return "Diff preview is unavailable because the file no longer exists on disk.";
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var diffLines = new List<string>
            {
                $"diff --git a/{changedFile.DiffPath} b/{changedFile.DiffPath}",
                "new file mode 100644",
                "index 0000000..0000000",
                "--- /dev/null",
                $"+++ b/{changedFile.DiffPath}",
                $"@@ -0,0 +1,{lines.Length} @@"
            };

            diffLines.AddRange(lines.Select(line => $"+{line}"));
            return string.Join(Environment.NewLine, diffLines);
        }
        catch (IOException)
        {
            return "Diff preview is unavailable for this file.";
        }
    }

    private async Task<GitCommandResult> RunGitCommandAsync(
        string repositoryPath,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new GitServiceException("Failed to start the git process.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new GitServiceException("Git CLI could not be started. Make sure Git is installed and available on PATH.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            var errorMessage = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
            throw new GitServiceException(errorMessage.Trim());
        }

        return new GitCommandResult(standardOutput.Trim(), standardError.Trim());
    }

    private static List<GitChangedFile> ParseChangedFiles(string statusOutput)
    {
        var changedFiles = new List<GitChangedFile>();
        var lines = statusOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines.Skip(1))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var indexStatus = line[0].ToString();
            var workingTreeStatus = line[1].ToString();
            var path = line[3..].Trim();

            changedFiles.Add(new GitChangedFile(indexStatus, workingTreeStatus, path));
        }

        return changedFiles;
    }

    private static IReadOnlyList<GitCommitItem> ParseCommitLog(string logOutput)
    {
        return logOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var separatorIndex = line.IndexOf(' ');
                return separatorIndex > 0
                    ? new GitCommitItem(line[..separatorIndex], line[(separatorIndex + 1)..])
                    : new GitCommitItem(line, string.Empty);
            })
            .ToList();
    }

    private static string ParseCurrentBranch(string statusOutput)
    {
        var header = statusOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("## ", StringComparison.Ordinal))
        {
            return "Unknown";
        }

        var branchInfo = header[3..];

        if (branchInfo.StartsWith("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return "Detached HEAD";
        }

        if (branchInfo.StartsWith("No commits yet on ", StringComparison.OrdinalIgnoreCase))
        {
            return branchInfo["No commits yet on ".Length..].Trim();
        }

        var endIndex = branchInfo.IndexOfAny(new[] { '.', ' ', '[' });
        return endIndex >= 0 ? branchInfo[..endIndex] : branchInfo;
    }

    private static void ParseRemoteTrackingCounts(string statusOutput, out int aheadCount, out int behindCount)
    {
        aheadCount = 0;
        behindCount = 0;

        var header = statusOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(header))
        {
            return;
        }

        var aheadMarker = "ahead ";
        var behindMarker = "behind ";

        var aheadIndex = header.IndexOf(aheadMarker, StringComparison.Ordinal);
        if (aheadIndex >= 0)
        {
            aheadCount = ParseTrackingCount(header, aheadIndex + aheadMarker.Length);
        }

        var behindIndex = header.IndexOf(behindMarker, StringComparison.Ordinal);
        if (behindIndex >= 0)
        {
            behindCount = ParseTrackingCount(header, behindIndex + behindMarker.Length);
        }
    }

    private static int ParseTrackingCount(string header, int startIndex)
    {
        var endIndex = startIndex;
        while (endIndex < header.Length && char.IsDigit(header[endIndex]))
        {
            endIndex++;
        }

        return int.TryParse(header[startIndex..endIndex], out var count) ? count : 0;
    }

    private static string BuildStatusSummary(IReadOnlyCollection<GitChangedFile> changedFiles)
    {
        if (changedFiles.Count == 0)
        {
            return "Working tree clean.";
        }

        var stagedCount = changedFiles.Count(file => file.IsStaged);
        var modifiedCount = changedFiles.Count(file => file.IsModified && !file.IsUntracked);
        var untrackedCount = changedFiles.Count(file => file.IsUntracked);

        return $"{changedFiles.Count} changed • {stagedCount} staged • {modifiedCount} modified • {untrackedCount} untracked";
    }

    private static string BuildMutationMessage(GitCommandResult result, string fallbackMessage)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput;
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return result.StandardError;
        }

        return fallbackMessage;
    }

    private sealed record GitCommandResult(string StandardOutput, string StandardError);
}

public sealed class GitServiceException : Exception
{
    public GitServiceException(string message) : base(message)
    {
    }
}
