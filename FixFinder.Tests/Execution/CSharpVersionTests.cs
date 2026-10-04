using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Which C# a program needs - worked out by the C# compiler itself - which C# its project is built as, from Microsoft's
/// table of the C# each target framework comes with, and which .NET SDK builds it. The SDKs are made here, laid out as
/// the .NET installer lays them out, and nothing is run.
/// </summary>
public class CSharpVersionTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private static string Project(string targetFramework, string? langVersion = null) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n" +
        $"    <TargetFramework>{targetFramework}</TargetFramework>\n" +
        (langVersion is null ? "" : $"    <LangVersion>{langVersion}</LangVersion>\n") +
        "  </PropertyGroup>\n</Project>\n";

    /// <summary>A computer whose dotnet has these SDKs, and which builds with the first of them - looked in until disposed.</summary>
    private IDisposable Computer(params string[] sdks)
    {
        var dotnet = Write(@"dotnet\dotnet.exe", "");
        foreach (var sdk in sdks) Write($@"dotnet\sdk\{sdk}\dotnet.dll", "");

        return DotnetSdks.LookingIn(new DotnetSdks.Places(name => name == "dotnet" ? dotnet : null, (_, _) => sdks[0]));
    }

    private static ParsedError CompileError(string file, int line, string code, string message) => new()
    {
        LanguageId = "csharp",
        Confidence = 90,
        RawText = $"{file}({line},5): error {code}: {message}",
        FirstLineSequence = 1,
        ExceptionType = "compile error",
        ErrorCode = code,
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = $"{file}({line},5): error {code}: {message}" }],
    };

    private static CapturedLine Said(string text) => new(1, StreamKind.StdOut, text, TimeSpan.Zero);

    [Theory]
    [InlineData("class Student\n{\n    void Show(string? name) { }\n}\n", "8", "Program.cs uses nullable reference types at line 3, which C# 8 added")]
    [InlineData("System.Console.WriteLine(\"hi\");\n", "9", "Program.cs uses top-level statements at line 1, which C# 9 added")]
    [InlineData("namespace Marks;\n\nclass Student { }\n", "10", "Program.cs uses file-scoped namespace at line 1, which C# 10 added")]
    [InlineData("record struct Point(int X, int Y);\n", "10", "Program.cs uses record structs at line 1, which C# 10 added")]
    [InlineData("class Student\n{\n    public required string Name { get; init; }\n}\n", "11", "Program.cs uses required members at line 3, which C# 11 added")]
    [InlineData("class Student(string name)\n{\n    public string Name => name;\n}\n", "12", "Program.cs uses primary constructors at line 1, which C# 12 added")]
    [InlineData("int[] marks = [1, 2];\nSystem.Console.WriteLine(marks.Length);\n", "12", "Program.cs uses collection expressions at line 1, which C# 12 added")]
    [InlineData("class Marks\n{\n    static int Sum(params System.ReadOnlySpan<int> values) => values.Length;\n}\n", "13", "Program.cs uses params collections at line 3, which C# 13 added")]
    [InlineData("class Node\n{\n    public int Value;\n    static void Set(Node? node) { node?.Value = 1; }\n}\n", "14", "Program.cs uses null conditional assignment at line 4, which no C# up to 13 has")]
    public void TheCSharpTheCodeNeedsIsTheOldestThatCompilesIt(string code, string version, string because)
    {
        var file = Write($@"features{Guid.NewGuid():N}\Program.cs", code);

        var needs = CSharpFeaturesUsed.Of([file]);

        Assert.NotNull(needs);
        Assert.Equal(version, needs!.Version.ToString());
        Assert.Equal(because, needs.Because);
    }

    /// <summary>Code C# 7.3 already compiles needs nothing newer - and a program's own mistakes, wrong as every C#, decide nothing.</summary>
    [Fact]
    public void CodeCSharp73CompilesAndItsOwnMistakesNeedNothingNewer()
    {
        var tuples = Write(@"old\Program.cs", "class Program\n{\n    static void Main() { var pair = (1, 2); System.Console.WriteLine(pair.Item1); }\n}\n");
        var mistaken = Write(@"mistaken\Program.cs", "class Program\n{\n    static void Main() { Console.WriteLine(1) }\n}\n");

        Assert.Null(CSharpFeaturesUsed.Of([tuples]));
        Assert.Null(CSharpFeaturesUsed.Of([mistaken]));
    }

    [Theory]
    [InlineData("net8.0", null, "12", "as App.csproj targets net8.0")]
    [InlineData("net10.0-windows", null, "14", "as App.csproj targets net10.0-windows")]
    [InlineData("net48", null, "7.3", "as App.csproj targets net48")]
    [InlineData("netstandard2.1", null, "8", "as App.csproj targets netstandard2.1")]
    [InlineData("netcoreapp3.1", null, "8", "as App.csproj targets netcoreapp3.1")]
    [InlineData("net8.0", "11", "11", "as App.csproj's LangVersion says 11")]
    [InlineData("net8.0", "7.3", "7.3", "as App.csproj's LangVersion says 7.3")]
    public void TheCSharpAProjectIsBuiltAsIsRead(string targetFramework, string? langVersion, string version, string because)
    {
        var folder = $"project{Guid.NewGuid():N}";
        Write($@"{folder}\App.csproj", Project(targetFramework, langVersion));
        var program = Write($@"{folder}\Program.cs", "class Program { static void Main() { } }\n");

        var builtAs = CSharpBuiltAs.For(program)!;

        Assert.Equal(version, builtAs.Version.ToString());
        Assert.Equal(because, builtAs.Because);
    }

    /// <summary>A LangVersion of latest depends on the compiler, so it is left unsaid; one set in Directory.Build.props is said as that file's.</summary>
    [Fact]
    public void ALangVersionOfLatestIsLeftUnsaidAndOneFromDirectoryBuildPropsIsItsOwn()
    {
        Write(@"latest\App.csproj", Project("net8.0", "latest"));
        var latest = Write(@"latest\Program.cs", "class Program { static void Main() { } }\n");
        Write(@"shared\Directory.Build.props", "<Project>\n  <PropertyGroup>\n    <LangVersion>10</LangVersion>\n  </PropertyGroup>\n</Project>\n");
        Write(@"shared\app\App.csproj", Project("net8.0"));
        var shared = Write(@"shared\app\Program.cs", "class Program { static void Main() { } }\n");

        Assert.Null(CSharpBuiltAs.For(latest));
        Assert.Equal("as Directory.Build.props's LangVersion says 10", CSharpBuiltAs.For(shared)!.Because);
    }

    [Fact]
    public void EveryDotnetSdkIsFoundNewestFirstAndDotnetSaysWhichItUses()
    {
        using var computer = Computer("10.0.400", "8.0.424");

        Assert.Equal(["10.0.400", "8.0.424"], DotnetSdks.Installed.Select(sdk => sdk.VersionText));
        Assert.Equal("10.0.400", DotnetSdks.UsedIn(_temp.Path)!.VersionText);
    }

    [Fact]
    public void HowItRanSaysTheSdkTheCSharpItIsBuiltAsAndWhatItsCodeNeeds()
    {
        using var computer = Computer("10.0.400", "8.0.424");
        Write(@"setup\App.csproj", Project("net8.0"));
        var program = Write(@"setup\Program.cs", "int[] marks = [1, 2];\nSystem.Console.WriteLine(marks.Length);\n");

        var setup = CSharpSetup.For(program)!;

        Assert.Equal("the .NET SDK 10.0.400", setup.Explained);
        Assert.Equal("It is built as C# 12, as App.csproj targets net8.0. Its code needs C# 12 or later: Program.cs uses collection expressions at line 1, " +
                     "which C# 12 added.", setup.Said);
    }

    [Fact]
    public void CodeNewerThanTheCSharpItIsBuiltAsIsSaidAsThat()
    {
        using var computer = Computer("10.0.400", "8.0.424");
        Write(@"older\App.csproj", Project("net7.0"));
        var program = Write(@"older\Program.cs", "int[] marks = [1, 2];\nSystem.Console.WriteLine(marks.Length);\n");

        Assert.Equal("Its code needs C# 12 or later: Program.cs uses collection expressions at line 1, which C# 12 added - and it is built as C# 11, as " +
                     "App.csproj targets net7.0.", CSharpSetup.For(program)!.Said);
    }

    /// <summary>The compiler's own words for code newer than the C# its project is built as: the note says which .NET to target, and that an SDK here builds it.</summary>
    [Fact]
    public void TheCompilersWordsForANewerCSharpSayWhichDotNetToTarget()
    {
        using var computer = Computer("10.0.400", "8.0.424");
        Write(@"target\App.csproj", Project("net8.0"));
        var program = Write(@"target\Program.cs", "class Marks\n{\n    static int Sum(params System.ReadOnlySpan<int> values) => values.Length;\n}\n");
        var error = CompileError(program, 3, "CS9202", "Feature 'params collections' is not available in C# 12.0. Please use language version 13.0 or greater.");

        Assert.Equal("Program.cs:3 uses params collections, which C# 13 added - and it is built as C# 12, as App.csproj targets net8.0, so that part of it " +
                     "cannot be built, which is not a mistake in the code. C# 13 is supported on .NET 9 and later: targeting net9.0 in App.csproj builds it, " +
                     "with the .NET SDK 10.0.400 here.", CSharpVersionErrors.NoteFor([error], [], program));
    }

    [Fact]
    public void WithNoSdkForThatDotNetTheNoteSaysWhichToInstall()
    {
        using var computer = Computer("8.0.424");
        Write(@"install\App.csproj", Project("net8.0"));
        var program = Write(@"install\Program.cs", "class Marks\n{\n    static int Sum(params System.ReadOnlySpan<int> values) => values.Length;\n}\n");
        var error = CompileError(program, 3, "CS9202", "Feature 'params collections' is not available in C# 12.0. Please use language version 13.0 or greater.");

        Assert.Equal("Program.cs:3 uses params collections, which C# 13 added - and it is built as C# 12, as App.csproj targets net8.0, so that part of it " +
                     "cannot be built, which is not a mistake in the code. C# 13 is supported on .NET 9 and later: targeting net9.0 in App.csproj builds it - " +
                     "and needs the .NET 9 SDK or later, for example:\n  winget install Microsoft.DotNet.SDK.9", CSharpVersionErrors.NoteFor([error], [], program));
    }

    [Fact]
    public void ALangVersionThatDecidesIsTheOneToRaise()
    {
        using var computer = Computer("10.0.400");
        Write(@"raise\App.csproj", Project("net9.0", "12"));
        var program = Write(@"raise\Program.cs", "class Marks\n{\n    static int Sum(params System.ReadOnlySpan<int> values) => values.Length;\n}\n");
        var error = CompileError(program, 3, "CS9202", "Feature 'params collections' is not available in C# 12.0. Please use language version 13.0 or greater.");

        Assert.Equal("Program.cs:3 uses params collections, which C# 13 added - and it is built as C# 12, as App.csproj's LangVersion says 12, so that part " +
                     "of it cannot be built, which is not a mistake in the code. Raising the LangVersion in App.csproj to 13 builds it.",
                     CSharpVersionErrors.NoteFor([error], [], program));
    }

    [Fact]
    public void ADotNetFrameworkProjectCanOnlyBeGivenANewerCSharpUnsupported()
    {
        using var computer = Computer("10.0.400");
        Write(@"framework\App.csproj", Project("net48"));
        var program = Write(@"framework\Program.cs", "class Student\n{\n    void Show(string? name) { }\n}\n");
        var error = CompileError(program, 3, "CS8370", "Feature 'nullable reference types' is not available in C# 7.3. Please use language version 8.0 or greater.");

        Assert.Equal("Program.cs:3 uses nullable reference types, which C# 8 added - and it is built as C# 7.3, as App.csproj targets net48, so that part " +
                     "of it cannot be built, which is not a mistake in the code. Setting <LangVersion>8</LangVersion> in App.csproj builds it, though " +
                     "Microsoft does not support a C# newer than the one its target framework comes with.", CSharpVersionErrors.NoteFor([error], [], program));
    }

    [Fact]
    public void ATargetFrameworkNoSdkHereBuildsForSaysWhichSdkToInstall()
    {
        using var computer = Computer("8.0.424");
        Write(@"newTarget\App.csproj", Project("net10.0"));
        var program = Write(@"newTarget\Program.cs", "System.Console.WriteLine(1);\n");

        var note = CSharpVersionErrors.NoteFor([], [Said(@"C:\Program Files\dotnet\sdk\8.0.424\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.TargetFrameworkInference.targets(166,5): " +
                                                        "error NETSDK1045: The current .NET SDK does not support targeting .NET 10.0.  Either target .NET 8.0 or lower, " +
                                                        "or use a version of the .NET SDK that supports .NET 10.0.")], program);

        Assert.Equal("App.csproj targets .NET 10, and the .NET SDK 8.0.424 that built it cannot build for .NET 10 - so it was not built, which is not a " +
                     "mistake in the code. Installing the .NET 10 SDK or later builds it - for example:\n  winget install Microsoft.DotNet.SDK.10", note);
    }

    [Fact]
    public void AGlobalJsonAskingForAnSdkThatIsNotHereIsSaidAsThat()
    {
        using var computer = Computer("10.0.400", "8.0.424");
        Write(@"pinned\global.json", "{ \"sdk\": { \"version\": \"9.0.100\" } }\n");
        Write(@"pinned\App.csproj", Project("net8.0"));
        var program = Write(@"pinned\Program.cs", "System.Console.WriteLine(1);\n");

        var note = CSharpVersionErrors.NoteFor([], [Said("      A compatible .NET SDK was not found."), Said(""), Said("Requested SDK version: 9.0.100")], program);

        Assert.Equal("Its global.json asks for the .NET SDK 9.0.100, and dotnet finds no SDK on this computer that the global.json allows - the SDKs here are " +
                     "10.0.400, 8.0.424 - so it was not built, which is not a mistake in the code. Changing the version global.json asks for to one here, or " +
                     "installing the .NET SDK 9.0.100, builds it.", note);
    }

    /// <summary>Code that needs a C# no SDK here builds for, which that SDK's compiler may not know at all: the note says which SDK to install.</summary>
    [Fact]
    public void CodeNeedingACSharpNoSdkHereHasSaysWhichToInstall()
    {
        using var computer = Computer("8.0.424");
        Write(@"noSdk\App.csproj", Project("net8.0"));
        var program = Write(@"noSdk\Program.cs", "class Marks\n{\n    static int Sum(params System.ReadOnlySpan<int> values) => values.Length;\n}\n");
        var error = CompileError(program, 3, "CS0225", "The params parameter must have a valid collection type");

        Assert.Equal("Program.cs uses params collections at line 3, which C# 13 added - and C# 13 is supported only on .NET 9 and later, which the newest " +
                     ".NET SDK on this computer, 8.0.424, cannot build for, so that part of it cannot be built, which is not a mistake in the code. Installing " +
                     "the .NET 9 SDK or later builds it - for example:\n  winget install Microsoft.DotNet.SDK.9", CSharpVersionErrors.NoteFor([error], [], program));
    }
}

/// <summary>A C# project built with the .NET SDK on this computer, as the window builds it, and what the compiler says of a C# newer than it is built as.</summary>
public class CSharpVersionLiveTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task TheCompilersRealWordsAreTheOnesTheNoteIsMadeFrom()
    {
        if (DotnetSdks.Dotnet is not { } dotnet || DotnetSdks.Installed.All(sdk => sdk.Version.Major < 9)) return;

        File.WriteAllText(Path.Combine(_temp.Path, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        var program = Path.Combine(_temp.Path, "Program.cs");
        File.WriteAllText(program, "System.Console.WriteLine(Sum(1, 2));\n\nstatic int Sum(params System.ReadOnlySpan<int> values) => values.Length;\n");

        var build = await new TargetRunner().RunAsync(new TargetSpec
        {
            ExecutablePath = dotnet,
            Arguments = "build -nologo -v q -clp:NoSummary",
            WorkingDirectory = _temp.Path,
            Timeout = TimeSpan.FromMinutes(3),
        }, CancellationToken.None);

        var errors = new FixFinder.Core.Parsing.Parsers.MsvcParser().ParseAll(build.Lines);
        var note = CSharpVersionErrors.NoteFor(errors, build.Lines, program);

        Assert.StartsWith("Program.cs:3 uses params collections, which C# 13 added - and it is built as C# 12, as App.csproj targets net8.0", note);
    }
}
