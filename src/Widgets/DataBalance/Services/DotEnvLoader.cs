using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace DataBalance.Services;

public static class DotEnvLoader
{
    public static IReadOnlyDictionary<string, string> Load()
    {
        string assemblyPath =
            Assembly.GetExecutingAssembly().Location;

        string assemblyDirectory =
            Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException(
                "Unable to determine the widget directory.");

        string environmentFile =
            Path.Combine(assemblyDirectory, ".env");

        if (!File.Exists(environmentFile))
        {
            throw new FileNotFoundException(
                $"Configuration file was not found: {environmentFile}");
        }

        var values =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (string originalLine in File.ReadAllLines(environmentFile))
        {
            string line = originalLine.Trim();

            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf('=');

            if (separator < 1)
            {
                throw new InvalidOperationException(
                    "Invalid .env entry. Expected KEY=VALUE.");
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            string? processValue =
                Environment.GetEnvironmentVariable(
                    key,
                    EnvironmentVariableTarget.Process);

            values[key] = string.IsNullOrWhiteSpace(processValue)
                ? value
                : processValue;
        }

        return values;
    }

    public static string Required(
        this IReadOnlyDictionary<string, string> values,
        string name)
    {
        if (!values.TryGetValue(name, out string? value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required configuration is missing: {name}");
        }

        return value;
    }

    public static string Optional(
        this IReadOnlyDictionary<string, string> values,
        string name)
    {
        return values.TryGetValue(name, out string? value)
            ? value
            : string.Empty;
    }
}