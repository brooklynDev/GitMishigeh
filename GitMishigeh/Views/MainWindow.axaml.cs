using Avalonia.Controls;
using Avalonia.Input;
using GitMishigeh.Models;
using GitMishigeh.ViewModels;

namespace GitMishigeh.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
}
