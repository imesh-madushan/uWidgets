using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

        AttachedToVisualTree += async (_, _) =>
        {
            _refreshTimer.Start();
            if (!_initialized)
            {
                _initialized = true;
                await _viewModel.InitializeAsync();
            }
        };

        DetachedFromVisualTree += (_, _) => _refreshTimer.Stop();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataBalanceViewModel.OtpLength))
            Dispatcher.UIThread.Post(() => BuildOtpBoxes(_viewModel.OtpLength));

        if (e.PropertyName == nameof(DataBalanceViewModel.AuthStatus) &&
            _viewModel.IsOtpVisible && _viewModel.AuthStatus != DialogAuthStatus.SubmittingOtp)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _otpSubmissionStarted = false;
                ClearOtpBoxes();
                _otpBoxes.FirstOrDefault()?.Focus();
            });
        }
    }

    private void BuildOtpBoxes(int count)
    {
        OtpBoxesPanel.Children.Clear();
        _otpBoxes.Clear();

        double width = count <= 6 ? 36 : Math.Max(25, 238d / count);
        for (int index = 0; index < count; index++)
        {
            var box = new TextBox
            {
                Width = width,
                Height = 42,
                FontSize = 17,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
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
                for (int offset = 0; offset < digits.Length && index + offset < _otpBoxes.Count; offset++)
                    _otpBoxes[index + offset].Text = digits[offset].ToString();
                _otpBoxes[Math.Min(index + digits.Length, _otpBoxes.Count) - 1].CaretIndex = 1;
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
        if (e.Key == Key.Back && string.IsNullOrEmpty(box.Text) && index > 0)
        {
            _otpBoxes[index - 1].Focus();
            _otpBoxes[index - 1].Text = "";
        }
        else if (e.Key == Key.Left && index > 0)
        {
            _otpBoxes[index - 1].Focus();
        }
        else if (e.Key == Key.Right && index + 1 < _otpBoxes.Count)
        {
            _otpBoxes[index + 1].Focus();
        }
    }

    private void ClearOtpBoxes()
    {
        _updatingOtpBoxes = true;
        foreach (TextBox box in _otpBoxes) box.Text = "";
        _updatingOtpBoxes = false;
    }

    private async void AccountInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await _viewModel.StartLoginAsync();
    }

    private async void ContinueButton_Click(object? sender, RoutedEventArgs e) =>
        await _viewModel.StartLoginAsync();

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

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();
}
