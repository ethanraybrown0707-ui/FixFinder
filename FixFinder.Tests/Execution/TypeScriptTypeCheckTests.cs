using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// TypeScript's types checked by tsc, without tsc: what TypeScript 7.0.2 wrote - captured as it wrote it - read as type errors,
/// Node's own names told apart from mistakes, the tsc a program is checked with found and run as Node.js runs the program, each
/// error rated and explained, and tsc's own suggestions made into changes.
/// </summary>
public class TypeScriptTypeCheckTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static IReadOnlyList<CapturedLine> Lines(params string[] texts) =>
        texts.Select((text, index) => new CapturedLine(index, StreamKind.StdOut, text, TimeSpan.Zero)).ToList();

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>What TypeScript 7.0.2 wrote for a program of mistakes, with --pretty false.</summary>
    private static readonly string[] Written =
    [
        "main.ts(4,5): error TS2322: Type 'string' is not assignable to type 'number'.",
        "main.ts(6,13): error TS2552: Cannot find name 'mraks'. Did you mean 'marks'?",
        "main.ts(8,13): error TS2591: Cannot find name 'process'. Do you need to install type definitions for node? Try `npm i --save-dev @types/node` and then add 'node' to the types field in your tsconfig.",
        "main.ts(9,13): error TS7053: Element implicitly has an 'any' type because expression of type 'string' can't be used to index type '{ Ada: number; Bob: number; }'.",
        "  No index signature with a parameter of type 'string' was found on type '{ Ada: number; Bob: number; }'.",
        "main.ts(10,13): error TS18048: 'found' is possibly 'undefined'.",
    ];

    [Fact]
    public void EachErrorIsReadAtItsLineAndColumnWithItsCodeAndTheLinesUnderItJoined()
    {
        var errors = TypeScriptCompilerParser.Errors(Lines(Written));

        Assert.Equal(["TS2322", "TS2552", "TS2591", "TS7053", "TS18048"], errors.Select(error => error.ErrorCode));
        Assert.Equal([(4, 5), (6, 13), (8, 13), (9, 13), (10, 13)], errors.Select(error => (error.Frames[0].Line!.Value, error.Frames[0].Column!.Value)));
        Assert.All(errors, error => Assert.Equal((TypeScriptCompilerParser.LanguageId, TypeScriptCompilerParser.TypeError, "main.ts"), (error.LanguageId, error.ExceptionType, error.Frames[0].File)));
        Assert.Equal("Element implicitly has an 'any' type because expression of type 'string' can't be used to index type '{ Ada: number; Bob: number; }'. " +
                     "No index signature with a parameter of type 'string' was found on type '{ Ada: number; Bob: number; }'.", errors[3].Message);
    }

    [Fact]
    public void WhatTscSaysOfHowItWasSetUpIsToldApartFromTheCode()
    {
        var lines = Lines(
            "tsconfig.json(3,5): error TS5023: Unknown compiler option 'strictt'.",
            "error TS18003: No inputs were found in config file 'C:/project/tsconfig.json'. Specified 'include' paths were '[\"src/*.ts\"]' and 'exclude' paths were '[]'.",
            "main.ts(4,7): error TS2322: Type 'string' is not assignable to type 'number'.");

        Assert.Equal(["tsconfig.json(3,5): error TS5023: Unknown compiler option 'strictt'.", lines[1].Text], TypeScriptCompilerParser.SetupProblems(lines));
    }

    [Theory]
    [InlineData("TS2591", "Cannot find name 'process'. Do you need to install type definitions for node? Try `npm i --save-dev @types/node` and then add 'node' to the types field in your tsconfig.", true)]
    [InlineData("TS2591", "Cannot find name 'node:readline'. Do you need to install type definitions for node? Try `npm i --save-dev @types/node` and then add 'node' to the types field in your tsconfig.", true)]
    [InlineData("TS2304", "Cannot find name '__dirname'.", true)]
    [InlineData("TS2304", "Cannot find name 'global'.", true)]
    [InlineData("TS2307", "Cannot find module 'node:fs' or its corresponding type declarations.", true)]
    [InlineData("TS2307", "Cannot find module 'fs' or its corresponding type declarations.", true)]
    [InlineData("TS2304", "Cannot find name 'totl'.", false)]
    [InlineData("TS2307", "Cannot find module './helpers.ts' or its corresponding type declarations.", false)]
    [InlineData("TS2322", "Type 'string' is not assignable to type 'number'.", false)]
    public void OnlyWhatSaysNodesOwnTypesAreMissingIsLeftOutOfTheErrors(string code, string message, bool onlyNodesTypes) =>
        Assert.Equal(onlyNodesTypes, TypeScriptCompiler.OnlyMissingNodeTypes(code, message));

    private string FakeCompiler(string folder, string name, string version)
    {
        Write(Path.Combine(folder, "node_modules", "typescript", "package.json"), $"{{\"name\":\"{name}\",\"version\":\"{version}\"}}");
        Write(Path.Combine(folder, "node_modules", "typescript", "bin", "tsc"), "#!/usr/bin/env node\n");
        return Write(Path.Combine(folder, "src", "main.ts"), "console.log(1);\n");
    }

    [Fact]
    public void TheProjectsOwnTscIsFoundInANodeModulesAboveTheProgram()
    {
        var file = FakeCompiler("project", "typescript", "7.0.2");

        var compiler = TypeScriptCompiler.For(file);

        Assert.Equal((Path.Combine(_temp.Path, "project", "node_modules", "typescript"), "7.0.2", true, true),
            (compiler?.Package, compiler?.Version, compiler?.ForTheProject, compiler?.ChecksAsNodeRuns));
    }

    [Fact]
    public void ATscOlderThanFiveCannotCheckAProgramAsNodeRunsIt() =>
        Assert.False(TypeScriptCompiler.For(FakeCompiler("old", "typescript", "4.9.5"))!.ChecksAsNodeRuns);

    [Fact]
    public void APackageThatIsNotTypeScriptIsNotTakenForTheProjectsTsc() =>
        Assert.NotEqual(true, TypeScriptCompiler.For(FakeCompiler("other", "not-typescript", "7.0.2"))?.ForTheProject);

    private static readonly TypeScriptCompiler.Compiler Compiler = new(@"C:\npm\node_modules\typescript", "7.0.2", false);

    [Fact]
    public void AFileOnItsOwnIsCheckedAsNodeRunsItAndNothingIsWritten()
    {
        var file = Write(Path.Combine("alone", "main.ts"), "console.log(1);\n");

        var check = TypeScriptCompiler.CheckOf(Compiler, "node.exe", file, TimeSpan.FromMinutes(1));

        Assert.Equal(("node.exe", Path.GetDirectoryName(file), null), (check.Spec.ExecutablePath, check.Spec.WorkingDirectory, check.Project));
        Assert.Equal($"\"{Compiler.Launcher}\" --noEmit --pretty false --strict --target esnext --module esnext --moduleResolution bundler " +
                     "--moduleDetection force --allowImportingTsExtensions --skipLibCheck \"main.ts\"", check.Spec.Arguments);
        Assert.False(check.NodeTypesFound);
    }

    [Fact]
    public void NodesOwnTypeDefinitionsInANodeModulesAboveAreNamedToTsc()
    {
        Write(Path.Combine("typed", "node_modules", "@types", "node", "package.json"), "{\"name\":\"@types/node\"}");
        var file = Write(Path.Combine("typed", "main.ts"), "console.log(process.argv.length);\n");

        var check = TypeScriptCompiler.CheckOf(Compiler, "node.exe", file, TimeSpan.FromMinutes(1));

        Assert.True(check.NodeTypesFound);
        Assert.EndsWith("--skipLibCheck --types node \"main.ts\"", check.Spec.Arguments);
    }

    [Fact]
    public void AProgramWithATsconfigIsCheckedAsItsProjectSays()
    {
        var project = Write(Path.Combine("configured", "tsconfig.json"), "{}\n");
        var file = Write(Path.Combine("configured", "src", "main.ts"), "console.log(1);\n");

        var check = TypeScriptCompiler.CheckOf(Compiler, "node.exe", file, TimeSpan.FromMinutes(1));

        Assert.Equal((project, Path.GetDirectoryName(project)), (check.Project, check.Spec.WorkingDirectory));
        Assert.Equal($"\"{Compiler.Launcher}\" --noEmit --pretty false -p \"{project}\"", check.Spec.Arguments);
    }

    [Fact]
    public void ATsconfigAboveTheProjectsOwnFolderIsNotTheProgramsProject()
    {
        Write(Path.Combine("outer", "tsconfig.json"), "{}\n");
        Write(Path.Combine("outer", "inner", "package.json"), "{}\n");
        var file = Write(Path.Combine("outer", "inner", "main.ts"), "console.log(1);\n");

        Assert.Null(TypeScriptCompiler.ProjectOf(file));
    }

    [Fact]
    public void EveryTypeErrorIsAnErrorTheCompilerIsCertainOf()
    {
        var error = TypeScriptCompilerParser.Errors(Lines(Written[0]))[0];

        Assert.Equal(new WarningRating(FindingKind.Syntax, Severity.Error, Confidence.Certain), WarningRatings.For(error));
    }

    [Theory]
    [InlineData("main.ts(4,5): error TS2322: Type 'string' is not assignable to type 'number'.", "TypeScript's compiler found a value of one type given to a name declared to hold another")]
    [InlineData("main.ts(10,13): error TS18048: 'found' is possibly 'undefined'.", "This value can be undefined")]
    // A type error with no guide of its own gets the one for every type error.
    [InlineData("main.ts(3,1): error TS2451: Cannot redeclare block-scoped variable 'top'.", "TypeScript's compiler, tsc, found a value whose type does not fit")]
    public void EachTypeErrorIsExplainedByItsOwnGuideOrTheOneForEveryTypeError(string written, string explanationStart)
    {
        var error = TypeScriptCompilerParser.Errors(Lines(written))[0];

        Assert.StartsWith(explanationStart, Guidebook.For("main.ts", FindingKind.Syntax, error: error, isWarning: true).Explanation);
    }

    private LocalFix? Fix(string rule, string written, string code)
    {
        Write("main.ts", code);
        var error = TypeScriptCompilerParser.Errors(Lines(written))[0];
        return LocalFixEngine.Rules.Single(each => each.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });
    }

    [Theory]
    [InlineData("main.ts(2,13): error TS2552: Cannot find name 'mraks'. Did you mean 'marks'?", "const marks = [70];\nconsole.log(mraks);\n", "console.log(marks);")]
    [InlineData("main.ts(2,19): error TS2551: Property 'lenght' does not exist on type 'number[]'. Did you mean 'length'?", "const marks = [70];\nconsole.log(marks.lenght);\n", "console.log(marks.length);")]
    // Two of the word on the line: the one at tsc's column is changed.
    [InlineData("main.ts(2,20): error TS2552: Cannot find name 'totl'. Did you mean 'total'?", "const total = 1;\nconsole.log(totl * totl);\n", "console.log(totl * total);")]
    public void TscsOwnSuggestionIsMadeWhereItSays(string written, string code, string changed) =>
        Assert.Equal(changed, Fix("ts-did-you-mean", written, code)?.NewLines.Single());

    [Fact]
    public void AConstTscSaysIsGivenANewValueIsDeclaredWithLetAsNodesErrorWouldHaveIt()
    {
        var fix = Fix("ts-const-reassigned", "main.ts(2,1): error TS2588: Cannot assign to 'count' because it is a constant.", "const count = 3;\ncount = 4;\n");

        Assert.Equal((1, "let count = 3;"), (fix?.StartLine, fix?.NewLines.Single()));
    }
}
