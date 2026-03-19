using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GitMishigeh.Models;

namespace GitMishigeh.Services;

public sealed class AppPreferencesStore : IAppPreferencesStore
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
    private readonly string _storageFilePath;

    public AppPreferencesStore()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var storageDirectory = Path.Combine(appDataPath, "GitMishigeh");
        _storageFilePath = Path.Combine(storageDirectory, "preferences.json");
    }

    public async Task<AppPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storageFilePath))
        {
            return new AppPreferences();
        }

        try
        {
            await using var stream = File.OpenRead(_storageFilePath);
            return await JsonSerializer.DeserializeAsync<AppPreferences>(stream, JsonSerializerOptions, cancellationToken)
                ?? new AppPreferences();
        }
        catch (IOException)
        {
            return new AppPreferences();
        }
        catch (JsonException)
        {
            return new AppPreferences();
        }
    }

    public async Task SaveAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_storageFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_storageFilePath);
        await JsonSerializer.SerializeAsync(stream, preferences, JsonSerializerOptions, cancellationToken);
    }
}
