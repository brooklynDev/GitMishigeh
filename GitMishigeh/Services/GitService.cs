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
        var branchTask = GetBranchesAsync(repositoryPath, cancellationToken);
        var remoteTask = GetRemotesAsync(repositoryPath, cancellationToken);

        await Task.WhenAll(statusTask, logTask, branchTask, remoteTask);

        var statusOutput = await statusTask;
        var recentCommits = await logTask;
        var branches = await branchTask;
        var remotes = await remoteTask;
        var changedFiles = ParseChangedFiles(statusOutput.StandardOutput);
        ParseRemoteTrackingCounts(statusOutput.StandardOutput, out var aheadCount, out var behindCount);

        return new GitRepositoryState(
            ParseCurrentBranch(statusOutput.StandardOutput),
            BuildStatusSummary(changedFiles),
            aheadCount,
            behindCount,
            branches,
            remotes,
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

    public async Task<string> DiscardFileAsync(string repositoryPath, GitChangedFile changedFile, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);

        if (changedFile.IsUntracked)
        {
            DeleteUntrackedPath(repositoryPath, changedFile.DiffPath);
            return $"Deleted untracked {changedFile.DiffPath}.";
        }

        GitCommandResult result;
        if (string.Equals(changedFile.IndexStatus, "A", StringComparison.Ordinal))
        {
            result = await RunGitCommandAsync(repositoryPath, cancellationToken, "rm", "-f", "--", changedFile.DiffPath);
        }
        else
        {
            result = await RunGitCommandAsync(
                repositoryPath,
                cancellationToken,
                "restore",
                "--source=HEAD",
                "--staged",
                "--worktree",
                "--",
                changedFile.DiffPath);
        }

        return BuildMutationMessage(result, $"Discarded changes in {changedFile.DiffPath}.");
    }

    public async Task<string> FetchAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "fetch", "origin", "--prune");
        return BuildMutationMessage(result, "Fetched origin.");
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
        GitCommandResult result;
        try
        {
            result = await RunGitCommandAsync(repositoryPath, cancellationToken, "push");
        }
        catch (GitServiceException exception) when (exception.Message.Contains("has no upstream branch", StringComparison.OrdinalIgnoreCase))
        {
            var branchResult = await RunGitCommandAsync(repositoryPath, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD");
            var branchName = branchResult.StandardOutput.Trim();
            if (string.IsNullOrWhiteSpace(branchName))
            {
                throw;
            }

            result = await RunGitCommandAsync(repositoryPath, cancellationToken, "push", "--set-upstream", "origin", branchName);
        }

        return BuildMutationMessage(result, "Pushed current branch.");
    }

    public async Task<string> CheckoutBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new GitServiceException("Choose a branch to switch to.");
        }

        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var normalizedBranchName = branchName.Trim();
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "checkout", normalizedBranchName);
        return BuildMutationMessage(result, $"Switched to {normalizedBranchName}.");
    }

    public async Task<string> CreateBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new GitServiceException("Enter a new branch name.");
        }

        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var normalizedBranchName = branchName.Trim();
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "checkout", "-b", normalizedBranchName);
        return BuildMutationMessage(result, $"Created and switched to {normalizedBranchName}.");
    }

    public async Task<string> DeleteBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new GitServiceException("Choose a branch to delete.");
        }

        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var normalizedBranchName = branchName.Trim();
        GitCommandResult result;
        try
        {
            result = await RunGitCommandAsync(repositoryPath, cancellationToken, "branch", "-d", normalizedBranchName);
        }
        catch (GitServiceException exception) when (exception.Message.Contains("not fully merged", StringComparison.OrdinalIgnoreCase))
        {
            throw new GitServiceException($"'{normalizedBranchName}' still has unmerged commits. Use Force Delete if you want to remove it anyway.");
        }

        return BuildMutationMessage(result, $"Deleted {normalizedBranchName}.");
    }

    public async Task<string> ForceDeleteBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new GitServiceException("Choose a branch to delete.");
        }

        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var normalizedBranchName = branchName.Trim();
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "branch", "-D", normalizedBranchName);
        return BuildMutationMessage(result, $"Force deleted {normalizedBranchName}.");
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

    public async Task<IReadOnlyList<GitChangedFile>> GetCommitFilesAsync(
        string repositoryPath,
        GitCommitItem commit,
        CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "show", "--format=", "--name-status", commit.Hash);
        return ParseCommitFiles(result.StandardOutput);
    }

    public async Task<string> GetCommitFileDiffAsync(
        string repositoryPath,
        GitCommitItem commit,
        GitChangedFile changedFile,
        CancellationToken cancellationToken = default)
    {
        await EnsureGitRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "show", "--format=", commit.Hash, "--", changedFile.DiffPath);
        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? "No textual diff is available for this file in the selected commit."
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
            var result = await RunGitCommandAsync(
                repositoryPath,
                cancellationToken,
                "log",
                "--decorate=short",
                "--pretty=format:%H%x1f%h%x1f%an%x1f%s%x1f%D",
                "-n",
                "30");
            return ParseCommitLog(result.StandardOutput);
        }
        catch (GitServiceException exception) when (exception.Message.Contains("does not have any commits yet", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<GitCommitItem>();
        }
    }

    private async Task<IReadOnlyList<GitBranchItem>> GetBranchesAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var result = await RunGitCommandAsync(
            repositoryPath,
            cancellationToken,
            "for-each-ref",
            "--sort=-committerdate",
            "--format=%(refname:short)\t%(HEAD)\t%(authorname)\t%(contents:subject)\t%(committerdate:relative)",
            "refs/heads");

        var branches = result.StandardOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split('\t');
                var name = parts[0].Trim();
                var isCurrent = parts.Length > 1 && parts[1].Trim() == "*";
                var tipCommitAuthor = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                var tipCommitSubject = parts.Length > 3 ? parts[3].Trim() : string.Empty;
                var tipCommitRelativeTime = parts.Length > 4 ? parts[4].Trim() : string.Empty;
                return new GitBranchItem(name, isCurrent, tipCommitSubject, tipCommitAuthor, tipCommitRelativeTime);
            })
            .ToList();

        return branches
            .OrderByDescending(branch => branch.IsCurrent)
            .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<GitRemoteItem>> GetRemotesAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var result = await RunGitCommandAsync(repositoryPath, cancellationToken, "remote");
        return result.StandardOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Select(name => new GitRemoteItem(name.Trim()))
            .ToList();
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

    private static void DeleteUntrackedPath(string repositoryPath, string diffPath)
    {
        var fullPath = Path.Combine(repositoryPath, diffPath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return;
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
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
        var lines = logOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        return lines
            .Select((line, index) =>
            {
                var parts = line.Split('\x1f');
                var hash = parts.Length > 0 ? parts[0] : string.Empty;
                var shortHash = parts.Length > 1 ? parts[1] : hash;
                var authorName = parts.Length > 2 ? parts[2] : string.Empty;
                var message = parts.Length > 3 ? parts[3] : string.Empty;
                var refs = parts.Length > 4 ? parts[4] : string.Empty;
                return new GitCommitItem(hash, shortHash, authorName, message, refs, index < lines.Length - 1);
            })
            .ToList();
    }

    private static IReadOnlyList<GitChangedFile> ParseCommitFiles(string output)
    {
        var files = new List<GitChangedFile>();
        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var statusCode = parts[0];
            var path = parts.Length >= 3 ? $"{parts[1]} -> {parts[2]}" : parts[1];
            files.Add(new GitChangedFile(statusCode[..1], " ", path, BuildCommitFileStatusLabel(statusCode), canToggleStage: false));
        }

        return files;
    }

    private static string BuildCommitFileStatusLabel(string statusCode)
    {
        return statusCode[0] switch
        {
            'A' => "Added in commit",
            'M' => "Modified in commit",
            'D' => "Deleted in commit",
            'R' => "Renamed in commit",
            'C' => "Copied in commit",
            _ => "Changed in commit"
        };
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
