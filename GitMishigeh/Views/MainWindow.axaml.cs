using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace GitMishigeh.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnDiffViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DiffScrollViewer is null)
        {
            return;
        }

        var currentOffset = DiffScrollViewer.Offset;
        var maxOffsetY = Math.Max(0, DiffScrollViewer.Extent.Height - DiffScrollViewer.Viewport.Height);
        var nextOffsetY = Math.Clamp(currentOffset.Y - (e.Delta.Y * 56), 0, maxOffsetY);

        DiffScrollViewer.Offset = new Vector(currentOffset.X, nextOffsetY);
        e.Handled = true;
    }
}
