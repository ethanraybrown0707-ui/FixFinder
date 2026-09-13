using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Python that ran the program has in its standard library, asked of that Python.</summary>
/// <remarks>
/// A missing module one letter away from a standard one - <c>maths</c> for <c>math</c> - looks like a
/// typo, and offering <c>pip install maths</c> for it would download a stranger's package to fix a
/// spelling mistake. But closeness alone proves nothing: <c>lxml</c>, <c>grpc</c>, <c>bson</c>,
/// <c>ujson</c> and <c>tomli</c> are all real, popular packages within a letter or two of a standard
/// module. So it counts as a typo only when <b>every name the file uses from it exists on the
/// standard module</b>, which the interpreter is asked directly - <c>maths.sqrt</c> is answered by
/// <c>math</c>; <c>grpc.insecure_channel</c> is not answered by <c>grp</c>.
/// <para>
/// The list comes from <c>sys.stdlib_module_names</c> on the interpreter that crashed, so it is
/// right for that version of Python rather than for whichever one a table was written against.
/// </para>
/// </remarks>
internal static partial class PythonStdlib
{
    private static readonly Dictionary<string, IReadOnlySet<string>> NamesByInterpreter = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Standard modules that do something when imported, which asking about them would set off.</summary>
    private static readonly HashSet<string> NotImported = new(StringComparer.Ordinal) { "antigravity", "this", "__hello__", "__phello__" };

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingDigits();

    /// <summary>Every standard module name that interpreter knows, or none when it cannot be asked.</summary>
    public static IReadOnlySet<string> Names(string? interpreter)
    {
        if (interpreter is not { Length: > 0 }) return new HashSet<string>();

        lock (NamesByInterpreter)
        {
            if (NamesByInterpreter.TryGetValue(interpreter, out var known)) return known;
        }

        var output = RunWithRetry(interpreter, ["-c", "import sys; print(*sorted(sys.stdlib_module_names))"]);
        IReadOnlySet<string> names = new HashSet<string>(
            (output ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(n => Identifier().IsMatch(n)),
            StringComparer.Ordinal);

        // Only a real answer is remembered. A Python that was slow to start once - a busy CI runner - would
        // otherwise leave an empty list behind, and every typo after it would be offered as a package to install.
        if (names.Count == 0) return names;

        lock (NamesByInterpreter)
        {
            NamesByInterpreter[interpreter] = names;
        }

        return names;
    }

    /// <summary>
    /// The standard module a missing one was a misspelling of, or null when there is no good reason
    /// to think it was one.
    /// </summary>
    public static string? TypoOf(string? interpreter, string module, IReadOnlyList<string>? lines)
    {
        if (interpreter is null || lines is null || !Identifier().IsMatch(module)) return null;

        var names = Names(interpreter);
        if (names.Count == 0 || names.Contains(module)) return null;

        if (CodeText.Nearest(module, names.Where(n => !n.StartsWith('_'))) is not { } candidate) return null;

        // cmd2, glob2, pathlib2: a package named after the standard module it extends is not a typo of it.
        if (TrailingDigits().Replace(module, "") == candidate) return null;
        if (NotImported.Contains(candidate)) return null;

        var used = UsedNames(module, lines);
        if (used.Count == 0) return null;

        var answer = RunWithRetry(interpreter,
        [
            "-c",
            "import importlib, sys; m = importlib.import_module(sys.argv[1]); print(all(hasattr(m, a) for a in sys.argv[2:]))",
            candidate,
            .. used,
        ]);

        return answer?.Trim() == "True" ? candidate : null;
    }

    /// <summary>The names a file uses from a module: <c>maths.sqrt</c>, or <c>from maths import sqrt, pi</c>.</summary>
    internal static List<string> UsedNames(string module, IReadOnlyList<string> lines)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var name = Regex.Escape(module);

        foreach (var line in CodeText.MaskAll(lines, Syntax.Python))
        {
            foreach (Match access in Regex.Matches(line, $@"(?<![\w.]){name}\.(?<attr>[A-Za-z_]\w*)"))
                used.Add(access.Groups["attr"].Value);

            if (Regex.Match(line, $@"^\s*from\s+{name}\s+import\s+(?<names>.+)$") is { Success: true } from)
            {
                foreach (var part in from.Groups["names"].Value.Trim('(', ')', ' ').Split(','))
                {
                    var imported = part.Trim().Split(' ')[0];
                    if (Identifier().IsMatch(imported)) used.Add(imported);
                }
            }
        }

        return [.. used.Order(StringComparer.Ordinal)];
    }

    /// <summary>Whether that interpreter's list is remembered - for tests of what a failure leaves behind.</summary>
    internal static bool IsRemembered(string interpreter)
    {
        lock (NamesByInterpreter)
        {
            return NamesByInterpreter.ContainsKey(interpreter);
        }
    }

    /// <summary>One more try when the first gives no answer: starting Python can stall on a busy machine.</summary>
    private static string? RunWithRetry(string interpreter, IReadOnlyList<string> arguments) =>
        Run(interpreter, arguments) ?? Run(interpreter, arguments);

    private static string? Run(string interpreter, IReadOnlyList<string> arguments)
    {
        try
        {
            var start = new ProcessStartInfo(interpreter)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments) start.ArgumentList.Add(argument);

            using var process = Process.Start(start);
            if (process is null) return null;

            process.StandardInput.Close();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();

            var output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(20_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }

                return null;
            }

            return process.ExitCode == 0 && output.Wait(5_000) ? output.Result : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
