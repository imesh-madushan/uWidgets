using Avalonia.Controls;
using Avalonia.Interactivity;
using DataBalance.ViewModels;

namespace DataBalance.Views.States;

public partial class BalanceDashboardView : UserControl
{
    public BalanceDashboardView() => InitializeComponent();

    private void ChooseConnectionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel)
            viewModel.ShowConnectionPicker();
    }

    private async void SignOutButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel)
            await viewModel.SignOutAsync();
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel)
            await viewModel.RefreshAsync();
    }
}
