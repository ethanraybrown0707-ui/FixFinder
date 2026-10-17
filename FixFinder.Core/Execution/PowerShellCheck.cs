using System.Security.Cryptography;
using System.Text;

namespace FixFinder.Core.Execution;

/// <summary>
/// How a PowerShell script is checked before it runs: by PowerShell's own parser, which reads the script and runs none of
/// it. A small script of FixFinder's asks the parser, and writes each mistake it found on a line of its own -
/// <c>marks.ps1:4:7: MissingStatementBlockAfterElse: Missing statement block after 'else' keyword.</c> - ending with a
/// failing code when it found any.
/// </summary>
public static class PowerShellCheck
{
    private const string CheckSource = """
        param([string]$Path)
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $Path).Path, [ref]$tokens, [ref]$errors) | Out-Null
        foreach ($found in $errors) {
            '{0}:{1}:{2}: {3}: {4}' -f $Path, $found.Extent.StartLineNumber, $found.Extent.StartColumnNumber, $found.ErrorId, ($found.Message -replace '\r?\n', ' ')
        }
        if ($errors.Count -gt 0) { exit 1 }
        """;

    /// <summary>The arguments that run the check, before the script's name: the check's own file, written once where it is read from.</summary>
    public static string Arguments => $"-NoProfile -ExecutionPolicy Bypass -File \"{Written()}\"";

    /// <summary>Writes the check where PowerShell can read it - the temp folder - named for its own text, and says where.</summary>
    private static string Written()
    {
        var name = $"fixfinder_parse_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CheckSource)))[..12].ToLowerInvariant()}.ps1";
        var check = Path.Combine(Path.GetTempPath(), "FixFinder-tests", name);

        if (!File.Exists(check))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(check)!);
            File.WriteAllText(check, CheckSource, new UTF8Encoding(false));
        }

        return check;
    }
}
