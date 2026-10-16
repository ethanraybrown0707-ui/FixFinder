using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// Builds an Erlang program with erlc and runs it with erl. The program is the chosen module and the modules beside it that
/// it calls, compiled with every warning -Wall gives into FixFinder's own build folder, so nothing is written into the
/// program's. It is started at the chosen module's own main/0, main/1 or start/0 by a small module of FixFinder's, which
/// prints what stopped the program as Erlang's shell prints it - <c>** exception error: ...</c> and the functions it was
/// in, with their files and lines - and ends with a failing code; no crash dump is written.
/// </summary>
public static partial class ErlangBuild
{
    /// <summary>FixFinder's own module, which starts the program and prints what stopped it.</summary>
    public const string RunnerModule = "fixfinder_run";

    private const string RunnerSource = """
        -module(fixfinder_run).
        -export([main/1]).

        %% Starts the program at its main/0, main/1 or start/0, and when it stops with an exception, prints it as the
        %% Erlang shell does and ends with a failing code.
        main([Module]) ->
            Name = list_to_atom(Module),
            Exports = Name:module_info(exports),
            try
                case {lists:member({main, 0}, Exports), lists:member({main, 1}, Exports)} of
                    {true, _} -> Name:main();
                    {false, true} -> Name:main([]);
                    _ -> Name:start()
                end
            of
                _ -> erlang:halt(0)
            catch
                Class:Reason:Stack ->
                    Trim = fun(M, _F, _A) -> M =:= fixfinder_run orelse M =:= init end,
                    Said = erl_error:format_exception(Class, Reason, Stack, #{stack_trim_fun => Trim}),
                    io:format(standard_error, "** ~ts~n", [Said]),
                    erlang:halt(1)
            end.
        """;

    [GeneratedRegex(@"^\s*-module\(\s*'?(?<name>[a-z][\w@]*)'?\s*\)\s*\.", RegexOptions.Multiline)]
    private static partial Regex ModuleDeclaration();

    [GeneratedRegex(@"^\s*-export\(\s*\[(?<functions>[^\]]*)\]\s*\)\s*\.", RegexOptions.Multiline)]
    private static partial Regex Export();

    /// <summary>A call to a function of another module: <c>helpers:average(Marks)</c>.</summary>
    [GeneratedRegex(@"(?<![\w?:])(?<module>[a-z]\w*):[a-z]\w*\s*\(")]
    private static partial Regex RemoteCall();

    public static string? Erlc() => TargetFactory.FindOnPath("erlc");

    public static string? Erl() => TargetFactory.FindOnPath("erl");

    /// <summary>What Erlang/OTP is installed - "Erlang/OTP 29.0.6" - read from its own OTP_VERSION file; null when there is none.</summary>
    public static string? Version()
    {
        if (Erl() is not { } erl || Directory.GetParent(Path.GetDirectoryName(erl)!) is not { } root) return null;

        try
        {
            var releases = Path.Combine(root.FullName, "releases");
            if (!Directory.Exists(releases)) return null;

            var newest = Directory.EnumerateFiles(releases, "OTP_VERSION", SearchOption.AllDirectories).OrderByDescending(file => file, StringComparer.Ordinal).FirstOrDefault();
            return newest is null ? null : $"Erlang/OTP {File.ReadAllText(newest).Trim()}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The module a file declares - marks for -module(marks). - or, with none, the file's own name.</summary>
    public static string ModuleOf(string file) =>
        TextOf(file) is { } text && ModuleDeclaration().Match(text) is { Success: true } declared ? declared.Groups["name"].Value : Path.GetFileNameWithoutExtension(file);

    /// <summary>The function the program starts at - main/0, main/1 or start/0, the first of them the module exports - or null with none.</summary>
    public static string? EntryOf(string file)
    {
        var exported = Export().Matches(TextOf(file) ?? "")
            .SelectMany(export => export.Groups["functions"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(function => Regex.Replace(function, @"\s+", ""))
            .ToHashSet(StringComparer.Ordinal);

        return new[] { "main/0", "main/1", "start/0" }.FirstOrDefault(exported.Contains);
    }

    /// <summary>
    /// The chosen file and the .erl files beside it that define the modules it calls, and the ones those call - the chosen
    /// file first.
    /// </summary>
    public static IReadOnlyList<string> Files(string chosen)
    {
        var whole = Path.GetFullPath(chosen);
        var folder = Path.GetDirectoryName(whole)!;
        var definedIn = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(folder, "*.erl"))
            definedIn.TryAdd(ModuleOf(file), Path.GetFullPath(file));

        var ordered = new List<string>();
        var waiting = new Queue<string>([whole]);

        while (waiting.TryDequeue(out var file))
        {
            if (ordered.Contains(file, StringComparer.OrdinalIgnoreCase)) continue;
            ordered.Add(file);

            var code = string.Join("\n", LocalFixes.CodeText.MaskAll((TextOf(file) ?? "").Split('\n'), LocalFixes.Syntax.Erlang));
            foreach (Match call in RemoteCall().Matches(code))
                if (definedIn.TryGetValue(call.Groups["module"].Value, out var definer)) waiting.Enqueue(definer);
        }

        return ordered;
    }

    public static (BuildAndRun? Plan, string? Problem) Prepare(string source, string output, TimeSpan timeout)
    {
        if (Erlc() is not { } erlc || Erl() is not { } erl)
        {
            return (null,
                $"{Path.GetFileName(source)} is Erlang, which has to be compiled before it can run, and no erlc and erl were found on PATH.\n\n" +
                "Install Erlang/OTP, and put its bin folder on PATH.");
        }

        if (EntryOf(source) is not { } entry)
        {
            return (null,
                $"{Path.GetFileName(source)} exports no main/0, main/1 or start/0, so there is nothing to start it with. Export the " +
                "function that starts the program - -export([main/0]). - for FixFinder to run it.");
        }

        var files = Files(source);
        var folder = Path.GetDirectoryName(Path.GetFullPath(source))!;

        var compile = new TargetSpec
        {
            ExecutablePath = erlc,
            Arguments = CompileArguments(output, [Runner(), .. files.Select(file => Path.GetRelativePath(folder, file))]),
            WorkingDirectory = folder,
            Timeout = timeout,
        };

        var run = new TargetSpec
        {
            ExecutablePath = erl,
            Arguments = $"-noshell -pa \"{output}\" -run {RunnerModule} main {ModuleOf(source)}",
            WorkingDirectory = folder,
            Timeout = timeout,
            ExtraEnvironment = Environment,
        };

        var together = files.Count > 1 ? $" with {string.Join(", ", files.Skip(1).Select(Path.GetFileName))}, whose functions it calls," : "";
        return (new BuildAndRun(compile, run, $"Compiling {Path.GetFileName(source)}{together} with erlc -Wall, then running it from its {entry}."), null);
    }

    /// <summary>No crash dump: Erlang writes erl_crash.dump into the folder a program runs in when it stops during boot.</summary>
    public static IReadOnlyDictionary<string, string> Environment { get; } = new Dictionary<string, string> { ["ERL_CRASH_DUMP_SECONDS"] = "0" };

    /// <summary>erlc's arguments: every warning -Wall gives, where the compiled modules go, and the files.</summary>
    public static string CompileArguments(string outputFolder, IEnumerable<string> files) =>
        $"-Wall -o \"{outputFolder}\" " + string.Join(" ", files.Select(file => $"\"{file}\""));

    /// <summary>Writes FixFinder's starting module where erlc can read it - the temp folder - named for its own text, and says where.</summary>
    public static string Runner()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-tests", $"erlang_run_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RunnerSource)))[..12].ToLowerInvariant()}");
        var runner = Path.Combine(folder, RunnerModule + ".erl");

        if (!File.Exists(runner))
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(runner, RunnerSource, new UTF8Encoding(false));
        }

        return runner;
    }

    private static string? TextOf(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
