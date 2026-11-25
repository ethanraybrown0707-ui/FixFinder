using FixFinder.Core;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A CommonJS program's requires played out from the file that is run, as Node runs them: a circle of requires leaves a
/// name undefined where a file takes it out of a module Node is still part way through, or keeps that module's exports and
/// the module then replaces them - said when the file uses the name, and confirmed by Node's own warning when it runs. A
/// circle whose names arrive in time, a name never used, an ES module, a require in a block, and a require inside the
/// function that needs it are left alone.
/// </summary>
public class JavaScriptRequireOrderTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.GetFullPath(Path.Combine(_temp.Path, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file, bool codeOnly)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.JavaScript };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = codeOnly ? await checker.CheckCodeAsync(launch) : await checker.CheckAsync(launch);

        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {FixFinder.Core.Checking.Evidence.Labelled(finding)} :: {finding.Found}");
        return report;
    }

    /// <summary>shop.js, the file that is run: it requires prices first, and exports at its end, as most coursework does.</summary>
    private const string Shop = """
        const { priceWithTax } = require("./prices");

        const TAX_RATE = 0.2;

        function basketTotal(items) {
            return items.reduce((total, item) => total + priceWithTax(item), 0);
        }

        module.exports = { TAX_RATE, basketTotal };

        console.log(basketTotal([10, 20]));
        """;

    private const string PricesTakingTheRate = """
        const { TAX_RATE } = require("./shop");

        function priceWithTax(price) {
            return price * (1 + TAX_RATE);
        }

        module.exports = { priceWithTax };
        """;

    private static IReadOnlyList<Finding> InACircle(CheckReport report) =>
        [.. report.Findings.Where(finding => finding.RuleId == PythonImportOrder.CheckId)];

    [Fact]
    public async Task ANameTakenOutOfTheFileThatIsRunBeforeItExportsItIsUndefinedWhereItIsRead()
    {
        Write(@"shop\shop.js", Shop);
        var prices = Write(@"shop\prices.js", PricesTakingTheRate);

        var report = await CheckAsync(Path.Combine(_temp.Path, @"shop\shop.js"), codeOnly: true);

        var circle = Assert.Single(InACircle(report));
        Assert.Equal((1, prices), (circle.Line, circle.File));
        Assert.Equal("Requires that go round in a circle", circle.Title);
        Assert.Equal("Likely (imports followed from the file that is run)", FixFinder.Core.Checking.Evidence.Labelled(circle));
        Assert.Equal("Run as it is, TAX_RATE is undefined in prices.js for as long as the program runs, and its line 4 reads it, because the " +
                     "requires go round in a circle: shop.js line 1 requires ./prices; and prices.js line 1 requires ./shop and takes TAX_RATE out " +
                     "of what shop.js has exported so far, which is not it yet: shop.js - the file being run, which Node does not start again - " +
                     "exports TAX_RATE on line 9, and is still on line 1.", circle.Found);
    }

    /// <summary>Run, the program prints NaN, and Node's warning about TAX_RATE confirms the finding.</summary>
    [Fact]
    public async Task NodesOwnWarningWhenItRunsConfirmsTheName()
    {
        if (!LocalFixLiveTests.Available("node")) return;

        Write(@"shop\shop.js", Shop);
        Write(@"shop\prices.js", PricesTakingTheRate);

        var report = await CheckAsync(Path.Combine(_temp.Path, @"shop\shop.js"), codeOnly: false);

        var circle = Assert.Single(InACircle(report));
        Assert.Equal("Certain (run reproduced it)", FixFinder.Core.Checking.Evidence.Labelled(circle));
        Assert.Equal("run as it is, Node warned: Accessing non-existent property 'TAX_RATE' of module exports inside circular dependency", circle.Confirmation);
        Assert.Contains(report.Run!.Run!.Lines, line => line.Text == "NaN");
    }

    [Fact]
    public async Task AnObjectKeptFromAModuleThatThenReplacesItsExportsNeverHasTheName()
    {
        Write(@"shop\shop.js", Shop.Replace("const { priceWithTax } = require(\"./prices\");", "const prices = require(\"./prices\");", StringComparison.Ordinal)
            .Replace("+ priceWithTax(item)", "+ prices.priceWithTax(item)", StringComparison.Ordinal));
        var prices = Write(@"shop\prices.js", PricesTakingTheRate.Replace("const { TAX_RATE } = require(\"./shop\");", "const shop = require(\"./shop\");", StringComparison.Ordinal)
            .Replace("(1 + TAX_RATE)", "(1 + shop.TAX_RATE)", StringComparison.Ordinal));

        var report = await CheckAsync(Path.Combine(_temp.Path, @"shop\shop.js"), codeOnly: true);

        var circle = Assert.Single(InACircle(report));
        Assert.Equal((1, prices), (circle.Line, circle.File));
        Assert.Equal("Run as it is, shop.TAX_RATE is undefined in prices.js for as long as the program runs, and its line 4 reads it, because the " +
                     "requires go round in a circle: shop.js line 1 requires ./prices; and prices.js line 1 requires ./shop and keeps what shop.js - " +
                     "the file being run, which Node does not start again - has exported so far, while it is still on line 1 - and its line 9 then " +
                     "replaces that with module.exports = ..., which prices.js never sees.", circle.Found);
    }

    /// <summary>
    /// A library and its books, each a class the other uses: book.js keeps library.js's exports from before its
    /// module.exports = Library, so instanceof Library on its line 6 has no class to ask - Node stops there with TypeError.
    /// </summary>
    [Fact]
    public async Task AClassKeptFromAModuleThatThenExportsItIsTheOldExportsObject()
    {
        var library = Write(@"library\library.js", """
            const Book = require("./book");

            class Library {
                constructor() {
                    this.books = [];
                }

                add(title) {
                    this.books.push(new Book(title, this));
                    return this;
                }
            }

            module.exports = Library;

            console.log(new Library().add("Dune").books.length);
            """);
        var book = Write(@"library\book.js", """
            const Library = require("./library");

            class Book {
                constructor(title, library) {
                    this.title = title;
                    this.library = library instanceof Library ? library : new Library();
                }
            }

            module.exports = Book;
            """);

        var report = await CheckAsync(library, codeOnly: true);

        var circle = Assert.Single(InACircle(report));
        Assert.Equal((1, book), (circle.Line, circle.File));
        Assert.Equal("Run as it is, Library in book.js is the exports object library.js had before its line 14 replaces it with module.exports = ..., " +
                     "for as long as the program runs, and its line 6 uses it, because the requires go round in a circle: library.js line 1 requires " +
                     "./book; and book.js line 1 requires ./library and keeps what library.js - the file being run, which Node does not start again - " +
                     "has exported so far, while it is still on line 1.", circle.Found);
    }

    /// <summary>
    /// Each of these runs as meant in Node - the names put in the object prices keeps, a require inside the function that
    /// needs it, which prints 36 - or holds something this does not follow, so nothing is said.
    /// </summary>
    [Theory]
    [InlineData("exports added to the object kept", "const prices = require(\"./prices\");\nconst TAX_RATE = 0.2;\nexports.TAX_RATE = TAX_RATE;\nconsole.log(prices.priceWithTax(10));\n",
        "const shop = require(\"./shop\");\n\nfunction priceWithTax(price) {\n    return price * (1 + shop.TAX_RATE);\n}\n\nmodule.exports = { priceWithTax };\n")]
    [InlineData("a require inside the function that needs it", Shop,
        "function priceWithTax(price) {\n    const { TAX_RATE } = require(\"./shop\");\n    return price * (1 + TAX_RATE);\n}\n\nmodule.exports = { priceWithTax };\n")]
    [InlineData("a name taken and never used", Shop,
        "const { TAX_RATE } = require(\"./shop\");\n\nfunction priceWithTax(price) {\n    return price * 1.2;\n}\n\nmodule.exports = { priceWithTax };\n")]
    [InlineData("a require in a block", Shop,
        "if (process.env.SHOP) {\n    require(\"./shop\");\n}\nconst { TAX_RATE } = require(\"./shop\");\n\nfunction priceWithTax(price) {\n    return price * (1 + TAX_RATE);\n}\n\nmodule.exports = { priceWithTax };\n")]
    [InlineData("an ES module", Shop,
        "import { TAX_RATE } from \"./shop.js\";\n\nexport function priceWithTax(price) {\n    return price * (1 + TAX_RATE);\n}\n")]
    public async Task WhatWorksOrCannotBeFollowedIsNotSaid(string what, string shop, string prices)
    {
        output.WriteLine(what);
        var runFile = Write(@"shop\shop.js", shop);
        Write(@"shop\prices.js", prices);

        var report = await CheckAsync(runFile, codeOnly: true);

        Assert.Empty(InACircle(report));
    }
}
