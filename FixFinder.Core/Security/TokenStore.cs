using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Engine;

namespace FixFinder.Core.Security;

/// <summary>The two optional credentials FixFinder can hold. Both raise quotas; neither is required.</summary>
/// <param name="GitHubToken">A fine-grained PAT with no permissions selected.</param>
/// <param name="StackExchangeKey">A free Stack Apps key. Not a secret in the usual sense, but stored the same way.</param>
public sealed record StoredCredentials(string? GitHubToken = null, string? StackExchangeKey = null)
{
    public static readonly StoredCredentials Empty = new();
}

/// <summary>
/// Stores API credentials encrypted to the current Windows user account.
/// </summary>
/// <remarks>
/// Uses DPAPI with <see cref="DataProtectionScope.CurrentUser"/>, so the file is decryptable
/// only by this user on this machine - copying it to another machine yields nothing. The extra
/// entropy means a different application that happened to read the file still could not
/// decrypt it, even running as the same user.
/// <para>
/// Neither credential is ever logged, echoed back into its text box, or put in a URL where it
/// could be captured by a proxy or written into the response cache - <see cref="Http.HttpCache"/>
/// strips credential query parameters before anything reaches the disk, which matters because
/// the Stack Exchange key travels as a query parameter rather than a header.
/// </para>
/// </remarks>
public static class TokenStore
{
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("FixFinder.v1"));

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixFinder", "credentials.dat");

    /// <summary>False off Windows, where DPAPI does not exist and nothing is persisted.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    public static bool Exists => File.Exists(FilePath);

    /// <summary>Reads the stored credentials, or empty ones. Never throws.</summary>
    public static StoredCredentials Load()
    {
        // Written as a direct OperatingSystem.IsWindows() call rather than through the
        // IsSupported property above, because that is the form the platform-compatibility
        // analyser recognises as a guard - a property wrapping it reads to the analyser as
        // an unguarded call to a Windows-only API from a cross-platform assembly.
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
            // Written by a different user, or corrupt. Behaving as though nothing is stored is
            // right: the tool works unauthenticated, just with smaller allowances.
            return StoredCredentials.Empty;
        }
    }

    /// <summary>Encrypts and stores the credentials. Returns false if it could not.</summary>
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

    /// <summary>
    /// The only form of a secret that may ever be displayed: prefix, bullets, last four.
    /// </summary>
    /// <remarks>
    /// Keeping the prefix is deliberate - it is not secret, and seeing "github_pat_" tells you
    /// at a glance whether you pasted the right kind of token into the right box.
    /// </remarks>
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
