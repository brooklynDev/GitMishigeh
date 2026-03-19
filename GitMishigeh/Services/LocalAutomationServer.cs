using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using GitMishigeh.ViewModels;
using GitMishigeh.Views;

namespace GitMishigeh.Services;

public sealed class LocalAutomationServer : IAsyncDisposable
{
    private readonly MainWindow _window;
    private readonly MainWindowViewModel _viewModel;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _serverTask;
    private readonly string _artifactsDirectory;

    public LocalAutomationServer(MainWindow window, MainWindowViewModel viewModel, int port)
    {
        _window = window;
        _viewModel = viewModel;
        Port = port;
        _artifactsDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "artifacts", "automation");
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _serverTask = Task.Run(() => RunAsync(_cancellationTokenSource.Token));
    }

    public int Port { get; }

    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();

        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();

        try
        {
            await _serverTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/health")
            {
                await WriteJsonAsync(context.Response, new
                {
                    ok = true,
                    port = Port,
                    state = await Dispatcher.UIThread.InvokeAsync(BuildState)
                });
                return;
            }

            if (context.Request.HttpMethod != "POST" || context.Request.Url?.AbsolutePath != "/command")
            {
                context.Response.StatusCode = 404;
                await WriteJsonAsync(context.Response, new { ok = false, error = "Not found" });
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("command", out var commandElement) || commandElement.ValueKind != JsonValueKind.String)
            {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context.Response, new { ok = false, error = "Missing command" });
                return;
            }

            var command = commandElement.GetString() ?? string.Empty;
            var result = await ExecuteCommandAsync(command, root);
            await WriteJsonAsync(context.Response, result);
        }
        catch (Exception exception)
        {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context.Response, new
            {
                ok = false,
                error = exception.Message,
                type = exception.GetType().Name
            });
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task<object> ExecuteCommandAsync(string command, JsonElement root)
    {
        switch (command)
        {
            case "ping":
                return new { ok = true, pong = true };
            case "get_state":
                return new { ok = true, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            case "open_repo":
            {
                var path = GetRequiredString(root, "path");
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationOpenRepositoryAsync(path));
                return new { ok = true, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            }
            case "refresh":
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationRefreshAsync());
                return new { ok = true, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            case "show_working_tree":
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationShowWorkingTreeAsync());
                return new { ok = true, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            case "show_history":
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationShowHistoryAsync());
                return new { ok = true, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            case "select_recent_repository":
            {
                var value = GetRequiredString(root, "value");
                var selected = await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationSelectRecentRepositoryAsync(value));
                return new { ok = selected, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            }
            case "select_changed_file":
            {
                var path = GetRequiredString(root, "path");
                var selected = await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationSelectVisibleFileAsync(path));
                return new { ok = selected, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            }
            case "select_commit":
            {
                var shortHash = GetRequiredString(root, "short_hash");
                var selected = await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationSelectCommitAsync(shortHash));
                return new { ok = selected, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            }
            case "set_commit_message":
            {
                var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString() ?? string.Empty
                    : string.Empty;
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationSetCommitMessage(message));
                return new { ok = true, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            }
            case "stage_all":
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationStageAllAsync());
                return new { ok = true, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            case "commit":
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationCommitAsync());
                return new { ok = true, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            case "push":
                await Dispatcher.UIThread.InvokeAsync(() => _viewModel.AutomationPushAsync());
                return new { ok = true, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            case "capture_screenshot":
            {
                var name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
                var screenshotPath = await CaptureScreenshotAsync(name);
                return new { ok = true, path = screenshotPath, state = await Dispatcher.UIThread.InvokeAsync(BuildState) };
            }
            default:
                throw new InvalidOperationException($"Unknown automation command '{command}'.");
        }
    }

    private object BuildState()
    {
        var files = new List<object>();
        foreach (var file in _viewModel.VisibleFiles)
        {
            files.Add(new
            {
                path = file.Path,
                diffPath = file.DiffPath,
                status = file.StatusCode,
                label = file.StatusLabel,
                staged = file.IsStaged
            });
        }

        var commits = new List<object>();
        foreach (var commit in _viewModel.RecentCommits)
        {
            commits.Add(new
            {
                shortHash = commit.ShortHash,
                authorName = commit.AuthorName,
                message = commit.Message
            });
        }

        var recentRepositories = new List<object>();
        foreach (var repository in _viewModel.RecentRepositories)
        {
            recentRepositories.Add(new
            {
                path = repository.Path,
                displayName = repository.DisplayName
            });
        }

        return new
        {
            repositoryPath = _viewModel.RepositoryPath,
            currentBranch = _viewModel.CurrentBranch,
            statusSummary = _viewModel.StatusSummary,
            outputMessage = _viewModel.OutputMessage,
            selectedDiffHeader = _viewModel.SelectedDiffHeader,
            selectedDiff = _viewModel.SelectedDiff,
            commitMessage = _viewModel.CommitMessage,
            isBusy = _viewModel.IsBusy,
            isHistoryMode = _viewModel.IsShowingCommitHistory,
            selectedFile = _viewModel.SelectedFileEntry?.Path,
            selectedCommit = _viewModel.SelectedCommit?.ShortHash,
            visibleFiles = files,
            recentCommits = commits,
            recentRepositories = recentRepositories,
            diffSectionCount = _viewModel.DiffSections.Count
        };
    }

    private Task<string> CaptureScreenshotAsync(string? name)
    {
        Directory.CreateDirectory(_artifactsDirectory);

        var fileName = string.IsNullOrWhiteSpace(name)
            ? $"screenshot-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png"
            : $"{SanitizeFileName(name)}.png";

        var fullPath = Path.GetFullPath(Path.Combine(_artifactsDirectory, fileName));
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? CaptureScreenshotOnMacAsync(fullPath)
            : CaptureScreenshotWithAvaloniaAsync(fullPath);
    }

    private async Task<string> CaptureScreenshotOnMacAsync(string fullPath)
    {
        var windowNumber = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _window.Activate();
            _window.InvalidateVisual();
            return GetMacWindowNumber(_window);
        });

        await Task.Delay(150);

        var arguments = $"-x -l {windowNumber} \"{fullPath}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/sbin/screencapture",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start macOS screencapture.");

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"screencapture failed with exit code {process.ExitCode}: {error}".Trim());
        }

        return fullPath;
    }

    private static nint GetMacWindowNumber(TopLevel topLevel)
    {
        var platformHandle = topLevel.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("Unable to get the native handle for the automation window.");

        var nativeHandle = platformHandle.Handle;
        if (nativeHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The automation window does not have a valid native handle.");
        }

        var descriptor = platformHandle.HandleDescriptor ?? string.Empty;
        var windowHandle = descriptor.Contains("NSView", StringComparison.OrdinalIgnoreCase)
            ? SendIntPtrMessage(nativeHandle, "window")
            : nativeHandle;

        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to resolve the macOS window handle for automation screenshots.");
        }

        var windowNumber = SendNIntMessage(windowHandle, "windowNumber");
        if (windowNumber <= 0)
        {
            throw new InvalidOperationException("Unable to resolve the macOS window number for automation screenshots.");
        }

        return windowNumber;
    }

    private static IntPtr SendIntPtrMessage(IntPtr receiver, string selectorName)
    {
        var selector = sel_registerName(selectorName);
        return IntPtr_objc_msgSend(receiver, selector);
    }

    private static nint SendNIntMessage(IntPtr receiver, string selectorName)
    {
        var selector = sel_registerName(selectorName);
        return nint_objc_msgSend(receiver, selector);
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint nint_objc_msgSend(IntPtr receiver, IntPtr selector);

    private async Task<string> CaptureScreenshotWithAvaloniaAsync(string fullPath)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _window.InvalidateVisual();

            var scale = _window.RenderScaling;
            var pixelSize = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(_window.Bounds.Width * scale)),
                Math.Max(1, (int)Math.Ceiling(_window.Bounds.Height * scale)));

            using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(scale, scale));
            bitmap.Render(_window);
            bitmap.Save(fullPath);
        });

        return fullPath;
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Missing required string property '{propertyName}'.");
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Property '{propertyName}' cannot be empty.");
        }

        return value;
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            builder.Append(Array.IndexOf(invalidChars, character) >= 0 ? '-' : character);
        }

        return builder.ToString();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload)
    {
        response.ContentType = "application/json";
        response.ContentEncoding = Encoding.UTF8;

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await using var writer = new StreamWriter(response.OutputStream, Encoding.UTF8, leaveOpen: true);
        await writer.WriteAsync(json);
        await writer.FlushAsync();
    }
}
