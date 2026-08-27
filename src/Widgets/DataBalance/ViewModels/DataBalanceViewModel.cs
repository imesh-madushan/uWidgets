using DataBalance.Models;
using DataBalance.Services;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DataBalance.ViewModels;

public sealed class DataBalanceViewModel : INotifyPropertyChanged
{
    private readonly DialogApiService _dialogApiService = new();

    private string _connectionName = "Dialog 4G";
    private string _phoneNumber = "Loading...";
    private string _connectionStatus = "Connecting";
    private string _balance = "--";
    private string _validity = "Loading balance";

    private string _packageOneType = "DATA";
    private string _packageOneName = "Loading package";
    private string _packageOneRemaining = "--";
    private string _packageOneExpiry = "";
    private double _packageOnePercentage;

    private string _packageTwoType = "DATA";
    private string _packageTwoName = "Loading package";
    private string _packageTwoRemaining = "--";
    private string _packageTwoExpiry = "";
    private double _packageTwoPercentage;

    private string _updatedText = "Waiting for first refresh";
    private bool _isRefreshing;

    public string ConnectionName
    {
        get => _connectionName;
        set => SetField(ref _connectionName, value);
    }

    public string PhoneNumber
    {
        get => _phoneNumber;
        set => SetField(ref _phoneNumber, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetField(ref _connectionStatus, value);
    }

    public string Balance
    {
        get => _balance;
        set => SetField(ref _balance, value);
    }

    public string Validity
    {
        get => _validity;
        set => SetField(ref _validity, value);
    }

    public string PackageOneType
    {
        get => _packageOneType;
        set => SetField(ref _packageOneType, value);
    }

    public string PackageOneName
    {
        get => _packageOneName;
        set => SetField(ref _packageOneName, value);
    }

    public string PackageOneRemaining
    {
        get => _packageOneRemaining;
        set => SetField(ref _packageOneRemaining, value);
    }

    public string PackageOneExpiry
    {
        get => _packageOneExpiry;
        set => SetField(ref _packageOneExpiry, value);
    }

    public double PackageOnePercentage
    {
        get => _packageOnePercentage;
        set => SetField(ref _packageOnePercentage, value);
    }

    public string PackageTwoType
    {
        get => _packageTwoType;
        set => SetField(ref _packageTwoType, value);
    }

    public string PackageTwoName
    {
        get => _packageTwoName;
        set => SetField(ref _packageTwoName, value);
    }

    public string PackageTwoRemaining
    {
        get => _packageTwoRemaining;
        set => SetField(ref _packageTwoRemaining, value);
    }

    public string PackageTwoExpiry
    {
        get => _packageTwoExpiry;
        set => SetField(ref _packageTwoExpiry, value);
    }

    public double PackageTwoPercentage
    {
        get => _packageTwoPercentage;
        set => SetField(ref _packageTwoPercentage, value);
    }

    public string UpdatedText
    {
        get => _updatedText;
        set => SetField(ref _updatedText, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetField(ref _isRefreshing, value);
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing)
            return;

        try
        {
            IsRefreshing = true;
            ConnectionStatus = "Connecting";
            UpdatedText = "Updating...";

            DialogBalanceSnapshot snapshot =
                await _dialogApiService.FetchAsync();

            ConnectionName = snapshot.ConnectionName;
            PhoneNumber = snapshot.Msisdn;
            ConnectionStatus = snapshot.Status;
            Balance = snapshot.PrepaidBalance;
            Validity = snapshot.Validity;

            DialogPackageSnapshot? packageOne =
                snapshot.Packages.ElementAtOrDefault(0);

            DialogPackageSnapshot? packageTwo =
                snapshot.Packages.ElementAtOrDefault(1);

            ApplyPackageOne(packageOne);
            ApplyPackageTwo(packageTwo);

            UpdatedText =
                $"Last updated: {snapshot.UpdatedAt:HH:mm:ss}";
        }
        catch (Exception exception)
        {
            ConnectionStatus = "Disconnected";
            UpdatedText = $"Refresh failed: {exception.Message}";

            System.Diagnostics.Debug.WriteLine(
                $"DataBalance refresh failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplyPackageOne(
        DialogPackageSnapshot? package)
    {
        if (package is null)
        {
            PackageOneType = "DATA";
            PackageOneName = "No package data";
            PackageOneRemaining = "--";
            PackageOneExpiry = "";
            PackageOnePercentage = 0;
            return;
        }

        PackageOneType = package.Type.ToUpperInvariant();
        PackageOneName = package.Name;
        PackageOneRemaining = package.Remaining;
        PackageOneExpiry = package.Expiry;
        PackageOnePercentage = package.HasProgress
            ? Math.Clamp(package.Percentage, 0, 100)
            : 100;
    }

    private void ApplyPackageTwo(
        DialogPackageSnapshot? package)
    {
        if (package is null)
        {
            PackageTwoType = "DATA";
            PackageTwoName = "No package data";
            PackageTwoRemaining = "--";
            PackageTwoExpiry = "";
            PackageTwoPercentage = 0;
            return;
        }

        PackageTwoType = package.Type.ToUpperInvariant();
        PackageTwoName = package.Name;
        PackageTwoRemaining = package.Remaining;
        PackageTwoExpiry = package.Expiry;
        PackageTwoPercentage = package.HasProgress
            ? Math.Clamp(package.Percentage, 0, 100)
            : 100;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return;

        field = value;

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}