using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// A copy of a program made to try a fix in is built and run with the version the program itself is - the same C or
/// C++ standard, the same Python, Go and Node.js, the same .NET SDK - so a fix is tried on the version it will run on,
/// never on a later one it would ask for itself.
/// </summary>
public class FixCopyVersionTests : IDisposable
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

    private string Folder(string relative) => Path.Combine(_temp.Path, relative);

    /// <summary>The original program, and a copy of it with a fix in the same file, remembered as a copy of it until disposed.</summary>
    private (string Original, string Copy, IDisposable Remembered) ProgramAndCopy(string name, string originalCode, string fixedCode)
    {
        var original = Write($@"original\{name}", originalCode);
        var copy = Write($@"copy\{name}", fixedCode);
        ProgramCopy.Remember(Folder("copy"), Folder("original"));
        return (original, copy, new Forgotten(Folder("copy")));
    }

    private sealed class Forgotten(string copy) : IDisposable
    {
        public void Dispose() => ProgramCopy.Forget(copy);
    }

    [Fact]
    public void ACopyOfACOrCppProgramIsBuiltAsTheStandardFoundForIt()
    {
        var (original, copy, remembered) = ProgramAndCopy("main.cpp", "int main() { return 0; }\n", "int main() { return 1; }\n");
        using var _ = remembered;

        NativeStandards.Remember([original], "c++20");

        Assert.Equal("c++20", NativeStandards.RememberedFor(copy));
    }

    [Fact]
    public void ACopyOfAPythonProgramRunsWithThePythonTheProgramDoes()
    {
        var usual = Write(@"Program Files\Python39\python.exe", "");
        Write(@"Program Files\Python39\python39.dll", "");
        Write(@"home\AppData\Local\Programs\Python\Python310\python.exe", "");
        Write(@"home\AppData\Local\Programs\Python\Python310\python310.dll", "");
        using var computer = Pythons.LookingIn(new Pythons.Places(
            Folder("home"), Folder(@"home\AppData\Roaming"), Folder(@"home\AppData\Local"), [Folder("Program Files")],
            name => name == "python" ? usual : null, ReadRegistry: false, Ask: _ => null));

        var (_, copy, remembered) = ProgramAndCopy("app.py", "print('hi')\n", "def show(command):\n    match command:\n        case 'quit':\n            return 0\n");
        using var __ = remembered;

        Assert.Equal(usual, PythonSetup.For(copy, environment: null)!.Interpreter);
    }

    [Fact]
    public void ACopyOfAGoProgramIsBuiltWithTheGoTheProgramIs()
    {
        var usual = Write(@"Program Files\Go\bin\go.exe", "");
        Write(@"Program Files\Go\VERSION", "go1.21.13\n");
        Write(@"home\sdk\go1.22.5\bin\go.exe", "");
        Write(@"home\sdk\go1.22.5\VERSION", "go1.22.5\n");
        using var computer = GoToolchains.LookingIn(new GoToolchains.Places(
            Folder("home"), [Folder("Program Files")], GoRoot: null, ModuleCache: null, FindOnPath: name => name == "go" ? usual : null, Ask: _ => null));

        var (_, copy, remembered) = ProgramAndCopy("main.go", "package main\n\nfunc main() {}\n", "package main\n\nfunc main() {\n\tfor i := range 10 {\n\t\t_ = i\n\t}\n}\n");
        using var __ = remembered;

        Assert.Equal(usual, GoSetup.For(copy)!.Go);
    }

    [Fact]
    public void ACopyOfAJavaScriptProgramRunsWithTheNodeTheProgramDoes()
    {
        var usual = Write(@"Program Files\nodejs\node.exe", "18.20.4");
        Write(@"nvm\versions\v20.11.0\node.exe", "20.11.0");
        Write(@"nvm\settings.txt", $"root: {Folder(@"nvm\versions")}\n");
        using var computer = Nodes.LookingIn(new Nodes.Places([Folder("Program Files")], Folder("nvm"), name => name == "node" ? usual : null, File.ReadAllText));

        var (_, copy, remembered) = ProgramAndCopy("app.js", "console.log('hi');\n", "const sorted = [3, 1].toSorted();\nconsole.log(sorted);\n");
        using var __ = remembered;

        Assert.Equal(usual, NodeSetup.For(copy)!.Node);
    }

    /// <summary>The global.json above a C# project chooses its .NET SDK: a copy of the project takes it along - a copy of anything else does not.</summary>
    [Fact]
    public void ACopyOfACSharpProjectTakesTheGlobalJsonThatChoosesItsSdk()
    {
        Write(@"solution\global.json", "{ \"sdk\": { \"version\": \"8.0.424\" } }\n");
        Write(@"solution\App\App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write(@"solution\App\Program.cs", "System.Console.WriteLine(1);\n");
        Write(@"solution\scripts\app.py", "print('hi')\n");

        Assert.True(ProgramCopy.TryCopyWhole(Folder(@"solution\App"), Folder("projectCopy")));
        Assert.True(ProgramCopy.TryCopyWhole(Folder(@"solution\scripts"), Folder("scriptCopy")));

        Assert.Equal(File.ReadAllText(Folder(@"solution\global.json")), File.ReadAllText(Folder(@"projectCopy\global.json")));
        Assert.False(File.Exists(Folder(@"scriptCopy\global.json")));
    }
}
