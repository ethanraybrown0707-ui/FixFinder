using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>Built-in names asked of Node itself, so they are right for the Node that ran the program.</summary>
internal static partial class NodeRuntime
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Cache = new(StringComparer.Ordinal);

    [GeneratedRegex(@"^[A-Za-z]+(?:\.prototype)?$")]
    private static partial Regex Expression();

    [GeneratedRegex(@"^(?:node:)?[a-z_][a-z0-9_/]*$")]
    private static partial Regex ModuleName();

    private const string ObjectMembers = " constructor hasOwnProperty isPrototypeOf propertyIsEnumerable toLocaleString toString valueOf";

    internal static readonly Dictionary<string, string[]> Standard = new(StringComparer.Ordinal)
    {
        ["String.prototype"] = ("length at charAt charCodeAt codePointAt concat endsWith includes indexOf lastIndexOf localeCompare match matchAll " +
                                "normalize padEnd padStart repeat replace replaceAll search slice split startsWith substring toLocaleLowerCase " +
                                "toLocaleUpperCase toLowerCase toUpperCase trim trimEnd trimStart" + ObjectMembers).Split(' '),
        ["Array.prototype"] = ("length at concat copyWithin entries every fill filter find findIndex findLast findLastIndex flat flatMap forEach " +
                               "includes indexOf join keys lastIndexOf map pop push reduce reduceRight reverse shift slice some sort splice " +
                               "unshift values" + ObjectMembers).Split(' '),
        ["Set.prototype"] = ("add clear delete entries forEach has keys size values" + ObjectMembers).Split(' '),
        ["Map.prototype"] = ("clear delete entries forEach get has keys set size values" + ObjectMembers).Split(' '),
        ["Math"] = ("abs acos acosh asin asinh atan atan2 atanh cbrt ceil clz32 cos cosh exp expm1 floor fround hypot imul log log10 log1p log2 " +
                    "max min pow random round sign sin sinh sqrt tan tanh trunc E LN10 LN2 LOG10E LOG2E PI SQRT1_2 SQRT2" + ObjectMembers).Split(' '),
        ["console"] = ("log error warn info debug trace dir table time timeEnd timeLog assert count countReset group groupEnd groupCollapsed clear" +
                       ObjectMembers).Split(' '),
        ["builtins"] = ("assert async_hooks buffer child_process cluster console crypto dgram dns events fs http http2 https inspector module net " +
                        "os path perf_hooks process querystring readline repl stream string_decoder timers tls tty url util v8 vm worker_threads zlib").Split(' '),
    };

    public static IReadOnlyList<string> Members(string expression) =>
        !Expression().IsMatch(expression)
            ? []
            : AskOrStandard(expression, $"members:{expression}",
                $"let o = {expression}; const names = new Set(); while (o) {{ Object.getOwnPropertyNames(o).forEach(n => names.add(n)); o = Object.getPrototypeOf(o); }} console.log([...names].join(' '))");

    public static IReadOnlyList<string> BuiltinModules() =>
        AskOrStandard("builtins", "builtins", "console.log(require('module').builtinModules.join(' '))");

    internal static IReadOnlyList<string> AskNode(string expression) =>
        Expression().IsMatch(expression)
            ? Ask($"members:{expression}",
                $"let o = {expression}; const names = new Set(); while (o) {{ Object.getOwnPropertyNames(o).forEach(n => names.add(n)); o = Object.getPrototypeOf(o); }} console.log([...names].join(' '))")
            : expression == "builtins" ? Ask("builtins", "console.log(require('module').builtinModules.join(' '))") : [];

    private static IReadOnlyList<string> AskOrStandard(string standard, string key, string script) =>
        Ask(key, script) is { Count: > 0 } answer ? answer : Standard.GetValueOrDefault(standard, []);

    public static IReadOnlyList<string> Exports(string module)
    {
        var bare = module.StartsWith("node:", StringComparison.Ordinal) ? module[5..] : module;
        if (!ModuleName().IsMatch(module) || !BuiltinModules().Contains(bare)) return [];

        return Ask($"exports:{bare}", $"console.log(Object.keys(require('{bare}')).join(' '))");
    }

    private static IReadOnlyList<string> Ask(string key, string script)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var known)) return known;
        }

        if (TargetFactory.FindOnPath("node") is not { } node) return [];

        var output = Run(node, script) ?? Run(node, script);
        var names = (output ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (names.Length == 0) return names;

        lock (Cache)
        {
            Cache[key] = names;
        }

        return names;
    }

    private static string? Run(string node, string script)
    {
        try
        {
            var start = new ProcessStartInfo(node)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };

            start.ArgumentList.Add("-e");
            start.ArgumentList.Add(script);

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
