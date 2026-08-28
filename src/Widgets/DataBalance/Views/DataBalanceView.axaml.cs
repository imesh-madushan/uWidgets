using Avalonia.Controls;
using Avalonia.Threading;
using DataBalance.ViewModels;
using System;

namespace DataBalance.Views;

/// <summary>
/// Hosts the Data Balance state views and manages application-wide refresh timers.
/// Each state's UI and user interactions live in the corresponding view under Views/States.
/// </summary>
public partial class DataBalanceView : UserControl
{
    private readonly DataBalanceViewModel _viewModel = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _otpTimer;
    private bool _initialized;

    public DataBalanceView()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += RefreshTimer_Tick;

        _otpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _otpTimer.Tick += OtpTimer_Tick;

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer.Start();
        _otpTimer.Start();

        if (!_initialized)
        {
            _initialized = true;
            await _viewModel.InitializeAsync();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer.Stop();
        _otpTimer.Stop();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e) =>
        await _viewModel.RefreshAsync();

    private async void OtpTimer_Tick(object? sender, EventArgs e) =>
        await _viewModel.UpdateOtpStateAsync();
}
