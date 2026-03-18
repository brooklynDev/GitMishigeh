using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace GitMishigeh.Views;

public partial class MainWindow : Window
{
    private double _pendingDiffWheelSteps;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(InputElement.PointerWheelChangedEvent, OnDiffPointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnDiffPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!IsPointerOverDiffTextBox(e))
        {
            return;
        }

        var scrollViewer = DiffTextBox.FindDescendantOfType<ScrollViewer>();
        if (scrollViewer is null || System.Math.Abs(e.Delta.Y) < double.Epsilon)
        {
            return;
        }

        _pendingDiffWheelSteps += -e.Delta.Y * 3;
        var moved = false;

        while (_pendingDiffWheelSteps >= 1)
        {
            scrollViewer.LineDown();
            _pendingDiffWheelSteps -= 1;
            moved = true;
        }

        while (_pendingDiffWheelSteps <= -1)
        {
            scrollViewer.LineUp();
            _pendingDiffWheelSteps += 1;
            moved = true;
        }

        if (!moved)
        {
            var currentOffset = scrollViewer.Offset;
            scrollViewer.Offset = new Avalonia.Vector(currentOffset.X, System.Math.Max(0, currentOffset.Y - (e.Delta.Y * 56)));
            moved = true;
        }

        if (moved)
        {
            e.Handled = true;
        }
    }

    private bool IsPointerOverDiffTextBox(PointerWheelEventArgs e)
    {
        var point = e.GetPosition(DiffTextBox);

        if (point.X < 0 || point.Y < 0)
        {
            return false;
        }

        return point.X <= DiffTextBox.Bounds.Width && point.Y <= DiffTextBox.Bounds.Height;
    }
}
