using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GitMishigeh.Models;

namespace GitMishigeh.Services;

public sealed class RecentRepositoryStore : IRecentRepositoryStore
{
    private const int MaxRepositories = 12;
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
    private readonly string _storageFilePath;

    public RecentRepositoryStore()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var storageDirectory = Path.Combine(appDataPath, "GitMishigeh");
        _storageFilePath = Path.Combine(storageDirectory, "recent-repositories.json");
    }

    public async Task<IReadOnlyList<RecentRepository>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storageFilePath))
        {
            return Array.Empty<RecentRepository>();
        }

        try
        {
            await using var stream = File.OpenRead(_storageFilePath);
            var repositories = await JsonSerializer.DeserializeAsync<List<RecentRepository>>(stream, JsonSerializerOptions, cancellationToken);

            return repositories?
                .Where(repository => Directory.Exists(repository.Path))
                .OrderByDescending(repository => repository.LastOpenedUtc)
                .ToList() ?? new List<RecentRepository>();
        }
        catch (IOException)
        {
            return Array.Empty<RecentRepository>();
        }
        catch (JsonException)
        {
            return Array.Empty<RecentRepository>();
        }
    }

    public async Task<IReadOnlyList<RecentRepository>> AddOrUpdateAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var repositories = (await LoadAsync(cancellationToken)).ToList();
        repositories.RemoveAll(repository => string.Equals(repository.Path, repositoryPath, StringComparison.Ordinal));

        repositories.Insert(0, new RecentRepository(
            repositoryPath,
            Path.GetFileName(repositoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            DateTime.UtcNow));

        var trimmedRepositories = repositories.Take(MaxRepositories).ToList();
        await SaveAsync(trimmedRepositories, cancellationToken);
        return trimmedRepositories;
    }

    private async Task SaveAsync(IReadOnlyList<RecentRepository> repositories, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storageFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_storageFilePath);
        await JsonSerializer.SerializeAsync(stream, repositories, JsonSerializerOptions, cancellationToken);
    }
}
