using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GitMishigeh.Models;
using GitMishigeh.ViewModels;
using System.ComponentModel;

namespace GitMishigeh.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _paneWidthSyncTimer;
    private MainWindowViewModel? _subscribedViewModel;
    private bool _isApplyingPaneWidthsFromViewModel;
    private ColumnDefinition NavigationPaneColumn => WorkspaceGrid.ColumnDefinitions[0];
    private ColumnDefinition FilePaneColumn => WorkspaceGrid.ColumnDefinitions[2];

    public MainWindow()
    {
        InitializeComponent();

        _paneWidthSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _paneWidthSyncTimer.Tick += PaneWidthSyncTimerOnTick;
        Opened += MainWindow_OnOpened;
        DataContextChanged += MainWindow_OnDataContextChanged;

        NavigationPaneColumn.PropertyChanged += PaneColumnOnPropertyChanged;
        FilePaneColumn.PropertyChanged += PaneColumnOnPropertyChanged;
    }

    public double ActualNavigationPaneWidth => NavigationPaneColumn.ActualWidth;

    public double ActualFilePaneWidth => FilePaneColumn.ActualWidth;

    public void ApplyPaneWidths(double navigationPaneWidth, double filePaneWidth)
    {
        _isApplyingPaneWidthsFromViewModel = true;

        try
        {
            NavigationPaneColumn.Width = new GridLength(Math.Max(NavigationPaneColumn.MinWidth, navigationPaneWidth));
            FilePaneColumn.Width = new GridLength(Math.Max(FilePaneColumn.MinWidth, filePaneWidth));
        }
        finally
        {
            _isApplyingPaneWidthsFromViewModel = false;
        }
    }

    private async void BranchListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: GitBranchItem branch })
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!viewModel.CheckoutBranchCommand.CanExecute(branch))
        {
            return;
        }

        await viewModel.CheckoutBranchCommand.ExecuteAsync(branch);
    }

    private void MainWindow_OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        ApplyPaneWidths(viewModel.NavigationPaneWidth, viewModel.FilePaneWidth);
    }

    private void MainWindow_OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _subscribedViewModel = viewModel;
        _subscribedViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        ApplyPaneWidths(viewModel.NavigationPaneWidth, viewModel.FilePaneWidth);
    }

    private void PaneColumnOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ColumnDefinition.WidthProperty)
        {
            return;
        }

        if (_isApplyingPaneWidthsFromViewModel)
        {
            return;
        }

        _paneWidthSyncTimer.Stop();
        _paneWidthSyncTimer.Start();
    }

    private void PaneWidthSyncTimerOnTick(object? sender, EventArgs e)
    {
        _paneWidthSyncTimer.Stop();

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var leftWidth = Math.Max(NavigationPaneColumn.MinWidth, NavigationPaneColumn.ActualWidth);
        var middleWidth = Math.Max(FilePaneColumn.MinWidth, FilePaneColumn.ActualWidth);
        viewModel.AutomationSetPaneWidths(leftWidth, middleWidth);
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.NavigationPaneWidth) or nameof(MainWindowViewModel.FilePaneWidth))
        {
            ApplyPaneWidths(viewModel.NavigationPaneWidth, viewModel.FilePaneWidth);
        }
    }
}
