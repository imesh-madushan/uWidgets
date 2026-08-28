using Avalonia.Controls;
using Avalonia.Interactivity;
using DataBalance.ViewModels;

namespace DataBalance.Views.States;

public partial class ConnectionErrorView : UserControl
{
    public ConnectionErrorView() => InitializeComponent();

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel)
            await viewModel.RefreshAsync();
    }

    private async void SignOutButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel)
            await viewModel.SignOutAsync();
    }
}
