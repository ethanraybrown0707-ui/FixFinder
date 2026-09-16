using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Compiling a C# file with the SDK's compiler directly, and the proof that it says what dotnet build says.
/// </summary>
/// <remarks>
/// The live cases compile the same file both ways and require the same exit code, the same errors in the
/// same places, and the same diagnostic lines. They cover what a hand-written compile would most likely
/// get wrong: implicit usings, nullable warnings, source generators, a generator's error with no file,
/// analyzers, warnings the SDK turns into errors, and a missing entry point. Each skips when there is no
/// .NET SDK that can build a lone file.
/// </remarks>
public class CSharpDirectCompileTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------ reading what the build captured

    private static string[] Captured(string probe, string obj) =>
    [
        "\uFEFFroslyn=C:\\nowhere",
        "shared=true",
        "toolpath=",
        "toolexe=",
        "/noconfig",
        "/nullable:enable",
        "/define:TRACE;DEBUG;NET",
        $"/out:{obj}\\Program.dll",
        $"/refout:{obj}\\refint\\Program.dll",
        $"/analyzerconfig:{obj}\\Program.cs.GeneratedMSBuildEditorConfig.editorconfig",
        "/analyzer:\"C:\\Program Files\\dotnet\\packs\\x\\analyzers\\gen.dll\"",
        probe,
        $"{obj}\\Program.cs.GlobalUsings.g.cs",
    ];

    private (string Probe, string Obj) Probe()
    {
        var probe = Path.Combine(_temp.Path, "probe", "Program.cs");
        var obj = Path.Combine(_temp.Path, "artifacts", "obj");
        Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
        Directory.CreateDirectory(obj);

        File.WriteAllText(probe, "Console.WriteLine();\n");
        File.WriteAllText(Path.Combine(obj, "Program.cs.GeneratedMSBuildEditorConfig.editorconfig"),
            $"is_global = true\nbuild_property.EntryPointFilePath = {probe}\nbuild_property.ProjectDir = {Path.GetDirectoryName(probe)}\\\n");
        File.WriteAllText(Path.Combine(obj, "Program.cs.GlobalUsings.g.cs"), "global using System;\n");

        return (probe, obj);
    }

    [Fact]
    public void CapturedArgumentsAreReadIntoWhatChangesAndWhatDoesNot()
    {
        var (probe, obj) = Probe();

        // The reader requires the compiler to exist, so an empty file stands in for the SDK's.
        var roslyn = Path.Combine(_temp.Path, "roslyn");
        Directory.CreateDirectory(Path.Combine(roslyn, "bincore"));
        File.WriteAllText(Path.Combine(roslyn, "bincore", "csc.exe"), "");

        var lines = Captured(probe, obj);
        lines[0] = "\uFEFFroslyn=" + roslyn;

        var template = CSharpDirectCompile.Read(lines, probe);

        Assert.NotNull(template);
        Assert.True(template.Shared);
        Assert.DoesNotContain(template.Arguments, a => a.Text == "/noconfig");
        Assert.Equal(
            [
                CSharpDirectCompile.Part.Verbatim, CSharpDirectCompile.Part.Verbatim, CSharpDirectCompile.Part.Output,
                CSharpDirectCompile.Part.ReferenceOutput, CSharpDirectCompile.Part.GeneratedSettings, CSharpDirectCompile.Part.Verbatim,
                CSharpDirectCompile.Part.Source, CSharpDirectCompile.Part.GeneratedSource,
            ],
            template.Arguments.Select(a => a.Part));

        var folder = Path.Combine(_temp.Path, "check");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, "Program.cs");

        var spec = CSharpDirectCompile.SpecFor(template, copy, folder, TimeSpan.FromMinutes(1));
        var response = File.ReadAllLines(Path.Combine(folder, "obj", "check.rsp"));
        var settings = File.ReadAllText(Path.Combine(folder, "obj", "Program.cs.GeneratedMSBuildEditorConfig.editorconfig"));

        Assert.StartsWith("-shared -nologo -noconfig", spec.Arguments);
        Assert.Contains($"\"{copy}\"", response);
        Assert.Contains($"/out:\"{Path.Combine(folder, "obj", "Program.dll")}\"", response);
        Assert.Contains("/define:TRACE;DEBUG;NET", response);
        Assert.Contains($"build_property.EntryPointFilePath = {copy}", settings);
        Assert.DoesNotContain(Path.GetDirectoryName(probe)!, settings);
    }

    [Fact]
    public void AnArgumentPointingIntoTheCaptureThatIsNotUnderstoodRefusesTheTemplate()
    {
        var (probe, obj) = Probe();
        var roslyn = Path.Combine(_temp.Path, "roslyn2");
        Directory.CreateDirectory(Path.Combine(roslyn, "bincore"));
        File.WriteAllText(Path.Combine(roslyn, "bincore", "csc.exe"), "");

        var lines = Captured(probe, obj).ToList();
        lines[0] = "roslyn=" + roslyn;
        lines.Add($"/pathmap:{obj}=/_/");

        Assert.Null(CSharpDirectCompile.Read(lines, probe));
    }

    [Fact]
    public void ACompilerSwappedInByAPackageRefusesTheTemplate()
    {
        var (probe, obj) = Probe();
        var lines = Captured(probe, obj);
        lines[2] = "toolpath=C:\\packages\\microsoft.net.compilers.toolset\\tasks";

        Assert.Null(CSharpDirectCompile.Read(lines, probe));
    }

    [Fact]
    public void ADiagnosticWithNoFileIsPrintedTheWayMsbuildPrintsIt()
    {
        IReadOnlyList<CapturedLine> lines =
        [
            new CapturedLine(0, StreamKind.StdOut, "error SYSLIB1062: LibraryImportAttribute requires unsafe code.", TimeSpan.Zero),
            new CapturedLine(1, StreamKind.StdOut, "C:\\x\\Program.cs(3,1): error CS1002: ; expected", TimeSpan.Zero),
        ];

        var printed = CSharpDirectCompile.AsBuildPrintsIt(lines);

        Assert.Equal("CSC : error SYSLIB1062: LibraryImportAttribute requires unsafe code.", printed[0].Text);
        Assert.Equal("C:\\x\\Program.cs(3,1): error CS1002: ; expected", printed[1].Text);
    }

    [Fact]
    public void FilesWithDirectivesAreLeftToDotnetBuild()
    {
        Assert.True(CSharpDirectCompile.HasDirectives("#:package Humanizer@2.14.1\nConsole.WriteLine();\n"));
        Assert.False(CSharpDirectCompile.HasDirectives("#nullable enable\nConsole.WriteLine();\n"));
    }

    // ------------------------------------------------------------------ the same answer as dotnet build

    public static TheoryData<string, string, string> Programs => new()
    {
        { "syntax", "CS1002", "Console.WriteLine(1)\n" },
        { "semantic", "CS0103", "string? name = args.Length > 0 ? args[0] : null;\nConsole.WriteLine(name.Length);\nint unused;\nConsole.WriteLine(Undeclared);\nvar list = new List<int>();\nConsole.WriteLine(list.Count());\n" },
        { "generators", "clean", "using System.Text.Json;\nusing System.Text.Json.Serialization;\nusing System.Text.RegularExpressions;\nConsole.WriteLine(Digits.Match().Match(\"a1\").Value);\nConsole.WriteLine(JsonSerializer.Serialize(new Point(1, 2), PointContext.Default.Point));\nrecord Point(int X, int Y);\n[JsonSerializable(typeof(Point))]\npartial class PointContext : JsonSerializerContext { }\npartial class Digits\n{\n    [GeneratedRegex(@\"\\d+\")]\n    public static partial Regex Match();\n}\n" },
        { "no-file", "SYSLIB1062", "using System.Runtime.InteropServices;\nConsole.WriteLine(Native.GetTickCount());\nstatic partial class Native\n{\n    [LibraryImport(\"kernel32\")]\n    public static partial int GetTickCount();\n}\n" },
        { "analyzers", "IL2026", "using System.Diagnostics.CodeAnalysis;\nvar type = Type.GetType(args[0]);\nConsole.WriteLine(type!.GetMethods().Length);\nConsole.WriteLine(\"a\".ToUpper());\nUse();\n[RequiresUnreferencedCode(\"reflection\")]\nstatic void Use() { }\n" },
        { "warning-as-error", "SYSLIB0011", "#pragma warning disable CS0618\nusing System.Runtime.Serialization.Formatters.Binary;\nvar formatter = new BinaryFormatter();\nConsole.WriteLine(formatter);\n" },
        { "no-entry-point", "CS5001", "class Library\n{\n    public int Value => 1;\n}\n" },
        { "clean", "clean", "var names = new List<string> { \"a\", \"b\" };\nforeach (var name in names.Where(n => n.Length > 0)) Console.WriteLine(name);\n" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheCompilerDirectlySaysWhatDotnetBuildSays(string name, string expected, string program)
    {
        if (TargetFactory.FindOnPath("dotnet") is not { } dotnet) return;
        if (await CSharpDirectCompile.TemplateFor("Program.cs", dotnet) is not { } template) return;

        var build = await Compile(name + "-build", program, folder => new TargetSpec
        {
            ExecutablePath = dotnet,
            Arguments = $"build \"{Path.Combine(folder, "Program.cs")}\" -nologo -v q",
            WorkingDirectory = folder,
            Timeout = TimeSpan.FromMinutes(2),
        }, direct: false);

        var direct = await Compile(name + "-direct", program,
            folder => CSharpDirectCompile.SpecFor(template, Path.Combine(folder, "Program.cs"), folder, TimeSpan.FromMinutes(2)),
            direct: true);

        Assert.True(build.Result.Ran);
        Assert.True(
            FasterCheck.Agree(build.Result, build.Folder, direct.Result, direct.Folder, everyLine: false, out var difference),
            difference);

        // And the case exercised what it is named for, rather than agreeing about nothing.
        if (expected == "clean") Assert.True(build.Result.Clean);
        else Assert.Contains(build.Result.Lines, line => line.Text.Contains(expected, StringComparison.Ordinal));
    }

    private async Task<(CheckResult Result, string Folder)> Compile(
        string name, string program, Func<string, TargetSpec> spec, bool direct)
    {
        var folder = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "Program.cs"), program);

        var registry = new ParserRegistry();
        var run = await new TargetRunner(registry).RunAsync(spec(folder), CancellationToken.None);
        var lines = direct ? CSharpDirectCompile.AsBuildPrintsIt(run.Lines) : run.Lines;

        return (new CheckResult(true, run.ExitCode, lines, CompileCheck.ErrorsIn(registry, lines)), folder);
    }
}
