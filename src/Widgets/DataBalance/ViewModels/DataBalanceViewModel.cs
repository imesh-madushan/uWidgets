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

    private static DialogCredentialState? _credentialState;
    private DialogAuthStatus _authStatus = DialogAuthStatus.NoAccount;
    private string _accountIdentifier = "";
    private string _statusMessage = "Sign in to view your Dialog balance";
    private int _otpLength = 6;
    private bool _isRefreshing;
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

    public bool IsAccountEntryVisible => AuthStatus is DialogAuthStatus.NoAccount or DialogAuthStatus.EnteringAccount;
    public bool IsOtpVisible => AuthStatus is DialogAuthStatus.RequestingOtp or DialogAuthStatus.WaitingForOtp or
        DialogAuthStatus.SubmittingOtp or DialogAuthStatus.IncorrectOtp or DialogAuthStatus.OtpExpired;
    public bool IsBalanceVisible => AuthStatus == DialogAuthStatus.Authenticated;
    public bool IsConnectionPickerVisible => AuthStatus == DialogAuthStatus.ChoosingConnection;
    public bool IsErrorVisible => AuthStatus == DialogAuthStatus.Error;
    public bool CanResendOtp => AuthStatus is DialogAuthStatus.WaitingForOtp or DialogAuthStatus.IncorrectOtp or DialogAuthStatus.OtpExpired;

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
        await RefreshAsync();
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

        await OperationGate.WaitAsync();
        try
        {
            AuthStatus = DialogAuthStatus.RequestingOtp;
            StatusMessage = "Requesting verification code…";
            DialogOtpChallenge challenge = await Authentication.StartOtpAsync(identifier);
            OtpLength = challenge.Length;
            AccountIdentifier = identifier;
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
                AuthStatus = result.Kind switch
                {
                    DialogOtpResultKind.Incorrect => DialogAuthStatus.IncorrectOtp,
                    DialogOtpResultKind.Expired => DialogAuthStatus.OtpExpired,
                    _ => DialogAuthStatus.WaitingForOtp
                };
                StatusMessage = result.Message;
                return;
            }

            string previousSelection = _credentialState?.SelectedConnection ?? "";
            _credentialState = new DialogCredentialState
            {
                LoginIdentifier = AccountIdentifier.Trim(),
                StorageStateJson = result.StorageStateJson,
                SelectedConnection = previousSelection,
                Connections = _credentialState?.Connections ?? []
            };
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
            AuthStatus = DialogAuthStatus.WaitingForOtp;
            StatusMessage = "A new verification code was sent.";
        }
        catch (Exception exception)
        {
            AuthStatus = DialogAuthStatus.OtpExpired;
            StatusMessage = $"Unable to resend the code: {exception.Message}";
        }
        finally
        {
            OperationGate.Release();
        }
    }

    public async Task CancelLoginAsync()
    {
        await Authentication.CancelAsync();
        AuthStatus = DialogAuthStatus.EnteringAccount;
        StatusMessage = "Change the email address or mobile number and try again.";
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
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
                SelectedConnection = selected
            };
            await Vault.SaveAsync(_credentialState);
            ReplaceConnections(result.Connections);
            ApplySnapshot(result.Snapshot);

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
            ConnectionStatus = "Disconnected";
            UpdatedText = $"Refresh failed: {exception.Message}";
            if (AuthStatus != DialogAuthStatus.Authenticated)
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
                SelectedConnection = retry.Snapshot.Msisdn
            };
            await Vault.SaveAsync(_credentialState);
            ReplaceConnections(retry.Connections);
            ApplySnapshot(retry.Snapshot);
            AuthStatus = DialogAuthStatus.Authenticated;
            return;
        }

        AccountIdentifier = _credentialState.LoginIdentifier;
        AuthStatus = DialogAuthStatus.RequestingOtp;
        StatusMessage = "Your session expired. Requesting a verification code…";
        DialogOtpChallenge challenge = await Authentication.StartOtpAsync(AccountIdentifier);
        OtpLength = challenge.Length;
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
