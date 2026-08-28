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

namespace DataBalance.Views.States;

public partial class OtpVerificationView : UserControl
{
    private readonly List<TextBox> _otpBoxes = [];
    private DataBalanceViewModel? _viewModel;
    private bool _submissionStarted;
    private bool _updatingBoxes;

    public OtpVerificationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        _viewModel = DataContext as DataBalanceViewModel;
        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        BuildOtpBoxes(_viewModel.OtpLength);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
            return;

        if (e.PropertyName == nameof(DataBalanceViewModel.OtpLength))
            Dispatcher.UIThread.Post(() => BuildOtpBoxes(_viewModel.OtpLength));

        if (e.PropertyName == nameof(DataBalanceViewModel.AuthStatus) && _viewModel.IsOtpVisible)
            Dispatcher.UIThread.Post(ResetForAuthenticationState);
    }

    private void ResetForAuthenticationState()
    {
        if (_viewModel is null)
            return;

        foreach (TextBox box in _otpBoxes)
            box.IsEnabled = _viewModel.CanEnterOtp;

        if (_viewModel.CanEnterOtp)
        {
            ClearOtpBoxes();
            _otpBoxes.FirstOrDefault()?.Focus();
        }
    }

    private void BuildOtpBoxes(int count)
    {
        OtpBoxesPanel.Children.Clear();
        _otpBoxes.Clear();

        const double spacing = 8;
        double width = Math.Clamp((280d - (Math.Max(1, count) - 1) * spacing) / Math.Max(1, count), 28, 40);
        for (int index = 0; index < count; index++)
        {
            var box = new TextBox
            {
                Width = width,
                MinWidth = 0,
                Height = 40,
                MinHeight = 0,
                Padding = new Avalonia.Thickness(0),
                FontSize = 16,
                MaxLength = count,
                TextAlignment = TextAlignment.Center,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#E7B6EF")),
                Background = new SolidColorBrush(Color.Parse("#16FFFFFF")),
                BorderBrush = new SolidColorBrush(Color.Parse("#42FFFFFF")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(7),
                IsEnabled = _viewModel?.CanEnterOtp ?? false,
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
        if (_updatingBoxes || _viewModel is null || sender is not TextBox box || box.Tag is not int index)
            return;

        string digits = new((box.Text ?? "").Where(char.IsDigit).ToArray());
        _updatingBoxes = true;
        try
        {
            if (digits.Length > 1)
            {
                int target = index;
                foreach (char digit in digits)
                    if (target < _otpBoxes.Count)
                        _otpBoxes[target++].Text = digit.ToString();

                _otpBoxes[Math.Min(target, _otpBoxes.Count - 1)].Focus();
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
            _updatingBoxes = false;
        }

        string otp = string.Concat(_otpBoxes.Select(item => item.Text ?? ""));
        if (!_submissionStarted && otp.Length == _otpBoxes.Count && otp.All(char.IsDigit))
        {
            _submissionStarted = true;
            await _viewModel.SubmitOtpAsync(otp);
            if (_viewModel.AuthStatus is DialogAuthStatus.IncorrectOtp or DialogAuthStatus.OtpExpired or DialogAuthStatus.WaitingForOtp)
            {
                ClearOtpBoxes();
                _otpBoxes.FirstOrDefault()?.Focus();
            }
        }
    }

    private void OtpBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not int index)
            return;

        if (e.Key == Key.Back)
        {
            _updatingBoxes = true;
            if (!string.IsNullOrEmpty(box.Text))
                box.Text = "";
            else if (index > 0)
            {
                _otpBoxes[index - 1].Text = "";
                _otpBoxes[index - 1].Focus();
            }
            _updatingBoxes = false;
            _submissionStarted = false;
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
    }

    private void ClearOtpBoxes()
    {
        _updatingBoxes = true;
        foreach (TextBox box in _otpBoxes)
            box.Text = "";
        _updatingBoxes = false;
        _submissionStarted = false;
    }

    private async void CancelOtpButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            await _viewModel.CancelLoginAsync();
    }

    private async void ResendOtpButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;

        ClearOtpBoxes();
        await _viewModel.ResendOtpAsync();
        _otpBoxes.FirstOrDefault()?.Focus();
    }
}
