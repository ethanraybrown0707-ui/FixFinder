using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes;

/// <summary>
/// A quicker way to run a check, used only once it has given the same answer as the usual way.
/// </summary>
/// <remarks>
/// Every quicker route here - the SDK's C# compiler run directly, javac kept running between checks - is
/// meant to be the same compiler saying the same thing. Meaning to is not the same as doing it, so each
/// one earns its place on this machine: its first checks run both ways, and it is used alone only after
/// they have agreed. One disagreement and that route is set aside for as long as FixFinder is open.
/// While a route is being compared, the answer given is always the usual one's.
/// </remarks>
internal static partial class FasterCheck
{
    /// <summary>How many checks run both ways before the quicker way is trusted alone.</summary>
    internal const int ChecksCompared = 2;

    /// <summary>For measuring: every check runs both ways, and any disagreement is recorded.</summary>
    internal static bool CompareEveryCheck { get; set; }

    /// <summary>Every disagreement seen, described, newest last.</summary>
    internal static ConcurrentQueue<string> Disagreements { get; } = new();

    private static int _comparisons;

    /// <summary>How many checks have run both ways and been compared.</summary>
    internal static int Comparisons => Volatile.Read(ref _comparisons);

    /// <summary>How far one quicker route has earned trust.</summary>
    internal sealed class Trust
    {
        private int _agreed;
        private volatile bool _refused;

        public bool Refused => _refused;

        public bool Trusted => !_refused && !CompareEveryCheck && Volatile.Read(ref _agreed) >= ChecksCompared;

        public void Record(bool agreed)
        {
            if (agreed) Interlocked.Increment(ref _agreed);
            else _refused = true;
        }
    }

    /// <summary>
    /// A check run the quicker way if it is trusted, the usual way if it has been refused, and both ways
    /// - answering with the usual one - while it is still being compared.
    /// </summary>
    /// <param name="faster">
    /// The quicker way, given a copy and a folder of its own. Null means it could not run this time, and the
    /// usual way answers instead.
    /// </param>
    /// <param name="everyLine">
    /// True to require every line of output to match; false to compare only the lines that are compiler
    /// diagnostics, for a usual way that also prints things about itself.
    /// </param>
    internal static async Task<CheckResult> RunAsync(
        string name,
        Trust trust,
        Func<CancellationToken, Task<CheckResult>> usual,
        Func<string, string, CancellationToken, Task<CheckResult?>> faster,
        string copy,
        string folder,
        bool everyLine,
        CancellationToken cancellationToken)
    {
        if (trust.Refused) return await usual(cancellationToken);

        var trusted = trust.Trusted;
        var fromUsual = trusted ? null : usual(cancellationToken);
        var otherFolder = Path.Combine(CompileCheck.Root, Guid.NewGuid().ToString("N")[..12]);

        try
        {
            // A folder of its own every time, so the quicker way never leaves anything where the usual way
            // runs - whether beside it for comparison, or before it when its answer is not used.
            Directory.CreateDirectory(otherFolder);

            var otherCopy = Path.Combine(otherFolder, Path.GetFileName(copy));
            File.Copy(copy, otherCopy);

            var fromFaster = await faster(otherCopy, otherFolder, cancellationToken);

            if (trusted)
                return fromFaster is { Ran: true } quick && Plain(quick) ? quick : await usual(cancellationToken);

            var answer = await fromUsual!;

            // Only two runs that both happened are a comparison. A timeout or a cancellation proves nothing,
            // and neither does output the quicker way would not have been trusted with anyway.
            if (answer.Ran && fromFaster is { Ran: true } && Plain(fromFaster))
            {
                var agreed = Agree(answer, folder, fromFaster, otherFolder, everyLine, out var difference);

                trust.Record(agreed);
                Interlocked.Increment(ref _comparisons);

                if (!agreed) Disagreements.Enqueue($"{name} {Path.GetFileName(copy)}: {difference}");
            }

            return answer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The quicker way could not be set up; the usual answer stands on its own.
            return fromUsual is null ? await usual(cancellationToken) : await fromUsual;
        }
        finally
        {
            try
            {
                if (Directory.Exists(otherFolder)) Directory.Delete(otherFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>True when every character of the output is ASCII.</summary>
    /// <remarks>
    /// A compiler writes non-ASCII text - an accented name, a quoted string echoed back - in whichever
    /// character set its output stream was given, and the two ways of running it need not have been given
    /// the same one. Rather than trust that they were, output with anything outside ASCII in it is always
    /// checked the usual way. It is rare, and it costs only time.
    /// </remarks>
    internal static bool Plain(CheckResult result) => result.Lines.All(line => line.Text.All(c => c < 128));

    /// <summary>
    /// Whether two runs of the same check said the same thing: the same exit code, the same errors in the
    /// same places, and the same output, once each folder's path is taken out.
    /// </summary>
    internal static bool Agree(CheckResult usual, string usualFolder, CheckResult faster, string fasterFolder, bool everyLine, out string difference)
    {
        difference = "";

        if (usual.Ran != faster.Ran || usual.ExitCode != faster.ExitCode)
        {
            difference = $"exit {usual.ExitCode} the usual way, {faster.ExitCode} the quicker way";
            return false;
        }

        static List<string> Errors(CheckResult result) => result.Errors
            .Select(e => $"{LocalFixEngine.KeyOf(e)}@{(e.CulpritFrame ?? e.Frames.FirstOrDefault())?.Line}:{(e.CulpritFrame ?? e.Frames.FirstOrDefault())?.Column}")
            .Order(StringComparer.Ordinal).ToList();

        if (!Errors(usual).SequenceEqual(Errors(faster)))
        {
            difference = $"errors differ: [{string.Join("; ", Errors(usual))}] against [{string.Join("; ", Errors(faster))}]";
            return false;
        }

        List<string> Output(CheckResult result, string folder)
        {
            var lines = result.Lines.Select(l => (l.Stream, Text: l.Text.Replace(folder, "<folder>", StringComparison.OrdinalIgnoreCase).TrimEnd()));

            return everyLine
                ? lines.Select(l => $"{l.Stream}: {l.Text}").ToList()
                : lines.Select(l => l.Text.Trim()).Where(t => Diagnostic().IsMatch(t)).Order(StringComparer.Ordinal).ToList();
        }

        var fromUsual = Output(usual, usualFolder);
        var fromFaster = Output(faster, fasterFolder);

        if (!fromUsual.SequenceEqual(fromFaster))
        {
            difference = $"output differs: [{string.Join(" | ", fromUsual)}] against [{string.Join(" | ", fromFaster)}]";
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"\b(?:error|warning) [A-Za-z]+\d+: ")]
    private static partial Regex Diagnostic();
}
