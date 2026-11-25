using FixFinder.Core;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Python's imports played out from the file that is run: imports that go round in a circle stop the program where a
/// module asks another, still part way through, for a name it defines further down - the file that is run being started
/// again when a module imports it by its own name - and nothing is said of a circle that works, or of a name no line defines.
/// </summary>
public class PythonImportOrderTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string PathOf(string relative) => Path.GetFullPath(Path.Combine(_temp.Path, relative));

    private static TopLevelStatement Import(int line, string module, int level = 0) => new(line, "import", module, level, [], [module.Split('.')[0]], false);

    private static TopLevelStatement From(int line, string module, string name, int level = 0, bool onlyWhenRun = false) =>
        new(line, "from", module, level, [name], [name], onlyWhenRun);

    private static TopLevelStatement Gives(int line, string name, bool onlyWhenRun = false) => new(line, "gives", "", 0, [], [name], onlyWhenRun);

    [Fact]
    public void TheFileThatIsRunIsStartedAgainWhenAModuleImportsItByItsOwnName()
    {
        var shop = PathOf("shop.py");
        var prices = PathOf("prices.py");
        var statements = new Dictionary<string, IReadOnlyList<TopLevelStatement>>(StringComparer.OrdinalIgnoreCase)
        {
            [shop] = [From(1, "prices", "price_with_tax"), Gives(3, "TAX_RATE"), Gives(6, "basket_total")],
            [prices] = [From(1, "shop", "TAX_RATE"), Gives(4, "price_with_tax")],
        };

        var stop = PythonImportOrder.Stop(shop, statements);

        Assert.NotNull(stop);
        Assert.Equal((shop, 1, "prices", prices, "price_with_tax", 4, 1), (stop.File, stop.Line, stop.Module, stop.ModuleFile, stop.Name, stop.DefinedOn, stop.StillOn));
        Assert.Equal([new ImportStep(shop, 1, "prices", false), new ImportStep(prices, 1, "shop", true), new ImportStep(shop, 1, "prices", false)], stop.Way);
    }

    /// <summary>
    /// A name the module has reached by the time it is asked for it is there, whatever the circle: a.py defines X, then
    /// imports b, whose from a import X finds a part way through, X and all.
    /// </summary>
    [Fact]
    public void ANameDefinedBeforeTheImportThatStartsTheCircleIsThere()
    {
        var statements = new Dictionary<string, IReadOnlyList<TopLevelStatement>>(StringComparer.OrdinalIgnoreCase)
        {
            [PathOf("main.py")] = [Import(1, "a")],
            [PathOf("a.py")] = [Gives(1, "X"), From(2, "b", "Y")],
            [PathOf("b.py")] = [From(1, "a", "X"), Gives(2, "Y")],
        };

        Assert.Null(PythonImportOrder.Stop(PathOf("main.py"), statements));
    }

    /// <summary>A name the module never defines is a mistake of another kind - a misspelling - and not this check's to say.</summary>
    [Fact]
    public void ANameNoLineDefinesIsNotSaidToBeACircle()
    {
        var shop = PathOf("shop.py");
        var statements = new Dictionary<string, IReadOnlyList<TopLevelStatement>>(StringComparer.OrdinalIgnoreCase)
        {
            [shop] = [From(1, "prices", "price_with_tax")],
            [PathOf("prices.py")] = [From(1, "shop", "TAX_RATE"), Gives(4, "price_with_vat")],
        };

        Assert.Null(PythonImportOrder.Stop(shop, statements));
    }

    /// <summary>Lines inside if __name__ == "__main__" run in the file that is run, and not when it is started again as a module.</summary>
    [Fact]
    public void WhatOnlyTheFileThatIsRunDoesIsNotDoneWhenItIsImported()
    {
        var game = PathOf("game.py");
        var board = PathOf("board.py");
        var statements = new Dictionary<string, IReadOnlyList<TopLevelStatement>>(StringComparer.OrdinalIgnoreCase)
        {
            [game] = [Import(1, "board"), Gives(4, "start"), From(9, "board", "size", onlyWhenRun: true)],
            [board] = [Import(1, "game"), Gives(4, "size")],
        };

        Assert.Null(PythonImportOrder.Stop(game, statements));
    }

    [Fact]
    public void ARelativeImportInAPackageIsFollowedToTheModuleItNames()
    {
        var main = PathOf("main.py");
        var orders = PathOf(@"pkg\orders.py");
        var stock = PathOf(@"pkg\stock.py");
        var statements = new Dictionary<string, IReadOnlyList<TopLevelStatement>>(StringComparer.OrdinalIgnoreCase)
        {
            [main] = [From(1, "pkg.orders", "place_order")],
            [PathOf(@"pkg\__init__.py")] = [],
            [orders] = [From(1, "stock", "in_stock", level: 1), Gives(4, "place_order")],
            [stock] = [From(1, "orders", "place_order", level: 1), Gives(3, "ITEMS"), Gives(6, "in_stock")],
        };

        var stop = PythonImportOrder.Stop(main, statements);

        Assert.NotNull(stop);
        Assert.Equal((stock, 1, "pkg.orders", "place_order", 4, 1), (stop.File, stop.Line, stop.Module, stop.Name, stop.DefinedOn, stop.StillOn));
    }

    private string Write(string relative, string content)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file, bool codeOnly)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = codeOnly ? await checker.CheckCodeAsync(launch) : await checker.CheckAsync(launch);

        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {FixFinder.Core.Checking.Evidence.Labelled(finding)} :: {finding.Found}");
        return report;
    }

    [Theory]
    [InlineData(true, "Likely (imports followed from the file that is run)")]
    [InlineData(false, "Certain (seen when it ran)")]
    public async Task AShopAndItsPricesThatImportEachOtherAreSaidToStopWhereTheyDo(bool codeOnly, string confidence)
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var shop = Write(@"shop\shop.py", """
            from prices import price_with_tax

            TAX_RATE = 0.2


            def basket_total(items):
                return sum(price_with_tax(item) for item in items)


            print(basket_total([10, 20]))
            """);
        Write(@"shop\prices.py", """
            from shop import TAX_RATE


            def price_with_tax(price):
                return price * (1 + TAX_RATE)
            """);

        var report = await CheckAsync(shop, codeOnly);

        var circle = Assert.Single(report.Findings);
        Assert.Equal(("analysis-circular-import", 1, shop), (circle.RuleId, circle.Line, circle.File));
        Assert.Equal("Imports that go round in a circle", circle.Title);
        Assert.Equal(confidence, FixFinder.Core.Checking.Evidence.Labelled(circle));
        Assert.Equal("Run as it is, shop.py stops with ImportError at line 1 of shop.py, as its imports go round in a circle: shop.py line 1 imports " +
                     "prices; prices.py line 1 imports shop, which starts shop.py again from its top as a module of its own, since shop.py is the file " +
                     "being run; and shop.py line 1 asks prices for price_with_tax, which prices.py has not reached yet: it defines price_with_tax on " +
                     "line 4, and is still on line 1.", circle.Found);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APackagesModulesThatImportEachOtherAreSaidToStopInTheOneThatAsks(bool codeOnly)
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var main = Write(@"shop\main.py", "from pkg.orders import place_order\n\nprint(place_order(\"tea\"))\n");
        Write(@"shop\pkg\__init__.py", "");
        Write(@"shop\pkg\orders.py", "from .stock import in_stock\n\n\ndef place_order(item):\n    return \"placed\" if in_stock(item) else \"sold out\"\n");
        var stock = Write(@"shop\pkg\stock.py", "from .orders import place_order\n\nITEMS = {\"tea\": 3}\n\n\ndef in_stock(item):\n    return ITEMS.get(item, 0) > 0\n");

        var report = await CheckAsync(main, codeOnly);

        var circle = Assert.Single(report.Findings, finding => finding.RuleId == "analysis-circular-import");
        Assert.Equal((1, stock), (circle.Line, circle.File));
        Assert.Equal("Run as it is, main.py stops with ImportError at line 1 of stock.py, as its imports go round in a circle: main.py line 1 imports " +
                     "pkg.orders; orders.py line 1 imports pkg.stock; and stock.py line 1 asks pkg.orders for place_order, which orders.py has not " +
                     "reached yet: it defines place_order on line 4, and is still on line 1.", circle.Found);
    }

    /// <summary>A game and its board import each other, and the game's own run is guarded: Python runs it, and it prints 16.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACircleThatWorksIsNotReported(bool codeOnly)
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var game = Write(@"game\game.py", "import board\n\n\ndef start():\n    return board.size() * 2\n\n\nif __name__ == \"__main__\":\n    print(start())\n");
        Write(@"game\board.py", "import game\n\n\ndef size():\n    return 8\n\n\ndef doubled():\n    return game.start()\n");

        var report = await CheckAsync(game, codeOnly);

        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == "analysis-circular-import");
        if (!codeOnly) Assert.Equal("16", Assert.Single(report.Run!.Run!.Lines).Text);
    }
}
