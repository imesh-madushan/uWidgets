using Avalonia.Controls;
using Avalonia.Interactivity;
using DataBalance.ViewModels;

namespace DataBalance.Views.States;

public partial class DependencySetupView : UserControl
{
    public DependencySetupView() => InitializeComponent();

    private async void InstallDependenciesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel)
            await viewModel.InstallDependenciesAsync();
    }

    private async void CheckDependenciesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DataBalanceViewModel viewModel)
            await viewModel.InitializeAsync();
    }
}
