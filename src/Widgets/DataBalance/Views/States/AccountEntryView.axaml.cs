using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DataBalance.ViewModels;

namespace DataBalance.Views.States;

public partial class AccountEntryView : UserControl
{
    public AccountEntryView() => InitializeComponent();

    private async void AccountInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is DataBalanceViewModel viewModel)
            await viewModel.StartLoginAsync();
    }

    private async void ContinueButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel)
            await viewModel.StartLoginAsync();
    }
}
