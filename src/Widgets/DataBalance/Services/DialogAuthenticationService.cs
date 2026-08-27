using DataBalance.Automation;
using DataBalance.Models;
using Microsoft.Playwright;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DataBalance.Services;

public sealed class DialogAuthenticationService : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    public async Task<bool> AreDependenciesInstalledAsync()
    {
        ConfigureDriverSearchPath();
        using IPlaywright playwright = await Playwright.CreateAsync();
        return File.Exists(playwright.Chromium.ExecutablePath);
    }

    public async Task InstallDependenciesAsync(CancellationToken cancellationToken = default)
    {
        int exitCode = await Task.Run(
            () => Microsoft.Playwright.Program.Main(["install", "chromium"]),
            cancellationToken);

        if (exitCode != 0 || !await AreDependenciesInstalledAsync())
            throw new InvalidOperationException($"Playwright could not install Chromium (exit code {exitCode}).");
    }

    public async Task<DialogOtpChallenge> StartOtpAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CloseBrowserAsync();
            await OpenBrowserAsync(null, cancellationToken);

            await _page!.GotoAsync(
                DialogAutomationProfile.LoginUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });

            ILocator input = _page.Locator(DialogAutomationProfile.AccountInput);
            await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await input.FillAsync("");
            await input.PressSequentiallyAsync(identifier, new LocatorPressSequentiallyOptions { Delay = 20 });
            await _page.Locator(DialogAutomationProfile.ContinueButton).ClickAsync();

            ILocator otpInputs = _page.Locator(DialogAutomationProfile.OtpInputs);
            await otpInputs.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30000
            });

            int count = await otpInputs.CountAsync();
            if (count <= 0)
                throw new InvalidOperationException("Dialog did not display OTP fields.");

            return new DialogOtpChallenge(count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DialogOtpResult> SubmitOtpAsync(
        string otp,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_page is null || _context is null)
                return new DialogOtpResult(DialogOtpResultKind.Expired, "The login attempt is no longer active.");

            ILocator inputs = _page.Locator(DialogAutomationProfile.OtpInputs);
            int count = await inputs.CountAsync();
            if (otp.Length != count || otp.Any(character => !char.IsDigit(character)))
                return new DialogOtpResult(DialogOtpResultKind.Incorrect, $"Enter the complete {count}-digit code.");

            for (int index = 0; index < count; index++)
                await inputs.Nth(index).FillAsync("");

            await inputs.First.PressSequentiallyAsync(otp, new LocatorPressSequentiallyOptions { Delay = 35 });
            await _page.Locator(DialogAutomationProfile.OtpSubmitButton).ClickAsync();

            DateTime deadline = DateTime.UtcNow.AddSeconds(35);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_page.Url.Contains("/mydialog-web/account-summary", StringComparison.OrdinalIgnoreCase))
                {
                    string state = await _context.StorageStateAsync();
                    await CloseBrowserAsync();
                    return new DialogOtpResult(DialogOtpResultKind.Success, "Signed in.", state);
                }

                string body = (await _page.Locator("body").InnerTextAsync()).ToLowerInvariant();
                if (DialogAutomationProfile.ExpiredOtpMarkers.Any(body.Contains))
                    return new DialogOtpResult(DialogOtpResultKind.Expired, "The verification code expired. Request a new code.");

                if (DialogAutomationProfile.InvalidOtpMarkers.Any(body.Contains))
                    return new DialogOtpResult(DialogOtpResultKind.Incorrect, "The verification code was incorrect.");

                await Task.Delay(250, cancellationToken);
            }

            return new DialogOtpResult(DialogOtpResultKind.Error, "Dialog did not finish signing in. Try again.");
        }
        catch (PlaywrightException)
        {
            await CloseBrowserAsync();
            return new DialogOtpResult(
                DialogOtpResultKind.SessionLost,
                "The secure login window stopped responding. Start sign-in again.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResendOtpAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_page is null)
                throw new InvalidOperationException("The login attempt is no longer active.");

            await _page.Locator(DialogAutomationProfile.ResendOtpButton).ClickAsync();
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> TrySilentRenewAsync(
        string storageStateJson,
        string selectedConnection,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CloseBrowserAsync();
            await OpenBrowserAsync(storageStateJson, cancellationToken);

            if (!string.IsNullOrWhiteSpace(selectedConnection))
            {
                await _context!.AddInitScriptAsync(
                    "value => sessionStorage.setItem('selected_number', value)",
                    selectedConnection);
            }

            await _page!.GotoAsync(
                DialogAutomationProfile.AccountSummaryUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });

            try
            {
                await _page.WaitForURLAsync(
                    url => url.Contains("/mydialog-web/account-summary", StringComparison.OrdinalIgnoreCase),
                    new PageWaitForURLOptions { Timeout = 15000 });
            }
            catch (TimeoutException)
            {
                // The URL below determines whether Dialog requested interactive login.
            }

            if (_page.Url.Contains("/mydialog-web/account-summary", StringComparison.OrdinalIgnoreCase) &&
                await _page.Locator(DialogAutomationProfile.AccountInput).CountAsync() == 0)
            {
                string state = await _context!.StorageStateAsync();
                await CloseBrowserAsync();
                return state;
            }

            await CloseBrowserAsync();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CancelAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await CloseBrowserAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task OpenBrowserAsync(string? storageStateJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigureDriverSearchPath();
        _playwright = await Playwright.CreateAsync();
        _browser = await LaunchChromiumAsync();
        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageState = storageStateJson,
            Locale = "en-GB",
            TimezoneId = "Asia/Colombo"
        });
        _page = await _context.NewPageAsync();
    }

    private Task<IBrowser> LaunchChromiumAsync() =>
        _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

    private static void ConfigureDriverSearchPath()
    {
        string? pluginDirectory = Path.GetDirectoryName(typeof(DialogAuthenticationService).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(pluginDirectory))
            Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", pluginDirectory);
    }

    private async Task CloseBrowserAsync()
    {
        IPage? page = _page;
        IBrowserContext? context = _context;
        IBrowser? browser = _browser;
        IPlaywright? playwright = _playwright;
        _page = null;
        _context = null;
        _browser = null;
        _playwright = null;

        // A crashed page/context must not prevent the remaining resources from
        // being released or bubble an exception into Avalonia's timer thread.
        try { if (page is not null && !page.IsClosed) await page.CloseAsync(); }
        catch (PlaywrightException) { }
        try { if (context is not null) await context.CloseAsync(); }
        catch (PlaywrightException) { }
        try { if (browser is not null && browser.IsConnected) await browser.CloseAsync(); }
        catch (PlaywrightException) { }
        playwright?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await CancelAsync();
        _gate.Dispose();
    }
}
