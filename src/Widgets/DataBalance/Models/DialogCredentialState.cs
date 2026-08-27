using System.Collections.Generic;

namespace DataBalance.Models;

public sealed record DialogCredentialState
{
    public int Version { get; init; } = 1;
    public string LoginIdentifier { get; init; } = "";
    public string StorageStateJson { get; init; } = "";
    public string SelectedConnection { get; init; } = "";
    public IReadOnlyList<DialogConnection> Connections { get; init; } = [];
    public DialogBalanceSnapshot? CachedSnapshot { get; init; }
}

public enum DialogAuthStatus
{
    NoAccount,
    EnteringAccount,
    RequestingOtp,
    WaitingForOtp,
    SubmittingOtp,
    IncorrectOtp,
    OtpExpired,
    ChoosingConnection,
    Authenticated,
    Error
}

public sealed record DialogOtpChallenge(int Length);

public enum DialogOtpResultKind
{
    Success,
    Incorrect,
    Expired,
    Error
}

public sealed record DialogOtpResult(
    DialogOtpResultKind Kind,
    string Message,
    string? StorageStateJson = null);

public sealed class DialogAuthenticationExpiredException : System.Exception
{
    public DialogAuthenticationExpiredException(string message) : base(message) { }
}
