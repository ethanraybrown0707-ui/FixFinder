namespace CrashDotNet;

/// <summary>
/// Crashes on startup with an unhandled <see cref="NullReferenceException"/>, several frames
/// deep, having first written a couple of ordinary lines to stdout.
/// </summary>
/// <remarks>
/// Shaped to exercise the parts of FixFinder that are easy to get wrong:
/// <list type="bullet">
/// <item>output on <b>both</b> streams, so the interleaving and the stderr-first parser
/// detection have something to work on;</item>
/// <item>a stack <b>several frames deep</b>, so culprit-frame selection has to choose rather
/// than take the only option;</item>
/// <item>an <b>inner exception</b>, so the .NET parser's <c>---&gt;</c> chain handling is
/// exercised by the live target and not only by a fixture file;</item>
/// <item>file and line numbers from the portable PDB, which is what source-root detection
/// reads in M2.</item>
/// </list>
/// </remarks>
public static class Program
{
    public static void Main()
    {
        Console.WriteLine("CrashDotNet starting");
        Console.Error.WriteLine("warning: this program is supposed to crash");
        Console.WriteLine("loading basket");

        var basket = LoadBasket();
        Console.WriteLine($"total: {Total(basket)}");
    }

    private static Basket LoadBasket()
    {
        try
        {
            return ReadFromStore();
        }
        catch (Exception ex)
        {
            // Wrapped rather than rethrown, so the printed trace carries a "--->" inner
            // exception chain for the parser to walk.
            throw new InvalidOperationException("Could not load the basket from the store.", ex);
        }
    }

    private static Basket ReadFromStore()
    {
        var raw = ReadStoreFile();

        // The crash. `raw` is null, so this dereference throws NullReferenceException with
        // ReadFromStore as the innermost frame - which is the frame the culprit selector
        // should pick, since it is the deepest one inside this program's own source.
        var count = raw!.Length;

        return new Basket(new List<BasketLine>(count));
    }

    /// <summary>Stands in for a store file that is not there.</summary>
    private static string? ReadStoreFile() => null;

    private static decimal Total(Basket basket)
    {
        decimal total = 0;
        foreach (var line in basket.Lines) total += line.Price;
        return total;
    }

    private sealed record Basket(IReadOnlyList<BasketLine> Lines);

    private sealed record BasketLine(string Name, decimal Price);
}
