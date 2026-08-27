using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DataBalance.ViewModels;
using System;

namespace DataBalance.Views;

public partial class DataBalanceView : UserControl
{
    private readonly DataBalanceViewModel _viewModel = new();
    private readonly DispatcherTimer _refreshTimer;

    public DataBalanceView()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };

        _refreshTimer.Tick += RefreshTimer_Tick;

        AttachedToVisualTree += async (_, _) =>
        {
            _refreshTimer.Start();
            await _viewModel.RefreshAsync();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _refreshTimer.Stop();
        };
    }

    private async void RefreshTimer_Tick(
        object? sender,
        EventArgs e)
    {
        await _viewModel.RefreshAsync();
    }

    private async void RefreshButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync();
    }
}