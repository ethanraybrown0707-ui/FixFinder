using System.Diagnostics;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Tests;

/// <summary>
/// Which Node.js a JavaScript program needs and runs with: what its code uses - each part at the Node.js MDN's browser
/// compatibility data says added it - what its project declares, and which of the Node.js installs on the computer runs
/// it. The installs are made here, laid out as their installers lay them out, each node.exe holding only its version.
/// </summary>
public class NodeVersionTests : IDisposable
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

    /// <summary>A node.exe that holds its version, which the computer below reads as the version a real one's file gives.</summary>
    private string FakeNode(string relative, string version) => Write(Path.Combine(relative, "node.exe"), version);

    /// <summary>A computer with the given node on PATH, Node.js in Program Files, and nvm for Windows rooted in nvm\versions - looked in until disposed.</summary>
    private IDisposable Computer(string? onPath)
    {
        Write(@"nvm\settings.txt", $"root: {Folder(@"nvm\versions")}\npath: C:\\nvm4w\\nodejs\n");

        return Nodes.LookingIn(new Nodes.Places(
            [Folder("Program Files")],
            NvmHome: Folder("nvm"),
            FindOnPath: name => name == "node" ? onPath : null,
            VersionOf: File.ReadAllText));
    }

    [Theory]
    [InlineData("const name = user?.profile?.name;\n", "14", "app.js uses optional chaining, ?. at line 1, which Node.js 14 added")]
    [InlineData("const port = process.env.PORT ?? 3000;\n", "14", "app.js uses ?? at line 1, which Node.js 14 added")]
    [InlineData("let count;\ncount ??= 0;\n", "15", "app.js uses ??= at line 2, which Node.js 15 added")]
    [InlineData("const own = Object.hasOwn(settings, 'theme');\n", "16.9", "app.js calls Object.hasOwn at line 1, which Node.js 16.9 added")]
    [InlineData("const copy = structuredClone({ a: 1 });\n", "17", "app.js calls structuredClone at line 1, which Node.js 17 added")]
    [InlineData("const last = [1, 2, 3].findLast(n => n > 1);\n", "18", "app.js calls an array's findLast at line 1, which Node.js 18 added")]
    [InlineData("async function load(url) {\n  const response = await fetch(url);\n  return response.json();\n}\n", "18", "app.js calls fetch at line 2, which Node.js 18 added")]
    [InlineData("const sorted = marks.toSorted((a, b) => a - b);\n", "20", "app.js calls an array's toSorted at line 1, which Node.js 20 added")]
    [InlineData("const byHouse = Object.groupBy(students, s => s.house);\n", "21", "app.js calls Object.groupBy at line 1, which Node.js 21 added")]
    [InlineData("const { promise, resolve } = Promise.withResolvers();\n", "22", "app.js calls Promise.withResolvers at line 1, which Node.js 22 added")]
    [InlineData("const parsed = Promise.try(() => JSON.parse(text));\n", "23", "app.js calls Promise.try at line 1, which Node.js 23 added")]
    [InlineData("const pattern = new RegExp(RegExp.escape(word));\n", "24", "app.js calls RegExp.escape at line 1, which Node.js 24 added")]
    public void APartOfJavaScriptIsFoundWithTheNodeThatAddedIt(string code, string version, string because)
    {
        var file = Write($@"features{Guid.NewGuid():N}\app.js", code);

        var needs = JavaScriptFeaturesUsed.Of([file]);

        Assert.NotNull(needs);
        Assert.Equal(version, needs!.Version.ToString());
        Assert.Equal(because, needs.Because);
    }

    /// <summary>
    /// What only looks like a newer part of JavaScript is not counted: lodash's _.findLast, a fetch of the program's own, a
    /// method of its own called findLast, a ternary before a number, a regular expression, text and comments.
    /// </summary>
    [Fact]
    public void WhatOnlyLooksLikeANewerPartOfJavaScriptIsNotCounted()
    {
        var file = Write(@"quiet\app.js", """
            const _ = require('lodash');
            const fetch = require('node-fetch');

            class Shelf {
              findLast(title) {
                return this.books.find(book => book.title === title);
              }
            }

            // const x = a?.b ?? c; Object.groupBy(list)
            const text = "items.toSorted() and structuredClone(x) and ??=";
            const pattern = /a?.b/;
            const last = _.findLast(users, user => user.active);
            const answer = ready ? .5 : 1;
            fetch('https://example.com').then(r => r.json());
            """);

        Assert.Null(JavaScriptFeaturesUsed.Of([file]));
    }

    [Theory]
    [InlineData(".nvmrc", "v20.11.0\n", "20.11.0", "20", ".nvmrc names Node.js v20.11.0")]
    [InlineData(".node-version", "22\n", "22", "22", ".node-version names Node.js 22")]
    [InlineData("package.json", "{ \"volta\": { \"node\": \"20.11.0\" } }", "20.11.0", "20", "package.json's volta pins Node.js 20.11.0")]
    [InlineData("package.json", "{ \"engines\": { \"node\": \">=18\" } }", "18", null, "package.json's engines say node >=18")]
    [InlineData("package.json", "{ \"engines\": { \"node\": \"^20.11.0\" } }", "20.11.0", "20", "package.json's engines say node ^20.11.0")]
    [InlineData("package.json", "{ \"engines\": { \"node\": \"~20.11\" } }", "20.11", "20.11", "package.json's engines say node ~20.11")]
    [InlineData("package.json", "{ \"engines\": { \"node\": \">=18 <21\" } }", "18", "20", "package.json's engines say node >=18 <21")]
    [InlineData("package.json", "{ \"engines\": { \"node\": \"^18 || ^20\" } }", "18", "20", "package.json's engines say node ^18 || ^20")]
    [InlineData("package.json", "{ \"engines\": { \"node\": \"18 - 20\" } }", "18", "20", "package.json's engines say node 18 - 20")]
    [InlineData("package.json", "{ \"engines\": { \"node\": \">20\" } }", "21", null, "package.json's engines say node >20")]
    [InlineData("package.json", "{ \"engines\": { \"node\": \"<20.0.5\" } }", null, "20.0.4", "package.json's engines say node <20.0.5")]
    public void WhatTheProjectDeclaresIsRead(string fileName, string text, string? atLeast, string? atMost, string saidBy)
    {
        var folder = $"declared{Guid.NewGuid():N}";
        Write($@"{folder}\{fileName}", text);
        var app = Write($@"{folder}\app.js", "console.log('hi');\n");

        var declared = DeclaredNode.Of(app)!;

        Assert.Equal(atLeast, declared.AtLeast?.Version.ToString());
        Assert.Equal(atMost, declared.AtMost?.Version.ToString());
        Assert.Equal(saidBy, (declared.AtLeast?.Because ?? declared.AtMost?.Because));
    }

    /// <summary>A name only a version manager can turn into a version - lts/iron - is not guessed at.</summary>
    [Fact]
    public void AVersionManagersOwnNameIsNotGuessedAt()
    {
        Write(@"named\.nvmrc", "lts/iron\n");
        var app = Write(@"named\app.js", "console.log('hi');\n");

        Assert.Null(DeclaredNode.Of(app));
    }

    [Fact]
    public void EveryNodeInTheUsualPlacesIsFoundNewestFirst()
    {
        var usual = FakeNode(@"Program Files\nodejs", "20.11.0");
        FakeNode(@"nvm\versions\v22.12.0", "22.12.0");
        FakeNode(@"nvm\versions\v18.20.4", "18.20.4");
        using var computer = Computer(onPath: usual);

        var found = Nodes.Installed;

        Assert.Equal(["22.12.0", "20.11.0", "18.20.4"], found.Select(node => node.VersionText));
        Assert.Equal(["nvm's", "on PATH", "nvm's"], found.Select(node => node.FoundIn));
    }

    /// <summary>The usual Node.js runs a program it is new enough for - and how it ran still says what the code needs.</summary>
    [Fact]
    public void TheUsualNodeRunsCodeItIsNewEnoughFor()
    {
        var usual = FakeNode(@"Program Files\nodejs", "22.12.0");
        using var computer = Computer(onPath: usual);
        var app = Write(@"usual\app.js", "const byHouse = Object.groupBy(students, s => s.house);\n");

        var setup = NodeSetup.For(app)!;

        Assert.Equal(usual, setup.Node);
        Assert.Equal("Node.js 22.12.0 (on PATH)", setup.Explained);
        Assert.Equal("Its code needs Node.js 21 or later: app.js calls Object.groupBy at line 1, which Node.js 21 added.", setup.CodeNeeds);
    }

    /// <summary>An array's toSorted is Node.js 20's: with the usual Node.js of 18, the oldest one nvm has that is new enough runs it.</summary>
    [Fact]
    public void ANewerNodeRunsCodeTheUsualOneIsTooOldFor()
    {
        var usual = FakeNode(@"Program Files\nodejs", "18.20.4");
        var nearest = FakeNode(@"nvm\versions\v20.11.0", "20.11.0");
        FakeNode(@"nvm\versions\v22.12.0", "22.12.0");
        using var computer = Computer(onPath: usual);
        var app = Write(@"newer\app.js", "const sorted = marks.toSorted((a, b) => a - b);\n");

        var setup = NodeSetup.For(app)!;

        Assert.Equal(nearest, setup.Node);
        Assert.Equal("Node.js 20.11.0 (nvm's), as app.js calls an array's toSorted at line 1, which Node.js 20 added", setup.Explained);
    }

    /// <summary>A Node.js the project rules out is not chosen, even when it is the usual one: an .nvmrc of 20 keeps 22 out.</summary>
    [Fact]
    public void WhatTheProjectDeclaresDecidesAmongTheNodes()
    {
        var usual = FakeNode(@"Program Files\nodejs", "22.12.0");
        var pinned = FakeNode(@"nvm\versions\v20.11.0", "20.11.0");
        using var computer = Computer(onPath: usual);
        Write(@"pinnedProject\.nvmrc", "20\n");
        var app = Write(@"pinnedProject\app.js", "console.log('hi');\n");

        var setup = NodeSetup.For(app)!;

        Assert.Equal(pinned, setup.Node);
        Assert.Equal("Node.js 20.11.0 (nvm's), as .nvmrc names Node.js 20", setup.Explained);
    }

    [Fact]
    public void WithNoNodeNewEnoughItSaysSoAndTheNoteSaysWhatToInstall()
    {
        var usual = FakeNode(@"Program Files\nodejs", "18.20.4");
        using var computer = Computer(onPath: usual);
        var app = Write(@"tooOld\app.js", "const { promise, resolve } = Promise.withResolvers();\n");

        var setup = NodeSetup.For(app)!;

        Assert.Equal(usual, setup.Node);
        Assert.Equal("Node.js 18.20.4 (on PATH), though app.js calls Promise.withResolvers at line 1, which Node.js 22 added, and no Node.js of 22 or later is on this computer", setup.Explained);
        Assert.Equal("app.js calls Promise.withResolvers at line 1, which Node.js 22 added - and Node.js 18.20.4 (on PATH) is the newest Node.js on this computer, so that " +
                     "part of it cannot run, which is not a mistake in the code. Installing Node.js 22 or later runs it - for example:\n  winget install OpenJS.NodeJS.LTS\n" +
                     "which installs the newest long-term support release",
                     ToolchainVersionErrors.NoteFor(app));
    }
}

/// <summary>Programs run with the Node.js on this computer, as the window runs them, saying the Node.js their code needs.</summary>
public class NodeVersionLiveTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>The version Windows reads from node.exe's own file is the one node itself says.</summary>
    [Fact]
    public void ANodesOwnFileSaysItsVersion()
    {
        if (Nodes.Usual is not { } usual) return;

        using var node = Process.Start(new ProcessStartInfo(usual.Program, "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        var said = node.StandardOutput.ReadToEnd().Trim();
        node.WaitForExit();

        Assert.Equal(said, $"v{usual.VersionText}");
    }

    [Fact]
    public void HowItRanSaysTheNodeTheCodeNeeds()
    {
        if (Nodes.Usual is not { } usual || usual.Version < new LanguageVersion(21)) return;

        var app = Path.Combine(_temp.Path, "app.js");
        File.WriteAllText(app, "const byHouse = Object.groupBy([{ house: 'a' }], s => s.house);\nconsole.log(Object.keys(byHouse));\n");

        var plan = TargetFactory.FromFile(app);

        Assert.True(plan.Ok, plan.Problem);
        Assert.Equal($"Running it with Node.js {usual.VersionText} (on PATH). Its code needs Node.js 21 or later: app.js calls Object.groupBy at line 1, which Node.js 21 added.", plan.Explanation);
    }
}
