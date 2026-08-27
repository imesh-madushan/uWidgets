using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DataBalance.Models;
using DataBalance.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace DataBalance.Views;

public partial class DataBalanceView : UserControl
{
    private readonly DataBalanceViewModel _viewModel = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _otpTimer;
    private readonly List<TextBox> _otpBoxes = [];
    private bool _otpSubmissionStarted;
    private bool _updatingOtpBoxes;
    private bool _initialized;

    public DataBalanceView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        BuildOtpBoxes(_viewModel.OtpLength);

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _otpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _otpTimer.Tick += OtpTimer_Tick;

        AttachedToVisualTree += async (_, _) =>
        {
            _refreshTimer.Start();
            _otpTimer.Start();
            if (!_initialized)
            {
                _initialized = true;
                await _viewModel.InitializeAsync();
            }
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _refreshTimer.Stop();
            _otpTimer.Stop();
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataBalanceViewModel.OtpLength))
            Dispatcher.UIThread.Post(() => BuildOtpBoxes(_viewModel.OtpLength));

        if (e.PropertyName == nameof(DataBalanceViewModel.AuthStatus) &&
            _viewModel.IsOtpVisible)
        {
            Dispatcher.UIThread.Post(() =>
            {
                foreach (TextBox box in _otpBoxes)
                    box.IsEnabled = _viewModel.CanEnterOtp;

                if (_viewModel.CanEnterOtp)
                {
                    _otpSubmissionStarted = false;
                    ClearOtpBoxes();
                    _otpBoxes.FirstOrDefault()?.Focus();
                }
            });
        }
    }

    private void BuildOtpBoxes(int count)
    {
        OtpBoxesPanel.Children.Clear();
        _otpBoxes.Clear();

        // Keep the complete code comfortably inside the widget's 276 px content area.
        double width = Math.Clamp((244d - (Math.Max(1, count) - 1) * 4) / Math.Max(1, count), 24, 32);
        for (int index = 0; index < count; index++)
        {
            var box = new TextBox
            {
                Width = width,
                MinWidth = 0,
                Height = 36,
                MinHeight = 0,
                Padding = new Avalonia.Thickness(0),
                FontSize = 16,
                MaxLength = count,
                TextAlignment = TextAlignment.Center,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.Parse("#18FFFFFF")),
                BorderBrush = new SolidColorBrush(Color.Parse("#65FFFFFF")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(6),
                IsEnabled = _viewModel.CanEnterOtp,
                Tag = index
            };
            box.TextChanged += OtpBox_TextChanged;
            box.KeyDown += OtpBox_KeyDown;
            _otpBoxes.Add(box);
            OtpBoxesPanel.Children.Add(box);
        }
    }

    private async void OtpBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingOtpBoxes || sender is not TextBox box || box.Tag is not int index)
            return;

        string digits = new((box.Text ?? "").Where(char.IsDigit).ToArray());
        _updatingOtpBoxes = true;
        try
        {
            if (digits.Length > 1)
            {
                int target = index;
                foreach (char digit in digits)
                {
                    if (target >= _otpBoxes.Count) break;
                    _otpBoxes[target++].Text = digit.ToString();
                }

                int focusIndex = Math.Min(target, _otpBoxes.Count - 1);
                _otpBoxes[focusIndex].Focus();
                _otpBoxes[focusIndex].CaretIndex = _otpBoxes[focusIndex].Text?.Length ?? 0;
            }
            else
            {
                box.Text = digits;
                box.CaretIndex = digits.Length;
                if (digits.Length == 1 && index + 1 < _otpBoxes.Count)
                    _otpBoxes[index + 1].Focus();
            }
        }
        finally
        {
            _updatingOtpBoxes = false;
        }

        string otp = string.Concat(_otpBoxes.Select(item => item.Text ?? ""));
        if (!_otpSubmissionStarted && otp.Length == _otpBoxes.Count && otp.All(char.IsDigit))
        {
            _otpSubmissionStarted = true;
            await _viewModel.SubmitOtpAsync(otp);
            if (_viewModel.AuthStatus is DialogAuthStatus.IncorrectOtp or DialogAuthStatus.OtpExpired or DialogAuthStatus.WaitingForOtp)
            {
                _otpSubmissionStarted = false;
                ClearOtpBoxes();
                _otpBoxes.FirstOrDefault()?.Focus();
            }
        }
    }

    private void OtpBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not int index) return;
        if (e.Key == Key.Back)
        {
            _updatingOtpBoxes = true;
            try
            {
                if (!string.IsNullOrEmpty(box.Text))
                {
                    box.Text = "";
                    box.CaretIndex = 0;
                }
                else if (index > 0)
                {
                    TextBox previous = _otpBoxes[index - 1];
                    previous.Text = "";
                    previous.Focus();
                    previous.CaretIndex = 0;
                }
            }
            finally
            {
                _updatingOtpBoxes = false;
            }

            _otpSubmissionStarted = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Left && index > 0)
        {
            _otpBoxes[index - 1].Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && index + 1 < _otpBoxes.Count)
        {
            _otpBoxes[index + 1].Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _updatingOtpBoxes = true;
            box.Text = "";
            box.CaretIndex = 0;
            _updatingOtpBoxes = false;
            _otpSubmissionStarted = false;
            e.Handled = true;
        }
    }

    private void ClearOtpBoxes()
    {
        _updatingOtpBoxes = true;
        foreach (TextBox box in _otpBoxes) box.Text = "";
        _updatingOtpBoxes = false;
        _otpSubmissionStarted = false;
    }

    private async void AccountInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await _viewModel.StartLoginAsync();
    }

    private async void ContinueButton_Click(object? sender, RoutedEventArgs e) =>
        await _viewModel.StartLoginAsync();

    private async void InstallDependenciesButton_Click(object? sender, RoutedEventArgs e) =>
        await _viewModel.InstallDependenciesAsync();

    private async void CheckDependenciesButton_Click(object? sender, RoutedEventArgs e) =>
        await _viewModel.InitializeAsync();

    private async void CancelOtpButton_Click(object? sender, RoutedEventArgs e) =>
        await _viewModel.CancelLoginAsync();

    private async void ResendOtpButton_Click(object? sender, RoutedEventArgs e)
    {
        _otpSubmissionStarted = false;
        ClearOtpBoxes();
        await _viewModel.ResendOtpAsync();
        _otpBoxes.FirstOrDefault()?.Focus();
    }

    private async void ConnectionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DialogConnection connection })
            await _viewModel.SelectConnectionAsync(connection);
    }

    private void ChooseConnectionButton_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.ShowConnectionPicker();

    private async void SignOutButton_Click(object? sender, RoutedEventArgs e) =>
        await _viewModel.SignOutAsync();

    private async void RefreshTimer_Tick(object? sender, EventArgs e) =>
        await _viewModel.RefreshAsync();

    private async void OtpTimer_Tick(object? sender, EventArgs e) =>
        await _viewModel.UpdateOtpStateAsync();

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();

    private async void ReconnectButton_Click(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();
}
