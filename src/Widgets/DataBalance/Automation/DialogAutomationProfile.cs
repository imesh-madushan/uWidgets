namespace DataBalance.Automation;

internal static class DialogAutomationProfile
{
    public const int Version = 1;
    public const string LoginUrl = "https://dialog.lk/mydialog-web";
    public const string AccountSummaryUrl = "https://dialog.lk/mydialog-web/account-summary";
    public const string ApiBaseUrl = "https://dialog.lk";

    public const string AccountInput = "#msisdnInput";
    public const string ContinueButton = "button:has-text('CONTINUE')";
    public const string OtpInputs = "#OTPInput .otp-box";
    public const string OtpSubmitButton = "#smsotpsubmit";
    public const string ResendOtpButton = "#resend-otp-btn";

    public const string MainConfigRoute = "/selfcare-proxy?r=dash/get-main-config";
    public const string NavConfigRoute = "/selfcare-proxy?r=dash/get-nav-config";
    public const string ComponentDataRoute = "/selfcare-proxy?r=dash/get-component-data";
    public const string UsageRoute = "/selfcare-proxy?r=usage/get";

    public static readonly string[] InvalidOtpMarkers =
    {
        "invalid verification code", "invalid otp", "incorrect code", "incorrect otp"
    };

    public static readonly string[] ExpiredOtpMarkers =
    {
        "verification code has expired", "otp has expired", "code expired", "request a new code"
    };
}
