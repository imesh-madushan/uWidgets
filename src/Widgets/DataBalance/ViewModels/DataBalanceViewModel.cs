using DataBalance.Models;
using DataBalance.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DataBalance.ViewModels;

public sealed partial class DataBalanceViewModel : INotifyPropertyChanged
{
    private static readonly DialogCredentialVault Vault = new();
    private static readonly DialogAuthenticationService Authentication = new();
    private static readonly DialogApiService Api = new();
    private static readonly SemaphoreSlim OperationGate = new(1, 1);
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    private static DialogCredentialState? _credentialState;
    private DialogAuthStatus _authStatus = DialogAuthStatus.NoAccount;
    private string _accountIdentifier = "";
    private string _statusMessage = "Sign in to view your Dialog balance";
    private int _otpLength = 6;
    private bool _isRefreshing;
    private bool _isDependencyPromptVisible;
    private bool _isInstallingDependencies;
    private bool _isUpdatingOtpState;
    private DateTimeOffset? _otpExpiresAt;
    private DateTimeOffset? _resendAvailableAt;
    private string _activeChallengeIdentifier = "";
    private bool _isReconnectVisible;
    private string _otpCountdown = "";
    private string _resendCountdown = "";
    private string _connectionStatusColor = "#54D98C";
    private string _connectionName = "Dialog";
    private string _phoneNumber = "Not signed in";
    private string _connectionKind = "Connection";
    private string _connectionStatus = "Disconnected";
    private string _balance = "--";
    private string _validity = "Sign in required";
    private string _packageOneType = "DATA";
    private string _packageOneName = "No package data";
    private string _packageOneRemaining = "--";
    private string _packageOneExpiry = "";
    private double _packageOnePercentage;
    private string _packageTwoType = "DATA";
    private string _packageTwoName = "No package data";
    private string _packageTwoRemaining = "--";
    private string _packageTwoExpiry = "";
    private double _packageTwoPercentage;
    private string _updatedText = "Waiting for sign in";

    public ObservableCollection<DialogConnection> Connections { get; } = [];

    public DialogAuthStatus AuthStatus
    {
        get => _authStatus;
        private set
        {
            if (!SetField(ref _authStatus, value)) return;
            OnPropertyChanged(nameof(IsAccountEntryVisible));
            OnPropertyChanged(nameof(IsOtpVisible));
            OnPropertyChanged(nameof(IsBalanceVisible));
            OnPropertyChanged(nameof(IsConnectionPickerVisible));
            OnPropertyChanged(nameof(IsErrorVisible));
            OnPropertyChanged(nameof(CanResendOtp));
            OnPropertyChanged(nameof(IsOtpBusy));
            OnPropertyChanged(nameof(IsOtpError));
            OnPropertyChanged(nameof(OtpStatusColor));
            OnPropertyChanged(nameof(CanEnterOtp));
        }
    }

    static DataBalanceViewModel()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Authentication.CancelAsync().GetAwaiter().GetResult(); }
            catch { }
        };
    }

    public bool IsAccountEntryVisible =>
        !IsDependencyPromptVisible && AuthStatus is (DialogAuthStatus.NoAccount or DialogAuthStatus.EnteringAccount);
    public bool IsOtpVisible => AuthStatus is DialogAuthStatus.RequestingOtp or DialogAuthStatus.WaitingForOtp or
        DialogAuthStatus.SubmittingOtp or DialogAuthStatus.IncorrectOtp or DialogAuthStatus.OtpExpired;
    public bool IsBalanceVisible => AuthStatus == DialogAuthStatus.Authenticated;
    public bool IsConnectionPickerVisible => AuthStatus == DialogAuthStatus.ChoosingConnection;
    public bool IsErrorVisible => AuthStatus == DialogAuthStatus.Error;
    public bool IsOtpBusy => AuthStatus is DialogAuthStatus.RequestingOtp or DialogAuthStatus.SubmittingOtp;
    public bool IsOtpError => AuthStatus is DialogAuthStatus.IncorrectOtp or DialogAuthStatus.OtpExpired;
    public string OtpStatusColor => IsOtpError ? "#FF8585" : IsOtpBusy ? "#60CDFF" : "#B8FFFFFF";
    public bool CanEnterOtp => AuthStatus is DialogAuthStatus.WaitingForOtp or DialogAuthStatus.IncorrectOtp;
    public bool CanResendOtp =>
        AuthStatus is (DialogAuthStatus.WaitingForOtp or DialogAuthStatus.IncorrectOtp or DialogAuthStatus.OtpExpired) &&
        (!_resendAvailableAt.HasValue || DateTimeOffset.Now >= _resendAvailableAt.Value);
    public string OtpCountdown { get => _otpCountdown; private set => SetField(ref _otpCountdown, value); }
    public string ResendCountdown { get => _resendCountdown; private set => SetField(ref _resendCountdown, value); }
    public bool IsReconnectVisible { get => _isReconnectVisible; private set => SetField(ref _isReconnectVisible, value); }
    public string ConnectionStatusColor { get => _connectionStatusColor; private set => SetField(ref _connectionStatusColor, value); }
    public bool IsDependencyPromptVisible
    {
        get => _isDependencyPromptVisible;
        private set
        {
            if (!SetField(ref _isDependencyPromptVisible, value)) return;
            OnPropertyChanged(nameof(IsAccountEntryVisible));
        }
    }

    public bool IsInstallingDependencies
    {
        get => _isInstallingDependencies;
        private set => SetField(ref _isInstallingDependencies, value);
    }

    public string AccountIdentifier
    {
        get => _accountIdentifier;
        set => SetField(ref _accountIdentifier, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public int OtpLength
    {
        get => _otpLength;
        private set => SetField(ref _otpLength, value);
    }

    public string ConnectionName { get => _connectionName; private set => SetField(ref _connectionName, value); }
    public string PhoneNumber { get => _phoneNumber; private set => SetField(ref _phoneNumber, value); }
    public string ConnectionKind { get => _connectionKind; private set => SetField(ref _connectionKind, value); }
    public string ConnectionStatus { get => _connectionStatus; private set => SetField(ref _connectionStatus, value); }
    public string Balance { get => _balance; private set => SetField(ref _balance, value); }
    public string Validity { get => _validity; private set => SetField(ref _validity, value); }
    public string PackageOneType { get => _packageOneType; private set => SetField(ref _packageOneType, value); }
    public string PackageOneName { get => _packageOneName; private set => SetField(ref _packageOneName, value); }
    public string PackageOneRemaining { get => _packageOneRemaining; private set => SetField(ref _packageOneRemaining, value); }
    public string PackageOneExpiry { get => _packageOneExpiry; private set => SetField(ref _packageOneExpiry, value); }
    public double PackageOnePercentage { get => _packageOnePercentage; private set => SetField(ref _packageOnePercentage, value); }
    public string PackageTwoType { get => _packageTwoType; private set => SetField(ref _packageTwoType, value); }
    public string PackageTwoName { get => _packageTwoName; private set => SetField(ref _packageTwoName, value); }
    public string PackageTwoRemaining { get => _packageTwoRemaining; private set => SetField(ref _packageTwoRemaining, value); }
    public string PackageTwoExpiry { get => _packageTwoExpiry; private set => SetField(ref _packageTwoExpiry, value); }
    public double PackageTwoPercentage { get => _packageTwoPercentage; private set => SetField(ref _packageTwoPercentage, value); }
    public string UpdatedText { get => _updatedText; private set => SetField(ref _updatedText, value); }
    public bool IsRefreshing { get => _isRefreshing; private set => SetField(ref _isRefreshing, value); }

    public async Task InitializeAsync()
    {
        try
        {
            if (!await Authentication.AreDependenciesInstalledAsync())
            {
                IsDependencyPromptVisible = true;
                StatusMessage = "Chromium is not installed yet.";
                return;
            }
        }
        catch (Exception exception)
        {
            IsDependencyPromptVisible = true;
            StatusMessage = GetDependencyErrorMessage(exception, "Playwright could not start");
            return;
        }

        IsDependencyPromptVisible = false;
        await LoadSavedAccountAsync();
    }

    public async Task InstallDependenciesAsync()
    {
        if (IsInstallingDependencies) return;
        try
        {
            IsInstallingDependencies = true;
            StatusMessage = "Downloading Chromium…";
            await Authentication.InstallDependenciesAsync();
            IsDependencyPromptVisible = false;
            StatusMessage = "Chromium installed.";
            await LoadSavedAccountAsync();
        }
        catch (Exception exception)
        {
            IsDependencyPromptVisible = true;
            StatusMessage = GetDependencyErrorMessage(exception, "Chromium installation failed");
        }
        finally
        {
            IsInstallingDependencies = false;
        }
    }

    private async Task LoadSavedAccountAsync()
    {
        try
        {
            _credentialState = await Vault.LoadAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Saved login could not be opened: {exception.Message}";
            AuthStatus = DialogAuthStatus.NoAccount;
            return;
        }

        if (_credentialState is null || string.IsNullOrWhiteSpace(_credentialState.StorageStateJson))
        {
            AuthStatus = DialogAuthStatus.NoAccount;
            return;
        }

        AccountIdentifier = _credentialState.LoginIdentifier;
        if (_credentialState.CachedSnapshot is not null)
        {
            ReplaceConnections(_credentialState.Connections);
            ApplySnapshot(_credentialState.CachedSnapshot);
        }
        await RefreshAsync();
    }

    private static string GetDependencyErrorMessage(Exception exception, string fallback)
    {
        if (exception.Message.Contains("Driver not found", StringComparison.OrdinalIgnoreCase))
            return "Playwright driver files are missing. Rebuild the Data Balance widget.";

        return exception.Message.Length <= 90
            ? $"{fallback}: {exception.Message}"
            : $"{fallback}. Please try again.";
    }

    public async Task StartLoginAsync()
    {
        string identifier = AccountIdentifier.Trim();
        if (!IsValidIdentifier(identifier))
        {
            StatusMessage = "Enter a valid email address or Dialog mobile number.";
            AuthStatus = DialogAuthStatus.EnteringAccount;
            return;
        }

        if (HasActiveChallenge(identifier))
        {
            AuthStatus = DialogAuthStatus.WaitingForOtp;
            StatusMessage = "A code was already sent to this account. Enter that code below.";
            UpdateOtpCountdown();
            return;
        }

        await OperationGate.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_activeChallengeIdentifier))
                await Authentication.CancelAsync();
            AuthStatus = DialogAuthStatus.RequestingOtp;
            StatusMessage = "Requesting verification code…";
            DialogOtpChallenge challenge = await Authentication.StartOtpAsync(identifier);
            OtpLength = challenge.Length;
            AccountIdentifier = identifier;
            BeginOtpChallenge(identifier);
            AuthStatus = DialogAuthStatus.WaitingForOtp;
            StatusMessage = $"Enter the {OtpLength}-digit code sent by Dialog.";
        }
        catch (Exception exception)
        {
            AuthStatus = DialogAuthStatus.EnteringAccount;
            StatusMessage = $"Unable to request a code: {exception.Message}";
        }
        finally
        {
            OperationGate.Release();
        }
    }

    public async Task SubmitOtpAsync(string otp)
    {
        if (AuthStatus == DialogAuthStatus.SubmittingOtp) return;
        await OperationGate.WaitAsync();
        try
        {
            AuthStatus = DialogAuthStatus.SubmittingOtp;
            StatusMessage = "Signing in…";
            DialogOtpResult result = await Authentication.SubmitOtpAsync(otp);

            if (result.Kind != DialogOtpResultKind.Success || result.StorageStateJson is null)
            {
                if (result.Kind == DialogOtpResultKind.SessionLost)
                {
                    ClearOtpChallenge();
                    AuthStatus = DialogAuthStatus.EnteringAccount;
                    StatusMessage = result.Message;
                    return;
                }

                AuthStatus = result.Kind switch
                {
                    DialogOtpResultKind.Incorrect => DialogAuthStatus.IncorrectOtp,
                    DialogOtpResultKind.Expired => DialogAuthStatus.OtpExpired,
                    _ => DialogAuthStatus.WaitingForOtp
                };
                if (result.Kind == DialogOtpResultKind.Expired)
                {
                    _resendAvailableAt = DateTimeOffset.Now;
                    UpdateOtpCountdown();
                }
                StatusMessage = result.Message;
                return;
            }

            string previousSelection = _credentialState?.SelectedConnection ?? "";
            _credentialState = new DialogCredentialState
            {
                LoginIdentifier = AccountIdentifier.Trim(),
                StorageStateJson = result.StorageStateJson,
                SelectedConnection = previousSelection,
                Connections = _credentialState?.Connections ?? [],
                CachedSnapshot = _credentialState?.CachedSnapshot
            };
            ClearOtpChallenge();
            await Vault.SaveAsync(_credentialState);
            await RefreshCoreAsync(showPickerAfterFirstLogin: string.IsNullOrWhiteSpace(previousSelection));
        }
        catch (Exception exception)
        {
            AuthStatus = DialogAuthStatus.WaitingForOtp;
            StatusMessage = $"Sign in failed: {exception.Message}";
        }
        finally
        {
            OperationGate.Release();
        }
    }

    public async Task ResendOtpAsync()
    {
        if (!CanResendOtp) return;
        await OperationGate.WaitAsync();
        try
        {
            AuthStatus = DialogAuthStatus.RequestingOtp;
            StatusMessage = "Requesting a new code…";
            await Authentication.ResendOtpAsync();
            _otpExpiresAt = DateTimeOffset.Now.Add(OtpLifetime);
            _resendAvailableAt = DateTimeOffset.Now.Add(ResendCooldown);
            UpdateOtpCountdown();
            AuthStatus = DialogAuthStatus.WaitingForOtp;
            StatusMessage = "A new verification code was sent.";
        }
        catch (Exception exception)
        {
            try { await Authentication.CancelAsync(); }
            catch { }
            ClearOtpChallenge();
            AuthStatus = DialogAuthStatus.EnteringAccount;
            StatusMessage = $"The code could not be resent. Start sign-in again. {exception.Message}";
        }
        finally
        {
            OperationGate.Release();
        }
    }

    public Task CancelLoginAsync()
    {
        AuthStatus = DialogAuthStatus.EnteringAccount;
        StatusMessage = HasActiveChallenge(AccountIdentifier.Trim())
            ? "A code is still active for this account. Continue again to enter it."
            : "Change the email address or mobile number and try again.";
        return Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        if (IsOtpVisible) return;
        if (_credentialState is null)
        {
            ResetDisplay();
            AuthStatus = DialogAuthStatus.NoAccount;
            return;
        }
        await OperationGate.WaitAsync();
        try
        {
            await RefreshCoreAsync(showPickerAfterFirstLogin: false);
        }
        finally
        {
            OperationGate.Release();
        }
    }

    private async Task RefreshCoreAsync(bool showPickerAfterFirstLogin)
    {
        if (_credentialState is null) return;
        try
        {
            IsRefreshing = true;
            UpdatedText = "Updating…";
            DialogFetchResult result = await Api.FetchAsync(
                _credentialState.StorageStateJson,
                _credentialState.SelectedConnection);

            string selected = result.Snapshot.Msisdn;
            _credentialState = _credentialState with
            {
                StorageStateJson = result.StorageStateJson,
                Connections = result.Connections,
                SelectedConnection = selected,
                CachedSnapshot = result.Snapshot
            };
            await Vault.SaveAsync(_credentialState);
            ReplaceConnections(result.Connections);
            ApplySnapshot(result.Snapshot);
            SetConnected(true);

            if (showPickerAfterFirstLogin && result.Connections.Count > 1)
            {
                AuthStatus = DialogAuthStatus.ChoosingConnection;
                StatusMessage = "Choose the connection to display.";
            }
            else
            {
                AuthStatus = DialogAuthStatus.Authenticated;
            }
        }
        catch (DialogAuthenticationExpiredException)
        {
            try
            {
                await RecoverExpiredSessionAsync();
            }
            catch (Exception exception)
            {
                AuthStatus = DialogAuthStatus.Error;
                StatusMessage = $"Session renewal failed: {exception.Message}";
                UpdatedText = StatusMessage;
            }
        }
        catch (Exception exception)
        {
            SetConnected(false);
            UpdatedText = $"Refresh failed: {exception.Message}";
            if (_credentialState?.CachedSnapshot is not null)
            {
                ApplySnapshot(_credentialState.CachedSnapshot);
                SetConnected(false);
                AuthStatus = DialogAuthStatus.Authenticated;
            }
            else if (AuthStatus != DialogAuthStatus.Authenticated)
            {
                AuthStatus = DialogAuthStatus.Error;
                StatusMessage = UpdatedText;
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task RecoverExpiredSessionAsync()
    {
        if (_credentialState is null) return;
        StatusMessage = "Renewing your Dialog session…";
        string? renewedState = await Authentication.TrySilentRenewAsync(
            _credentialState.StorageStateJson,
            _credentialState.SelectedConnection);

        if (renewedState is not null)
        {
            _credentialState = _credentialState with { StorageStateJson = renewedState };
            await Vault.SaveAsync(_credentialState);
            DialogFetchResult retry = await Api.FetchAsync(renewedState, _credentialState.SelectedConnection);
            _credentialState = _credentialState with
            {
                StorageStateJson = retry.StorageStateJson,
                Connections = retry.Connections,
                SelectedConnection = retry.Snapshot.Msisdn,
                CachedSnapshot = retry.Snapshot
            };
            await Vault.SaveAsync(_credentialState);
            ReplaceConnections(retry.Connections);
            ApplySnapshot(retry.Snapshot);
            SetConnected(true);
            AuthStatus = DialogAuthStatus.Authenticated;
            return;
        }

        AccountIdentifier = _credentialState.LoginIdentifier;
        AuthStatus = DialogAuthStatus.RequestingOtp;
        StatusMessage = "Your session expired. Requesting a verification code…";
        DialogOtpChallenge challenge = await Authentication.StartOtpAsync(AccountIdentifier);
        OtpLength = challenge.Length;
        BeginOtpChallenge(AccountIdentifier);
        AuthStatus = DialogAuthStatus.WaitingForOtp;
        StatusMessage = $"Session expired. Enter the new {OtpLength}-digit code.";
    }

    public async Task SelectConnectionAsync(DialogConnection connection)
    {
        if (_credentialState is null) return;
        _credentialState = _credentialState with { SelectedConnection = connection.Connection };
        await Vault.SaveAsync(_credentialState);
        AuthStatus = DialogAuthStatus.Authenticated;
        await RefreshAsync();
    }

    public void ShowConnectionPicker()
    {
        if (Connections.Count > 1)
        {
            AuthStatus = DialogAuthStatus.ChoosingConnection;
            StatusMessage = "Choose a linked Dialog connection.";
        }
    }

    public async Task SignOutAsync()
    {
        await OperationGate.WaitAsync();
        try
        {
            await Authentication.CancelAsync();
            await Vault.DeleteAsync();
            _credentialState = null;
            ClearOtpChallenge();
            Connections.Clear();
            AccountIdentifier = "";
            ResetDisplay();
            AuthStatus = DialogAuthStatus.NoAccount;
            StatusMessage = "All saved Dialog sessions were removed.";
        }
        finally
        {
            OperationGate.Release();
        }
    }

    private void ReplaceConnections(System.Collections.Generic.IReadOnlyList<DialogConnection> items)
    {
        Connections.Clear();
        foreach (DialogConnection item in items.Where(item => item.IsActive)) Connections.Add(item);
    }

    private void ApplySnapshot(DialogBalanceSnapshot snapshot)
    {
        ConnectionName = snapshot.ConnectionName;
        PhoneNumber = snapshot.Msisdn;
        ConnectionKind = snapshot.ConnectionKind;
        ConnectionStatus = snapshot.Status;
        Balance = snapshot.PrepaidBalance;
        Validity = snapshot.Validity;
        ApplyPackage(snapshot.Packages.ElementAtOrDefault(0), first: true);
        ApplyPackage(snapshot.Packages.ElementAtOrDefault(1), first: false);
        UpdatedText = $"Last updated: {snapshot.UpdatedAt:HH:mm:ss}";
    }

    public async Task UpdateOtpStateAsync()
    {
        // Keep enforcing the challenge lifetime even when the user presses Back.
        // Back only hides OTP entry; it must not leave Chromium alive indefinitely.
        if (_isUpdatingOtpState || !_otpExpiresAt.HasValue) return;
        if (DateTimeOffset.Now < _otpExpiresAt.Value)
        {
            UpdateOtpCountdown();
            return;
        }

        _isUpdatingOtpState = true;
        await OperationGate.WaitAsync();
        try
        {
            // A submit or resend may have completed while the timer waited for
            // the operation gate. Re-check before closing its browser session.
            if (!_otpExpiresAt.HasValue || DateTimeOffset.Now < _otpExpiresAt.Value)
                return;

            await Authentication.CancelAsync();
            ClearOtpChallenge();
            if (_credentialState?.CachedSnapshot is not null)
            {
                ApplySnapshot(_credentialState.CachedSnapshot);
                SetConnected(false);
                UpdatedText = "Verification timed out · showing last saved balance";
                AuthStatus = DialogAuthStatus.Authenticated;
            }
            else
            {
                AuthStatus = DialogAuthStatus.EnteringAccount;
                StatusMessage = "The verification code timed out. Start sign-in again when you are ready.";
            }
        }
        finally
        {
            OperationGate.Release();
            _isUpdatingOtpState = false;
        }
    }

    private bool HasActiveChallenge(string identifier) =>
        _otpExpiresAt.HasValue && DateTimeOffset.Now < _otpExpiresAt.Value &&
        string.Equals(_activeChallengeIdentifier, identifier, StringComparison.OrdinalIgnoreCase);

    private void BeginOtpChallenge(string identifier)
    {
        _activeChallengeIdentifier = identifier;
        _otpExpiresAt = DateTimeOffset.Now.Add(OtpLifetime);
        _resendAvailableAt = DateTimeOffset.Now.Add(ResendCooldown);
        UpdateOtpCountdown();
    }

    private void ClearOtpChallenge()
    {
        _activeChallengeIdentifier = "";
        _otpExpiresAt = null;
        _resendAvailableAt = null;
        OtpCountdown = "";
        ResendCountdown = "";
        OnPropertyChanged(nameof(CanResendOtp));
    }

    private void UpdateOtpCountdown()
    {
        TimeSpan remaining = (_otpExpiresAt ?? DateTimeOffset.Now) - DateTimeOffset.Now;
        OtpCountdown = remaining > TimeSpan.Zero
            ? $"Code available for {Math.Ceiling(remaining.TotalMinutes):0} min"
            : "Code expired";

        TimeSpan resend = (_resendAvailableAt ?? DateTimeOffset.Now) - DateTimeOffset.Now;
        ResendCountdown = resend > TimeSpan.Zero
            ? $"Resend available in {Math.Ceiling(resend.TotalSeconds):0}s"
            : "You can request a new code";
        OnPropertyChanged(nameof(CanResendOtp));
    }

    private void SetConnected(bool connected)
    {
        IsReconnectVisible = !connected;
        ConnectionStatus = connected ? "Connected" : "Disconnected";
        ConnectionStatusColor = connected ? "#54D98C" : "#FF6B6B";
    }

    private void ApplyPackage(DialogPackageSnapshot? package, bool first)
    {
        string type = package?.Type.ToUpperInvariant() ?? "DATA";
        string name = package?.Name ?? "No package data";
        string remaining = package?.Remaining ?? "--";
        string expiry = package?.Expiry ?? "";
        double percentage = package is null ? 0 : package.HasProgress ? Math.Clamp(package.Percentage, 0, 100) : 100;
        if (first)
        {
            PackageOneType = type; PackageOneName = name; PackageOneRemaining = remaining;
            PackageOneExpiry = expiry; PackageOnePercentage = percentage;
        }
        else
        {
            PackageTwoType = type; PackageTwoName = name; PackageTwoRemaining = remaining;
            PackageTwoExpiry = expiry; PackageTwoPercentage = percentage;
        }
    }

    private void ResetDisplay()
    {
        ConnectionName = "Dialog"; PhoneNumber = "Not signed in"; ConnectionKind = "Connection";
        ConnectionStatus = "Disconnected"; Balance = "--"; Validity = "Sign in required";
        ApplyPackage(null, true); ApplyPackage(null, false); UpdatedText = "Waiting for sign in";
    }

    private static bool IsValidIdentifier(string value) =>
        EmailPattern().IsMatch(value) || MobilePattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^(?:(?:\\+94|0094|0)?7[764]\\d{7})$")]
    private static partial Regex MobilePattern();

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
