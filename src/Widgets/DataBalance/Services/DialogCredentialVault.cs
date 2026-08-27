using DataBalance.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DataBalance.Services;

public sealed class DialogCredentialVault
{
    private const string ServiceName = "uWidgets.DataBalance";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(ServiceName);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uWidgets",
        "DataBalance");

    private string VaultPath => Path.Combine(_directory, "credentials.vault");
    private string WindowsKeyPath => Path.Combine(_directory, "vault-key.bin");

    public bool Exists => File.Exists(VaultPath);

    public async Task SaveAsync(
        DialogCredentialState state,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        byte[] key = await GetOrCreateKeyAsync(cancellationToken);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Entropy);

            var envelope = new VaultEnvelope(
                Version: 1,
                Nonce: Convert.ToBase64String(nonce),
                Ciphertext: Convert.ToBase64String(ciphertext),
                Tag: Convert.ToBase64String(tag));

            string temporaryPath = VaultPath + ".tmp";
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(envelope, JsonOptions),
                Encoding.UTF8,
                cancellationToken);

            File.Move(temporaryPath, VaultPath, true);
            RestrictFilePermissions(VaultPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<DialogCredentialState?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(VaultPath))
            return null;

        byte[] key = await GetOrCreateKeyAsync(cancellationToken);

        try
        {
            string text = await File.ReadAllTextAsync(VaultPath, cancellationToken);
            VaultEnvelope envelope = JsonSerializer.Deserialize<VaultEnvelope>(text, JsonOptions)
                ?? throw new InvalidDataException("The Dialog credential vault is invalid.");

            byte[] nonce = Convert.FromBase64String(envelope.Nonce);
            byte[] ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            byte[] tag = Convert.FromBase64String(envelope.Tag);
            byte[] plaintext = new byte[ciphertext.Length];

            try
            {
                using var aes = new AesGcm(key, tag.Length);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, Entropy);
                return JsonSerializer.Deserialize<DialogCredentialState>(plaintext, JsonOptions)
                    ?? throw new InvalidDataException("The Dialog credential state is invalid.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(VaultPath))
            File.Delete(VaultPath);

        if (OperatingSystem.IsWindows())
        {
            if (File.Exists(WindowsKeyPath))
                File.Delete(WindowsKeyPath);
        }
        else if (OperatingSystem.IsMacOS())
        {
            await RunSecretCommandAsync(
                "security",
                ["delete-generic-password", "-s", ServiceName, "-a", Environment.UserName],
                null,
                allowFailure: true,
                cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await RunSecretCommandAsync(
                "secret-tool",
                ["clear", "application", "uWidgets", "service", "DataBalance"],
                null,
                allowFailure: true,
                cancellationToken);
        }
    }

    private async Task<byte[]> GetOrCreateKeyAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);

        if (OperatingSystem.IsWindows())
        {
            if (File.Exists(WindowsKeyPath))
            {
                byte[] savedProtectedKey = await File.ReadAllBytesAsync(WindowsKeyPath, cancellationToken);
                return ProtectedData.Unprotect(savedProtectedKey, Entropy, DataProtectionScope.CurrentUser);
            }

            byte[] key = RandomNumberGenerator.GetBytes(32);
            byte[] protectedKey = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(WindowsKeyPath, protectedKey, cancellationToken);
            RestrictFilePermissions(WindowsKeyPath);
            return key;
        }

        if (OperatingSystem.IsMacOS())
        {
            ProcessResult existing = await RunSecretCommandAsync(
                "security",
                ["find-generic-password", "-s", ServiceName, "-a", Environment.UserName, "-w"],
                null,
                allowFailure: true,
                cancellationToken);

            if (existing.ExitCode == 0 && !string.IsNullOrWhiteSpace(existing.Output))
                return Convert.FromBase64String(existing.Output.Trim());

            byte[] key = RandomNumberGenerator.GetBytes(32);
            await RunSecretCommandAsync(
                "security",
                ["add-generic-password", "-U", "-s", ServiceName, "-a", Environment.UserName,
                    "-w", Convert.ToBase64String(key)],
                null,
                allowFailure: false,
                cancellationToken);
            return key;
        }

        if (OperatingSystem.IsLinux())
        {
            ProcessResult existing = await RunSecretCommandAsync(
                "secret-tool",
                ["lookup", "application", "uWidgets", "service", "DataBalance"],
                null,
                allowFailure: true,
                cancellationToken);

            if (existing.ExitCode == 0 && !string.IsNullOrWhiteSpace(existing.Output))
                return Convert.FromBase64String(existing.Output.Trim());

            byte[] key = RandomNumberGenerator.GetBytes(32);
            await RunSecretCommandAsync(
                "secret-tool",
                ["store", "--label=uWidgets DataBalance", "application", "uWidgets", "service", "DataBalance"],
                Convert.ToBase64String(key),
                allowFailure: false,
                cancellationToken);
            return key;
        }

        throw new PlatformNotSupportedException(
            "Secure credential persistence is unavailable on this platform.");
    }

    private static async Task<ProcessResult> RunSecretCommandAsync(
        string fileName,
        string[] arguments,
        string? input,
        bool allowFailure,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {fileName}.");

        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException(
                $"The operating-system credential store is unavailable: {error.Trim()}");
        }

        return new ProcessResult(process.ExitCode, output);
    }

    private static void RestrictFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private sealed record VaultEnvelope(int Version, string Nonce, string Ciphertext, string Tag);
    private sealed record ProcessResult(int ExitCode, string Output);
}
