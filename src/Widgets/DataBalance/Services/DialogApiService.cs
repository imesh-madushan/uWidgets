using DataBalance.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DataBalance.Services;

public sealed class DialogApiService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    public async Task<DialogBalanceSnapshot> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, string> configuration =
            DotEnvLoader.Load();

        string connectionName =
            configuration.Required("DIALOG_CONNECTION_NAME");

        string msisdn =
            configuration.Required("DIALOG_MSISDN");

        string cookie =
            configuration.Required("DIALOG_COOKIE");

        using JsonDocument balanceResponse =
            await SendRequestAsync(
                configuration.Required("DIALOG_BALANCE_URL"),
                configuration.Required("DIALOG_BALANCE_BODY"),
                msisdn,
                cookie,
                configuration.Optional("DIALOG_BALANCE_UUID"),
                configuration.Required("DIALOG_BALANCE_REFERER"),
                cancellationToken);

        using JsonDocument usageResponse =
            await SendRequestAsync(
                configuration.Required("DIALOG_USAGE_URL"),
                configuration.Required("DIALOG_USAGE_BODY"),
                msisdn,
                cookie,
                configuration.Optional("DIALOG_USAGE_UUID"),
                configuration.Required("DIALOG_USAGE_REFERER"),
                cancellationToken);

        EnsureSuccessful(balanceResponse.RootElement);
        EnsureSuccessful(usageResponse.RootElement);

        (string prepaidBalance, string validity) =
            ParseBalance(balanceResponse.RootElement);

        IReadOnlyList<DialogPackageSnapshot> packages =
            ParsePackages(usageResponse.RootElement);

        return new DialogBalanceSnapshot(
            UpdatedAt: DateTime.Now,
            Status: "Connected",
            ConnectionName: connectionName,
            Msisdn: msisdn,
            PrepaidBalance: prepaidBalance,
            Validity: validity,
            Packages: packages);
    }

    private static async Task<JsonDocument> SendRequestAsync(
        string url,
        string requestBody,
        string msisdn,
        string cookie,
        string uuid,
        string referer,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url);

        request.Headers.TryAddWithoutValidation(
            "Msisdn",
            msisdn);

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            cookie);

        request.Headers.TryAddWithoutValidation(
            "Accept",
            "application/json, text/plain, */*");

        request.Headers.TryAddWithoutValidation(
            "Accept-Language",
            "en-GB,en-US;q=0.9,en;q=0.8");

        request.Headers.TryAddWithoutValidation(
            "Lang",
            "en");

        request.Headers.TryAddWithoutValidation(
            "Referer",
            referer);

        if (!string.IsNullOrWhiteSpace(uuid))
        {
            request.Headers.TryAddWithoutValidation(
                "uuid",
                uuid);
        }

        request.Content = new StringContent(
            requestBody,
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response =
            await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        string responseText =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Dialog API returned HTTP {(int)response.StatusCode}.");
        }

        try
        {
            return JsonDocument.Parse(responseText);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Dialog returned an invalid JSON response.",
                exception);
        }
    }

    private static void EnsureSuccessful(JsonElement response)
    {
        if (!response.TryGetProperty(
                "success",
                out JsonElement success) ||
            success.ValueKind != JsonValueKind.True)
        {
            string error = GetString(response, "error");

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "Dialog API response was unsuccessful."
                    : error);
        }
    }

    private static (string Balance, string Validity) ParseBalance(
        JsonElement response)
    {
        JsonElement tabContents =
            response
                .GetProperty("data")
                .GetProperty("tabContents");

        foreach (JsonElement tab in tabContents.EnumerateArray())
        {
            if (GetString(tab, "id") != "money" ||
                !tab.TryGetProperty(
                    "cardData",
                    out JsonElement cardData))
            {
                continue;
            }

            foreach (JsonElement card in cardData.EnumerateArray())
            {
                if (!string.Equals(
                        GetString(card, "topLabel"),
                        "Prepaid Balance",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return (
                    GetString(card, "middleLabel", "--"),
                    GetString(card, "bottomLabel", "Validity unavailable"));
            }
        }

        throw new InvalidOperationException(
            "Prepaid balance was not present in the Dialog response.");
    }

    private static IReadOnlyList<DialogPackageSnapshot> ParsePackages(
        JsonElement response)
    {
        var packages = new List<DialogPackageSnapshot>();

        JsonElement usageTypes =
            response
                .GetProperty("data")
                .GetProperty("usage_types");

        foreach (JsonElement usageType in usageTypes.EnumerateArray())
        {
            string type =
                GetString(usageType, "tab_name", "Data");

            string code =
                GetString(usageType, "code");

            if (!usageType.TryGetProperty(
                    "usages",
                    out JsonElement usages))
            {
                continue;
            }

            foreach (JsonElement usage in usages.EnumerateArray())
            {
                packages.Add(
                    new DialogPackageSnapshot(
                        Type: type,
                        Code: code,
                        Name: GetString(
                            usage,
                            "usage_name",
                            "Unknown package"),
                        Total: GetString(usage, "total"),
                        Remaining: GetString(
                            usage,
                            "remaining_amount",
                            "--"),
                        RemainingText: GetString(
                            usage,
                            "remaining_text"),
                        Expiry: GetString(
                            usage,
                            "expiry",
                            "Expiry unavailable"),
                        Percentage: GetInteger(
                            usage,
                            "precentage"),
                        HasProgress: GetBoolean(
                            usage,
                            "progress"),
                        Order: GetInteger(
                            usage,
                            "order")));
            }
        }

        return packages
            .OrderBy(package => package.Order)
            .ToArray();
    }

    private static string GetString(
        JsonElement element,
        string propertyName,
        string fallback = "")
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement property) ||
            property.ValueKind is JsonValueKind.Null or
                JsonValueKind.Undefined)
        {
            return fallback;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : property.ToString();
    }

    private static int GetInteger(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out int number))
        {
            return number;
        }

        return int.TryParse(
            property.ToString(),
            out int parsed)
            ? parsed
            : 0;
    }

    private static bool GetBoolean(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(
                   propertyName,
                   out JsonElement property) &&
               property.ValueKind == JsonValueKind.True;
    }
}