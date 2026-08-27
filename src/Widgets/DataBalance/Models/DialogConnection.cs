using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DataBalance.Models;

public sealed record DialogConnection
{
    [JsonPropertyName("connection")]
    public string Connection { get; init; } = "";

    [JsonPropertyName("connectionType")]
    public string ConnectionType { get; init; } = "";

    [JsonPropertyName("lob")]
    public string Lob { get; init; } = "";

    [JsonPropertyName("ddsLob")]
    public string DdsLob { get; init; } = "";

    [JsonPropertyName("nickName")]
    public string NickName { get; init; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "";

    [JsonPropertyName("connectionDotColor")]
    public string ConnectionDotColor { get; init; } = "";

    [JsonPropertyName("statusLabel")]
    public string StatusLabel { get; init; } = "";

    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }

    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; init; }

    [JsonPropertyName("profLanguageId")]
    public string ProfileLanguageId { get; init; } = "";

    [JsonPropertyName("underPrimaryNic")]
    public bool UnderPrimaryNic { get; init; }

    [JsonPropertyName("address")]
    public IReadOnlyList<string> Address { get; init; } = [];

    [JsonPropertyName("packageCategory")]
    public string PackageCategory { get; init; } = "";

    [JsonPropertyName("packageName")]
    public string PackageName { get; init; } = "";

    public string? Price { get; init; }

    [JsonPropertyName("ctridPrimary")]
    public string PrimaryCustomerId { get; init; } = "";

    [JsonPropertyName("email")]
    public string Email { get; init; } = "";

    [JsonPropertyName("dialoguuid")]
    public string DialogUuid { get; init; } = "";

    [JsonIgnore]
    public string FriendlyType => DdsLob.ToUpperInvariant() switch
    {
        "GSM" => "Mobile",
        "HBB" => "Home Broadband",
        "DTV" => "Television",
        _ => "Dialog Connection"
    };

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(NickName)
        ? $"{FriendlyType} · {Connection}"
        : $"{NickName} · {Connection}";
}
