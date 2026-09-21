using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes;

/// <summary>Compiles one C# file the way <c>dotnet build</c> does, by running the compiler it runs with the arguments it passes -
/// without the second of MSBuild around it.</summary>
internal static partial class CSharpDirectCompile
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<Template?>>> Templates = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, FasterCheck.Trust> Trusts = new(StringComparer.OrdinalIgnoreCase);

    internal static FasterCheck.Trust TrustFor(string fileName) => Trusts.GetOrAdd(fileName, _ => new FasterCheck.Trust());

    /// <summary>What an argument does once the file being checked is known.</summary>
    internal enum Part
    {
        Verbatim,
        Source,
        Output,
        ReferenceOutput,
        GeneratedSource,
        GeneratedSettings,
    }

    internal sealed record Argument(Part Part, string Text, string? FileName = null, string? Content = null);

    /// <summary>The compile <c>dotnet build</c> runs for a file of one name, with the paths that change taken out.</summary>
    internal sealed record Template(string Compiler, bool Shared, IReadOnlyList<Argument> Arguments, string ProbeFolder);

    internal static TargetSpec SpecFor(Template template, string copy, string folder, TimeSpan timeout)
    {
        var obj = Path.Combine(folder, "obj");
        Directory.CreateDirectory(Path.Combine(obj, "refint"));

        var lines = new List<string>(template.Arguments.Count);

        foreach (var argument in template.Arguments)
        {
            switch (argument.Part)
            {
                case Part.Source:
                    lines.Add(Quote(copy));
                    break;

                case Part.Output:
                    lines.Add("/out:" + Quote(Path.Combine(obj, argument.FileName!)));
                    break;

                case Part.ReferenceOutput:
                    lines.Add("/refout:" + Quote(Path.Combine(obj, "refint", argument.FileName!)));
                    break;

                case Part.GeneratedSource or Part.GeneratedSettings:
                    var path = Path.Combine(obj, argument.FileName!);
                    File.WriteAllText(path, argument.Content!.Replace(template.ProbeFolder, folder, StringComparison.Ordinal), new UTF8Encoding(false));
                    lines.Add(argument.Part == Part.GeneratedSettings ? "/analyzerconfig:" + Quote(path) : Quote(path));
                    break;

                default:
                    lines.Add(argument.Text);
                    break;
            }
        }

        var response = Path.Combine(obj, "check.rsp");
        File.WriteAllLines(response, lines, new UTF8Encoding(false));

        return new TargetSpec
        {
            ExecutablePath = template.Compiler,
            Arguments = $"{(template.Shared ? "-shared " : "")}-nologo -noconfig \"@{response}\"",
            WorkingDirectory = folder,
            Timeout = timeout,
        };
    }

    internal static IReadOnlyList<CapturedLine> AsBuildPrintsIt(IReadOnlyList<CapturedLine> lines) =>
        lines.Select(line => Unplaced().IsMatch(line.Text) ? line with { Text = "CSC : " + line.Text } : line).ToList();

    [GeneratedRegex(@"^(?:error|warning|info) [A-Za-z]+\d+: ")]
    private static partial Regex Unplaced();

    internal static Task<Template?> TemplateFor(string fileName, string dotnet) =>
        Templates.GetOrAdd(fileName, name => new Lazy<Task<Template?>>(() => Task.Run(() => CaptureAsync(name, dotnet)))).Value;

    internal static bool HasDirectives(string text) =>
        text.Split('\n').Any(line => line.TrimStart().StartsWith("#:", StringComparison.Ordinal));

    private const string CaptureTargets = """
        <Project>
          <Target Name="FixFinderCaptureCompile" AfterTargets="CoreCompile" Condition="'$(FixFinderCompileArguments)' != ''">
            <WriteLinesToFile File="$(FixFinderCompileArguments)"
                              Lines="roslyn=$(RoslynTargetsPath);shared=$(UseSharedCompilation);toolpath=$(CscToolPath);toolexe=$(CscToolExe);@(CscCommandLineArgs)"
                              Overwrite="true" Encoding="utf-8" />
          </Target>
        </Project>
        """;

    private static async Task<Template?> CaptureAsync(string fileName, string dotnet)
    {
        var probeFolder = Path.Combine(CompileCheck.Root, "csharp-" + Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(probeFolder);

            var probe = Path.Combine(probeFolder, fileName);
            var targets = Path.Combine(probeFolder, "capture.targets");
            var captured = Path.Combine(probeFolder, "arguments.txt");

            await File.WriteAllTextAsync(probe, "Console.WriteLine();\n");
            await File.WriteAllTextAsync(targets, CaptureTargets);

            var spec = new TargetSpec
            {
                ExecutablePath = dotnet,
                Arguments =
                    $"build \"{probe}\" -nologo -v q -p:ProvideCommandLineArgs=true -p:SkipCompilerExecution=true " +
                    $"\"-p:CustomAfterMicrosoftCommonTargets={targets}\" \"-p:FixFinderCompileArguments={captured}\"",
                WorkingDirectory = probeFolder,
                Timeout = TimeSpan.FromMinutes(2),
            };

            var run = await new TargetRunner(new ParserRegistry()).RunAsync(spec, CancellationToken.None);

            if (run.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut or RunOutcome.Cancelled || !File.Exists(captured))
                return null;

            return Read(await File.ReadAllLinesAsync(captured), probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(probeFolder)) Directory.Delete(probeFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    internal static Template? Read(IReadOnlyList<string> lines, string probe)
    {
        if (lines.Count < 5) return null;

        string? Setting(int index, string name) =>
            lines[index].TrimStart('﻿') is var line && line.StartsWith(name + "=", StringComparison.Ordinal) ? line[(name.Length + 1)..] : null;

        if (Setting(0, "roslyn") is not { Length: > 0 } roslyn || Setting(1, "shared") is not { } shared ||
            Setting(2, "toolpath") is not "" || Setting(3, "toolexe") is not "")
            return null;

        var compiler = Path.Combine(Path.GetFullPath(roslyn), "bincore", "csc.exe");
        if (!File.Exists(compiler)) return null;

        var probeFolder = Path.GetDirectoryName(probe)!;
        var raw = lines.Skip(4).ToList();

        if (raw.FirstOrDefault(a => a.StartsWith("/out:", StringComparison.Ordinal)) is not { } output) return null;

        var obj = Path.GetDirectoryName(Unquote(output[5..]))!;
        var arguments = new List<Argument>();

        foreach (var text in raw)
        {
            if (text == "/noconfig") continue;

            if (Unquote(text).Equals(probe, StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add(new Argument(Part.Source, text));
            }
            else if (text.StartsWith("/out:", StringComparison.Ordinal))
            {
                arguments.Add(new Argument(Part.Output, text, Path.GetFileName(Unquote(text[5..]))));
            }
            else if (text.StartsWith("/refout:", StringComparison.Ordinal))
            {
                arguments.Add(new Argument(Part.ReferenceOutput, text, Path.GetFileName(Unquote(text[8..]))));
            }
            else if (!text.StartsWith('/') && Inside(Unquote(text), obj))
            {
                var path = Unquote(text);
                arguments.Add(new Argument(Part.GeneratedSource, text, Path.GetFileName(path), File.ReadAllText(path)));
            }
            else if (text.StartsWith("/analyzerconfig:", StringComparison.Ordinal) && Inside(Unquote(text[16..]), obj))
            {
                var path = Unquote(text[16..]);
                arguments.Add(new Argument(Part.GeneratedSettings, text, Path.GetFileName(path), File.ReadAllText(path)));
            }
            else if (text.Contains(obj, StringComparison.OrdinalIgnoreCase) || text.Contains(probeFolder, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            else
            {
                arguments.Add(new Argument(Part.Verbatim, text));
            }
        }

        if (arguments.Count(a => a.Part == Part.Source) != 1) return null;

        var generated = arguments.Where(a => a.FileName is not null && a.Part is Part.GeneratedSource or Part.GeneratedSettings).Select(a => a.FileName!);
        if (generated.Count() != generated.Distinct(StringComparer.OrdinalIgnoreCase).Count()) return null;

        return new Template(compiler, !shared.Equals("false", StringComparison.OrdinalIgnoreCase), arguments, probeFolder);
    }

    private static bool Inside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    private static string Quote(string path) => $"\"{path}\"";
}
