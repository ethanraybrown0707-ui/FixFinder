using System.Collections.Concurrent;
using System.Diagnostics;

namespace FixFinder.Core.Execution;

/// <summary>
/// The languages run straight from their source by an interpreter of their own - Ruby, PHP, Perl and Lua - and how each
/// is checked before it runs: by the language's own syntax check, which reads the file and runs none of it.
/// </summary>
/// <remarks>
/// <para>ruby -wc reads a file and says Syntax OK, or its syntax error - and, with -w, its warnings.</para>
/// <para>php -l, PHP's lint, says No syntax errors detected, or its parse error.</para>
/// <para>
/// perl -wc compiles a file without running it - though a BEGIN block, or a use, runs while it compiles, as it would when
/// the program runs - and says syntax OK, or its errors and warnings.
/// </para>
/// <para>luac -p, Lua's compiler told only to parse, says nothing for a file that reads, and its error for one that does not.</para>
/// <para>
/// dart analyze lists every error and warning in a file, and runs none of it; with --no-fatal-warnings it fails only for an
/// error. Unlike dart run, it never fetches a package's dependencies.
/// </para>
/// The check runs in the file's own folder and is given the file's name, so what it says names the file as it is there.
/// </remarks>
public static class ScriptLanguages
{
    /// <summary>A language run by an interpreter: its name, its files, the program that checks it and how, and its version's flag.</summary>
    /// <param name="Checker">The program that checks a file: the interpreter, or Lua's compiler beside it.</param>
    /// <param name="CheckArguments">The checker's arguments before the file's name.</param>
    /// <param name="Interpreter">The program that runs it, as the run table names it.</param>
    /// <param name="VersionArguments">What makes the interpreter say its version.</param>
    public sealed record ScriptLanguage(string Name, string LanguageId, string[] Extensions, string Checker, string CheckArguments, string Interpreter, string VersionArguments);

    public static ScriptLanguage Ruby { get; } = new("Ruby", "ruby", [".rb"], "ruby", "-wc", "ruby", "-v");

    public static ScriptLanguage Php { get; } = new("PHP", "php", [".php"], "php", "-l", "php", "-v");

    public static ScriptLanguage Perl { get; } = new("Perl", "perl", [".pl", ".pm"], "perl", "-wc", "perl", "-v");

    public static ScriptLanguage Lua { get; } = new("Lua", "lua", [".lua"], "luac", "-p", "lua", "-v");

    public static ScriptLanguage Dart { get; } = new("Dart", "dart", [".dart"], "dart", "analyze --no-fatal-warnings", "dart", "--version");

    /// <summary>Windows PowerShell, checked by its own parser through FixFinder's small check script, which runs none of the script.</summary>
    public static ScriptLanguage PowerShell { get; } =
        new("PowerShell", "powershell", [".ps1"], "powershell", PowerShellCheck.Arguments, "powershell", "-NoProfile -Command $PSVersionTable.PSVersion.ToString()");

    public static IReadOnlyList<ScriptLanguage> All { get; } = [Ruby, Php, Perl, Lua, Dart, PowerShell];

    public static ScriptLanguage? Of(string file)
    {
        var extension = Path.GetExtension(file);
        return All.FirstOrDefault(language => language.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The program that checks a language's files, or null when it is not on PATH.</summary>
    public static string? CheckerOf(ScriptLanguage language) => TargetFactory.FindOnPath(language.Checker);

    /// <summary>How a file is checked: the checker, its arguments with the file's name, in the file's folder.</summary>
    public static TargetSpec? CheckOf(ScriptLanguage language, string file, TimeSpan timeout) =>
        CheckerOf(language) is { } checker
            ? new TargetSpec
            {
                ExecutablePath = checker,
                Arguments = $"{language.CheckArguments} \"{Path.GetFileName(file)}\"",
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(file))!,
                Timeout = timeout,
            }
            : null;

    private static readonly ConcurrentDictionary<string, (DateTime Written, string? Version)> Asked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the interpreter on PATH says its version is - its first line that says anything: "ruby 3.3.12 (2026-07-16
    /// revision 0581089df9) [x64-mingw-ucrt]" - asked once while the program is unchanged; null when it is not there.
    /// </summary>
    public static string? VersionOf(ScriptLanguage language)
    {
        if (TargetFactory.FindOnPath(language.Interpreter) is not { } interpreter) return null;

        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(interpreter);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (Asked.TryGetValue(interpreter, out var known) && known.Written == written) return known.Version;

        var version = Ask(interpreter, language.VersionArguments);
        Asked[interpreter] = (written, version);
        return version;
    }

    private static string? Ask(string interpreter, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(interpreter, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null) return null;

            var said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000)) return null;

            return said.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>"Ruby       : ruby 3.3.12 (...): C:\Ruby33-x64\bin\ruby.exe", for Settings' list of what was found.</summary>
    public static string Describe(ScriptLanguage language) =>
        TargetFactory.FindOnPath(language.Interpreter) is { } interpreter && VersionOf(language) is { } version
            ? $"{language.Name,-11}: {version}: {interpreter}"
            : $"{language.Name,-11}: not found";
}
