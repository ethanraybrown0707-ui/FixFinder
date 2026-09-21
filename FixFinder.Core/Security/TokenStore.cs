using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Engine;

namespace FixFinder.Core.Security;

/// <summary>The two optional credentials FixFinder can hold.</summary>
public sealed record StoredCredentials(string? GitHubToken = null, string? StackExchangeKey = null)
{
    public static readonly StoredCredentials Empty = new();
}

/// <summary>Stores API credentials encrypted to the current Windows user account.</summary>
public static class TokenStore
{
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("FixFinder.v1"));

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixFinder", "credentials.dat");

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static bool Exists => File.Exists(FilePath);

    public static StoredCredentials Load()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(FilePath)) return StoredCredentials.Empty;

        try
        {
            var protectedBytes = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<StoredCredentials>(
                Encoding.UTF8.GetString(plain), JsonOptions.Default) ?? StoredCredentials.Empty;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or JsonException or UnauthorizedAccessException)
        {
            return StoredCredentials.Empty;
        }
    }

    public static bool Save(StoredCredentials credentials)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var json = JsonSerializer.Serialize(credentials, JsonOptions.Default);
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);

            var temp = FilePath + ".tmp";
            File.WriteAllBytes(temp, encrypted);
            File.Move(temp, FilePath, overwrite: true);

            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (IOException) { }
    }

    public static string Mask(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return "(not set)";

        var value = secret.Trim();
        var underscore = value.LastIndexOf('_');
        var prefix = underscore > 0 && underscore < 16 ? value[..(underscore + 1)] : "";
        var tail = value.Length >= 4 ? value[^4..] : "";

        return $"{prefix}{new string('•', 8)}{tail}";
    }
}
