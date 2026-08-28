using Avalonia.Controls;
using Avalonia.Interactivity;
using DataBalance.Models;
using DataBalance.ViewModels;

namespace DataBalance.Views.States;

public partial class ConnectionPickerView : UserControl
{
    public ConnectionPickerView() => InitializeComponent();

    private async void ConnectionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel &&
            sender is Button { DataContext: DialogConnection connection })
            await viewModel.SelectConnectionAsync(connection);
    }

    private async void SignOutButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel)
            await viewModel.SignOutAsync();
    }
}
