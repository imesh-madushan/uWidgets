using DataBalance.Automation;
using DataBalance.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DataBalance.Services;

public sealed record DialogFetchResult(
    DialogBalanceSnapshot Snapshot,
    IReadOnlyList<DialogConnection> Connections,
    string StorageStateJson);

public sealed class DialogApiService
{
    public async Task<DialogFetchResult> FetchAsync(
        string storageStateJson,
        string selectedConnection,
        CancellationToken cancellationToken = default)
    {
        CookieContainer cookies = CreateCookieContainer(storageStateJson);
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(DialogAutomationProfile.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(45)
        };

        using JsonDocument mainConfig = await SendAsync(
            client, DialogAutomationProfile.MainConfigRoute, "{\"forcedNumber\":null}",
            selectedConnection, "/mydialog-web/account-summary", cancellationToken);

        IReadOnlyList<DialogConnection> connections = ParseConnections(mainConfig.RootElement);
        DialogConnection selected = connections.FirstOrDefault(item => item.Connection == selectedConnection)
            ?? connections.FirstOrDefault(item => item.IsPrimary)
            ?? connections.FirstOrDefault()
            ?? throw new InvalidOperationException("No Dialog connections are linked to this account.");

        using JsonDocument navConfig = await SendAsync(
            client, DialogAutomationProfile.NavConfigRoute, "{\"nav_id\":\"492\"}",
            selected.Connection, "/mydialog-web/account-summary", cancellationToken);

        (string balanceTermId, string? packageTermId) = ParseSectionTerms(navConfig.RootElement);

        using JsonDocument balanceResponse = await SendAsync(
            client,
            DialogAutomationProfile.ComponentDataRoute,
            JsonSerializer.Serialize(new { id = "balance_summery_section", nav_id = "492", term_id = balanceTermId }),
            selected.Connection,
            "/mydialog-web/account-summary",
            cancellationToken);

        (string prepaidBalance, string validity) = ParseBalance(balanceResponse.RootElement);
        IReadOnlyList<DialogPackageSnapshot> packages;

        if (string.Equals(selected.DdsLob, "DTV", StringComparison.OrdinalIgnoreCase))
        {
            packages = await FetchTelevisionPackagesAsync(
                client, selected, packageTermId, cancellationToken);
        }
        else
        {
            using JsonDocument usageResponse = await SendAsync(
                client, DialogAutomationProfile.UsageRoute, "{}", selected.Connection,
                "/mydialog-web/services/data-detail-tab", cancellationToken);
            packages = ParseUsagePackages(usageResponse.RootElement);
        }

        var snapshot = new DialogBalanceSnapshot(
            UpdatedAt: DateTime.Now,
            Status: selected.StatusLabel,
            ConnectionName: selected.DisplayName,
            Msisdn: selected.Connection,
            ConnectionKind: selected.FriendlyType,
            PrepaidBalance: prepaidBalance,
            Validity: validity,
            Packages: packages);

        return new DialogFetchResult(snapshot, connections, MergeCookies(storageStateJson, cookies));
    }

    private static async Task<IReadOnlyList<DialogPackageSnapshot>> FetchTelevisionPackagesAsync(
        HttpClient client,
        DialogConnection selected,
        string? termId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(termId))
            return [];

        using JsonDocument response = await SendAsync(
            client,
            DialogAutomationProfile.ComponentDataRoute,
            JsonSerializer.Serialize(new { id = "my_packages_section", nav_id = "492", term_id = termId }),
            selected.Connection,
            "/mydialog-web/account-summary",
            cancellationToken);

        JsonElement packages = response.RootElement.GetProperty("data").GetProperty("packages");
        return packages.EnumerateArray().Select((package, index) => new DialogPackageSnapshot(
            Type: selected.FriendlyType,
            Code: "package",
            Name: GetString(package, "package_name", selected.PackageName),
            Total: "",
            Remaining: GetFlexibleString(package, "price_label", selected.Price ?? "Active") ?? "Active",
            RemainingText: "",
            Expiry: GetString(package, "package_title", "Base package"),
            Percentage: 100,
            HasProgress: false,
            Order: index)).ToArray();
    }

    private static async Task<JsonDocument> SendAsync(
        HttpClient client,
        string route,
        string body,
        string selectedConnection,
        string refererPath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Lang", "en");
        request.Headers.TryAddWithoutValidation("uuid", Guid.NewGuid().ToString());
        request.Headers.Referrer = new Uri(DialogAutomationProfile.ApiBaseUrl + refererPath);
        if (!string.IsNullOrWhiteSpace(selectedConnection))
            request.Headers.TryAddWithoutValidation("Msisdn", selectedConnection);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            IsAuthenticationRedirect(response))
            throw new DialogAuthenticationExpiredException("The Dialog session expired.");

        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Dialog API returned HTTP {(int)response.StatusCode}.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            if (text.Contains("authenticationendpoint", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Mobile Connect", StringComparison.OrdinalIgnoreCase))
                throw new DialogAuthenticationExpiredException("Dialog requested a new login.");
            throw new InvalidOperationException("Dialog returned an invalid JSON response.", exception);
        }

        if (!IsSuccessful(document.RootElement))
        {
            string error = GetString(document.RootElement, "error", "Dialog API response was unsuccessful.");
            document.Dispose();
            if (LooksLikeAuthenticationFailure(error))
                throw new DialogAuthenticationExpiredException(error);
            throw new InvalidOperationException(error);
        }

        return document;
    }

    private static bool IsAuthenticationRedirect(HttpResponseMessage response)
    {
        if ((int)response.StatusCode is < 300 or >= 400)
            return false;
        string location = response.Headers.Location?.ToString() ?? "";
        return location.Contains("authenticationendpoint", StringComparison.OrdinalIgnoreCase) ||
               location.Contains("login", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuccessful(JsonElement response) =>
        response.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.True;

    private static bool LooksLikeAuthenticationFailure(string error) =>
        error.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("session", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("login", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<DialogConnection> ParseConnections(JsonElement response)
    {
        JsonElement list = response.GetProperty("data").GetProperty("config").GetProperty("connectionList");
        var result = new List<DialogConnection>();
        foreach (JsonElement element in list.EnumerateArray())
        {
            result.Add(new DialogConnection
            {
                Connection = GetString(element, "connection"),
                ConnectionType = GetString(element, "connectionType"),
                Lob = GetString(element, "lob"),
                DdsLob = GetString(element, "ddsLob"),
                NickName = GetString(element, "nickName"),
                Icon = GetString(element, "icon"),
                ConnectionDotColor = GetString(element, "connectionDotColor"),
                StatusLabel = GetString(element, "statusLabel", "Connected"),
                IsActive = GetBoolean(element, "isActive"),
                IsPrimary = GetBoolean(element, "isPrimary"),
                ProfileLanguageId = GetString(element, "profLanguageId"),
                UnderPrimaryNic = GetBoolean(element, "underPrimaryNic"),
                Address = element.TryGetProperty("address", out JsonElement address) && address.ValueKind == JsonValueKind.Array
                    ? address.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() : [],
                PackageCategory = GetString(element, "packageCategory"),
                PackageName = GetString(element, "packageName"),
                Price = GetFlexibleString(element, "price", null),
                PrimaryCustomerId = GetString(element, "ctridPrimary"),
                Email = GetString(element, "email"),
                DialogUuid = GetString(element, "dialoguuid")
            });
        }
        return result;
    }

    private static (string BalanceTerm, string? PackageTerm) ParseSectionTerms(JsonElement response)
    {
        string? balance = null;
        string? package = null;
        foreach (JsonElement section in response.GetProperty("data").GetProperty("sections").EnumerateArray())
        {
            string id = GetString(section, "id");
            if (id == "balance_summery_section") balance = GetString(section, "term_id");
            if (id == "my_packages_section") package = GetString(section, "term_id");
        }
        return (balance ?? throw new InvalidOperationException("Dialog balance configuration was unavailable."), package);
    }

    private static (string Balance, string Validity) ParseBalance(JsonElement response)
    {
        foreach (JsonElement tab in response.GetProperty("data").GetProperty("tabContents").EnumerateArray())
        {
            if (GetString(tab, "id") != "money" || !tab.TryGetProperty("cardData", out JsonElement cards))
                continue;
            foreach (JsonElement card in cards.EnumerateArray())
            {
                if (string.Equals(GetString(card, "topLabel"), "Prepaid Balance", StringComparison.OrdinalIgnoreCase))
                    return (GetString(card, "middleLabel", "--"), GetString(card, "bottomLabel", "Validity unavailable"));
            }
        }
        throw new InvalidOperationException("Prepaid balance was not present in the Dialog response.");
    }

    private static IReadOnlyList<DialogPackageSnapshot> ParseUsagePackages(JsonElement response)
    {
        var packages = new List<DialogPackageSnapshot>();
        foreach (JsonElement type in response.GetProperty("data").GetProperty("usage_types").EnumerateArray())
        {
            if (!type.TryGetProperty("usages", out JsonElement usages)) continue;
            packages.AddRange(usages.EnumerateArray().Select(usage => new DialogPackageSnapshot(
                Type: GetString(type, "tab_name", "Data"),
                Code: GetString(type, "code"),
                Name: GetString(usage, "usage_name", "Unknown package"),
                Total: GetString(usage, "total"),
                Remaining: GetString(usage, "remaining_amount", "--"),
                RemainingText: GetString(usage, "remaining_text"),
                Expiry: GetString(usage, "expiry", "Expiry unavailable"),
                Percentage: GetInteger(usage, "precentage"),
                HasProgress: GetBoolean(usage, "progress"),
                Order: GetInteger(usage, "order"))));
        }
        return packages.OrderBy(item => item.Order).ToArray();
    }

    private static CookieContainer CreateCookieContainer(string storageStateJson)
    {
        var container = new CookieContainer();
        using JsonDocument state = JsonDocument.Parse(storageStateJson);
        if (!state.RootElement.TryGetProperty("cookies", out JsonElement cookies)) return container;
        foreach (JsonElement item in cookies.EnumerateArray())
        {
            string name = GetString(item, "name");
            string domain = GetString(item, "domain");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain)) continue;
            var cookie = new Cookie(name, GetString(item, "value"), GetString(item, "path", "/"), domain)
            {
                HttpOnly = GetBoolean(item, "httpOnly"),
                Secure = GetBoolean(item, "secure")
            };
            if (item.TryGetProperty("expires", out JsonElement expires) && expires.TryGetDouble(out double seconds) && seconds > 0)
                cookie.Expires = DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime;
            try { container.Add(cookie); } catch (CookieException) { }
        }
        return container;
    }

    private static string MergeCookies(string storageStateJson, CookieContainer container)
    {
        if (JsonNode.Parse(storageStateJson) is not JsonObject state ||
            state["cookies"] is not JsonArray storedCookies)
            return storageStateJson;

        static string Key(string name, string domain, string path) =>
            $"{name}\n{domain.TrimStart('.')}\n{path}";

        Dictionary<string, JsonObject> storedByKey = storedCookies
            .OfType<JsonObject>()
            .ToDictionary(
                item => Key(
                    item["name"]?.GetValue<string>() ?? "",
                    item["domain"]?.GetValue<string>() ?? "",
                    item["path"]?.GetValue<string>() ?? "/"),
                StringComparer.OrdinalIgnoreCase);

        foreach (Cookie cookie in container.GetAllCookies())
        {
            string key = Key(cookie.Name, cookie.Domain, cookie.Path);
            if (!storedByKey.TryGetValue(key, out JsonObject? item))
            {
                item = new JsonObject
                {
                    ["name"] = cookie.Name,
                    ["domain"] = cookie.Domain,
                    ["path"] = cookie.Path,
                    ["httpOnly"] = cookie.HttpOnly,
                    ["secure"] = cookie.Secure,
                    ["sameSite"] = "Lax"
                };
                storedCookies.Add(item);
            }

            item["value"] = cookie.Value;
            item["expires"] = cookie.Expires == DateTime.MinValue
                ? -1
                : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds();
        }

        return state.ToJsonString();
    }

    private static string GetString(JsonElement element, string propertyName, string fallback = "")
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return fallback;
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? fallback : property.ToString();
    }

    private static string? GetFlexibleString(JsonElement element, string propertyName, string? fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined) return fallback;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.True;

    private static int GetInteger(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)) return 0;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int number)) return number;
        return int.TryParse(property.ToString(), out int parsed) ? parsed : 0;
    }
}
