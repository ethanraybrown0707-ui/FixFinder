using System.Globalization;
using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Symbolic;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// Hybrid static and dynamic analysis: what the static checks predicted - this line fails when `values` is empty - is
/// tried for real. The function, or for top-level code the whole program, is run under the probe with exactly those
/// inputs. Only if it stops with the predicted error on the predicted line does the finding become certain, and the
/// report then says what ran, the values at that moment and the lines it went through.
/// </summary>
public static partial class Confirmation
{
    private const int MostRuns = 6;
    private const int LongestList = 100;

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);

    [GeneratedRegex(@"[A-Za-z_]\w*")]
    private static partial Regex Identifier();

    public static async Task<IReadOnlyList<AnalysisFinding>> ConfirmAsync(IReadOnlyList<AnalysisFinding> findings, string interpreter,
        CancellationToken cancellationToken)
    {
        var runs = findings
            .Select((finding, index) => (Finding: finding, Index: index))
            .Where(item => item.Finding.WitnessValues is { Count: > 0 } && Expected(item.Finding.CheckId).Length > 0 && item.Finding.Function is not null &&
                           Path.GetExtension(item.Finding.Span.File).Equals(".py", StringComparison.OrdinalIgnoreCase))
            .Take(MostRuns)
            .Select(async item => (item.Index, Shown: await TryAsync(item.Finding, interpreter, cancellationToken)));

        var confirmed = findings.ToList();
        foreach (var (index, shown) in await Task.WhenAll(runs))
        {
            if (shown is not { } seen) continue;

            confirmed[index] = confirmed[index] with
            {
                Confidence = Confidence.Certain,
                Confirmation = seen.Evidence,
                State = seen.State,
            };
        }

        return confirmed;
    }

    private static string[] Expected(string check) => check switch
    {
        "analysis-division-by-zero" => ["ZeroDivisionError"],
        "analysis-null-used" => ["AttributeError", "TypeError"],
        "analysis-index-out-of-range" or "analysis-empty-collection" => ["IndexError"],
        _ => [],
    };

    private static async Task<(string Evidence, StateTrace? State)?> TryAsync(AnalysisFinding finding, string interpreter, CancellationToken cancellationToken)
    {
        var values = finding.WitnessValues!;
        var file = finding.Span.File;
        var typed = values.Where(v => v.TypedAt is not null).OrderBy(v => v.TypedAt).ToList();
        var input = string.Concat(typed.Select(v => Typed(v) + "\n"));

        Trace? trace;
        string ran;

        if (finding.Function == IrFunction.ModuleBody)
        {
            if (typed.Count != values.Count) return null;

            trace = await PythonProbe.RunAsync(interpreter, file, input, RunTimeout, cancellationToken, finding.Span.Line);
            ran = "the program, typing " + string.Join(" then ", typed.Select(v => $"`{Typed(v)}`"));
        }
        else
        {
            var arguments = values.Where(v => v.Parameter is not null).ToList();
            if (finding.Function!.Contains('.') || arguments.Any(v => Literal(v) is null)) return null;

            var literal = "{" + string.Join(", ", arguments.Select(v => $"'{v.Parameter}': {Literal(v)}")) + "}";
            trace = await PythonProbe.CallAsync(interpreter, file, finding.Function, literal, input, RunTimeout, cancellationToken, finding.Span.Line);
            ran = $"`{finding.Function}({string.Join(", ", arguments.Select(v => $"{v.Parameter}={Literal(v)}"))})`";
        }

        if (trace is not { Error: { } error } || trace.ErrorLine != finding.Span.Line || !Expected(finding.CheckId).Contains(error)) return null;

        var said = trace.Message is { Length: > 0 } message ? $"{error}: {message}" : error;

        var evidence = $"Running {ran} stopped with {said} on line {trace.ErrorLine}{At(trace, file)}. Lines it ran: {TraceCompression.Compress(trace.Lines)}" +
                       (trace.Cut ? " …" : "") + ".";

        // The same run recorded what the variables held each time it reached the line, so the table costs nothing
        // extra and every value in it is one the program really held.
        var state = StateTrace.From(finding.Span.Line, trace.Watched, Mentioned(file, finding.Span.Line), failedOnLastPass: true, trace.WatchedCut);

        return (evidence, state);
    }

    /// <summary>The names written on one line of the file, which are the variables that line's behaviour turns on.</summary>
    private static IReadOnlyCollection<string> Mentioned(string file, int number)
    {
        try
        {
            var line = File.ReadLines(file).Skip(number - 1).FirstOrDefault() ?? "";
            return [.. Identifier().Matches(line).Select(m => m.Value).Distinct(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The values, at the moment it failed, of the variables the failing line uses.</summary>
    private static string At(Trace trace, string file)
    {
        string line;
        try
        {
            line = File.ReadLines(file).Skip(trace.ErrorLine!.Value - 1).FirstOrDefault() ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }

        var used = Identifier().Matches(line).Select(m => m.Value).Distinct()
            .Where(trace.Values.ContainsKey).Take(4)
            .Select(name => $"`{name}` was {trace.Values[name]}")
            .ToList();

        return used.Count == 0 ? "" : ", where " + string.Join(" and ", used);
    }

    private static string Typed(WitnessValue value) => value.Kind switch
    {
        WitnessKind.Characters => new string('x', (int)Math.Min(value.Value.ToDouble(), LongestList)),
        WitnessKind.WholeNumber => value.Value.Numerator.ToString(CultureInfo.InvariantCulture),
        _ => value.Value.ToDouble().ToString("R", CultureInfo.InvariantCulture),
    };

    /// <summary>The value as Python source, or null when it cannot be written down exactly.</summary>
    private static string? Literal(WitnessValue value) => value.Kind switch
    {
        WitnessKind.Nothing => "None",
        WitnessKind.Truth => value.Value.IsZero ? "False" : "True",
        WitnessKind.WholeNumber when value.Value.IsInteger => value.Value.Numerator.ToString(CultureInfo.InvariantCulture),
        WitnessKind.Number => value.Value.IsInteger ? value.Value.Numerator.ToString(CultureInfo.InvariantCulture) : value.Value.ToDouble().ToString("R", CultureInfo.InvariantCulture),
        WitnessKind.Items when value.Value.IsInteger && value.Value <= LongestList => "[" + string.Join(", ", Enumerable.Repeat("0", (int)value.Value.Numerator)) + "]",
        WitnessKind.Characters when value.Value.IsInteger && value.Value <= LongestList => "'" + new string('a', (int)value.Value.Numerator) + "'",
        _ => null,
    };
}
