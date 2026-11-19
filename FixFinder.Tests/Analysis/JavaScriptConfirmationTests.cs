using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// What the analyses predicted of a JavaScript function, tried for real: the function is called with the witness's values
/// from the end of a copy of its file that keeps only the file's definitions, and only a run that stops with a TypeError
/// on the predicted line makes the finding certain. The program's own top-level code never runs; its constants and
/// requires do. A method, a function kept in a constant and an ES module are not run, and their findings stay as they
/// were. The words each certain finding carries are those the runs here gave.
/// </summary>
public class JavaScriptConfirmationTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes the files, reads the first, and tries its findings; null when there is no Node to run them.</summary>
    private async Task<IReadOnlyList<AnalysisFinding>?> Tried(params (string Name, string Code)[] files)
    {
        if (TargetFactory.FindOnPath("node") is not { } node) return null;

        foreach (var (name, code) in files) await File.WriteAllTextAsync(Path.Combine(_temp.Path, name), code.ReplaceLineEndings("\n"));
        var path = Path.Combine(_temp.Path, files[0].Name);
        var program = await JavaScriptFrontend.ReadAsync([path]);
        var findings = AbstractChecks.Run(program, new SourceText());
        return await JavaScriptConfirmation.ConfirmAsync(findings, program, node, CancellationToken.None);
    }

    [Fact]
    public async Task AParameterTheCodeLetsBeNullIsRunWithNullAndBecomesCertain()
    {
        const string code =
            "function greet(person) {\n    if (person === null) {\n        console.log(\"nobody there\");\n    }\n    return \"hello \" + person.name;\n" +
            "}\n\nfunction upper(name) {\n    let chosen = null;\n    if (name.length > 3) {\n" +
            "        chosen = name;\n    }\n    return chosen.toUpperCase();\n}\n\n" +
            "function first(list) {\n    if (list.length === 0) {\n        console.log(\"empty\");\n    }\n    return list[0].name;\n" +
            "}\n\nconsole.log(greet({ name: \"sam\" }), upper(\"annabel\"), first([{ name: \"a\" }]));\n";
        if (await Tried(("people.js", code)) is not { } findings) return;

        var nothing = Assert.Single(findings, finding => finding.CheckId == "analysis-null-used" && finding.Span.Line == 5);
        Assert.Equal(Confidence.Certain, nothing.Confidence);
        Assert.Equal("Running `greet(null)` stopped with TypeError: Cannot read properties of null (reading 'name') on line 5.", nothing.Confirmation);
    }

    [Fact]
    public async Task TheProgramsOwnTopLevelCodeNeverRunsWhileItsRequiresDoAndAFreeParameterIsUndefined()
    {
        // Run as it is, report.js stops at once reading a file that is not there, and would then go round for ever.
        const string report =
            "const fs = require(\"fs\");\nconst { title } = require(\"./helpers\");\nconst GREETING = \"hello\";\n\nfunction label(prefix, person) {\n" +
            "    if (person === null) {\n        console.log(\"nobody\");\n    }\n    const name = person.name;\n    return prefix + GREETING + \" \" + title(name);\n" +
            "}\n\nconst marks = fs.readFileSync(\"marks-that-are-not-there.txt\", \"utf8\");\nwhile (true) {\n    console.log(label(\"> \", { name: \"sam\" }));\n" +
            "}\n";
        const string helpers =
            "function title(word) {\n    return word.charAt(0).toUpperCase() + word.slice(1);\n}\n\nmodule.exports = { title };\n";
        if (await Tried(("report.js", report), ("helpers.js", helpers)) is not { } findings) return;

        var nothing = Assert.Single(findings, finding => finding.CheckId == "analysis-null-used");
        Assert.Equal(Confidence.Certain, nothing.Confidence);
        Assert.Equal("Running `label(undefined, null)` stopped with TypeError: Cannot read properties of null (reading 'name') on line 9.", nothing.Confirmation);
    }

    [Fact]
    public async Task TheConstantsAFunctionReadsAreKept()
    {
        const string code =
            "\"use strict\";\nconst LIMIT = 3;\nconst LABELS = [\"first\", \"second\", \"third\"];\n\nfunction shorten(words) {\n" +
            "    if (words === null) {\n        console.log(\"nothing to shorten\");\n    }\n    const most = LIMIT + LABELS.length - 3;\n    return words.slice(0, most);\n" +
            "}\n\nconsole.log(shorten([\"a\", \"b\", \"c\", \"d\"]));\n";
        if (await Tried(("shorten.js", code)) is not { } findings) return;

        var nothing = Assert.Single(findings, finding => finding.CheckId == "analysis-null-used");
        Assert.Equal(Confidence.Certain, nothing.Confidence);
        Assert.Equal("Running `shorten(null)` stopped with TypeError: Cannot read properties of null (reading 'slice') on line 10.", nothing.Confirmation);
    }

    [Fact]
    public async Task AMethodAndAFunctionKeptInAConstantAreNotRun()
    {
        const string code =
            "class Shelf {\n    take(book) {\n        if (book === null) {\n            console.log(\"none\");\n        }\n" +
            "        return book.title;\n    }\n}\n\nconst pick = (book) => {\n" +
            "    if (book === null) {\n        console.log(\"none\");\n    }\n    return book.title;\n};\n" +
            "\nconsole.log(new Shelf().take({ title: \"Emma\" }), pick({ title: \"Dune\" }));\n";
        if (await Tried(("shapes.js", code)) is not { } findings) return;

        var both = findings.Where(finding => finding.CheckId == "analysis-null-used").ToList();
        Assert.Equal([6, 14], both.Select(finding => finding.Span.Line));
        Assert.All(both, finding => Assert.NotEqual(Confidence.Certain, finding.Confidence));
        Assert.All(both, finding => Assert.Null(finding.Confirmation));
    }

    [Fact]
    public async Task AnESModuleIsNotRun()
    {
        const string code =
            "import { readFileSync } from \"fs\";\n\nfunction greet(person) {\n    if (person === null) {\n        console.log(\"nobody\");\n" +
            "    }\n    return \"hello \" + person.name;\n}\n\nconsole.log(greet({ name: \"sam\" }), readFileSync.name);\n";
        if (await Tried(("greet.js", code)) is not { } findings) return;

        var nothing = Assert.Single(findings, finding => finding.CheckId == "analysis-null-used");
        Assert.NotEqual(Confidence.Certain, nothing.Confidence);
        Assert.Null(nothing.Confirmation);
    }
}
