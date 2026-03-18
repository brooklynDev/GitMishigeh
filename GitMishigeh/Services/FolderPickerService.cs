using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace GitMishigeh.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            return null;
        }

        var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Open Git Repository",
                AllowMultiple = false
            });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
