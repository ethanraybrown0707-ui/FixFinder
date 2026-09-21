using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes;

/// <summary>A quicker way to run a check, used only once it has given the same answer as the usual way.</summary>
internal static partial class FasterCheck
{
    internal const int ChecksCompared = 2;

    internal static bool CompareEveryCheck { get; set; }

    internal static ConcurrentQueue<string> Disagreements { get; } = new();

    private static int _comparisons;

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
            Directory.CreateDirectory(otherFolder);

            var otherCopy = Path.Combine(otherFolder, Path.GetFileName(copy));
            File.Copy(copy, otherCopy);

            var fromFaster = await faster(otherCopy, otherFolder, cancellationToken);

            if (trusted)
                return fromFaster is { Ran: true } quick && Plain(quick) ? quick : await usual(cancellationToken);

            var answer = await fromUsual!;

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

    internal static bool Plain(CheckResult result) => result.Lines.All(line => line.Text.All(c => c < 128));

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
