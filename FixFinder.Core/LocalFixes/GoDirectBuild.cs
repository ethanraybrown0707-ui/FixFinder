using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes;

/// <summary>
/// Builds one Go file the way <c>go build</c> does, by running the compiler and linker it runs with the
/// arguments it passes - without the go command's own start-up in front of them.
/// </summary>
/// <remarks>
/// On Windows the go command takes about a fifth of a second to start before it does anything, and a check
/// with a compile error spends little else. <c>go build -n</c> prints the plan instead of carrying it out:
/// the import configuration, the exact <c>compile</c> command, the link. That plan depends on the file's
/// name, its package clause and what it imports, and on nothing else in it - so it is asked for once for each
/// combination, and every later check of a file that matches runs the two tools directly.
/// <para>
/// <b>What the go command prints is reproduced, not approximated.</b> When a tool fails, go build prints
/// <c># command-line-arguments</c> and then the tool's output with the package folder shortened to <c>.</c>
/// and its work folder to <c>$WORK</c>, and exits 1. The same rewriting is done here, the same way. A tool
/// that prints anything while succeeding, or fails without a word, is left to go build.
/// </para>
/// <para>
/// <b>Anything else the go command decides from a file's contents sends it back to go build</b>: build
/// constraints, cgo, <c>//go:embed</c>, <c>//go:debug</c>, a test file, or an import this reader cannot
/// read plainly. So does a plan whose cached packages have gone, or a Go whose tools have changed since it
/// was made. And it is used alone only once it has agreed with go build on this machine - see
/// <see cref="FasterCheck"/>.
/// </para>
/// </remarks>
internal static partial class GoDirectBuild
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<Plan?>>> Plans = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, FasterCheck.Trust> Trusts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How far building directly has earned trust for this Go.</summary>
    internal static FasterCheck.Trust TrustFor(string go) => Trusts.GetOrAdd(go, _ => new FasterCheck.Trust());

    internal sealed record Command(string Program, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment, bool InPackageFolder);

    /// <summary>go build's plan for one file name, package and set of imports, with the paths that change taken out.</summary>
    internal sealed record Plan(
        string Description,
        IReadOnlyDictionary<string, string> Files,
        IReadOnlyList<Command> Commands,
        string ProbeFolder,
        IReadOnlyList<string> CachedPackages,
        IReadOnlyList<(string Path, DateTime Written, long Length)> Tools);

    /// <summary>
    /// What decides go build's plan for this file - its name, package and imports - or null when something
    /// in it could change the plan in a way that is not worth reading.
    /// </summary>
    internal static string? KeyFor(string fileName, string text)
    {
        if (fileName.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase)) return null;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//go:build", StringComparison.Ordinal) || trimmed.StartsWith("// +build", StringComparison.Ordinal) ||
                trimmed.StartsWith("//go:embed", StringComparison.Ordinal) || trimmed.StartsWith("//go:debug", StringComparison.Ordinal) ||
                trimmed.StartsWith("#cgo", StringComparison.Ordinal))
                return null;
        }

        if (Header(text) is not { } header || header.Imports.Contains("C")) return null;

        return $"{fileName}\n{header.Package}\n{string.Join("\n", header.Imports.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}";
    }

    /// <summary>
    /// The package name and import paths at the top of a Go file, or null for anything but the plain forms:
    /// <c>import "fmt"</c>, <c>import name "fmt"</c> and a parenthesised list of either, with comments anywhere.
    /// </summary>
    internal static (string Package, IReadOnlyList<string> Imports)? Header(string text)
    {
        var tokens = new List<string>();
        var i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        // Enough tokens to reach the end of the imports: identifiers, strings and punctuation, comments skipped.
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '\n' || c == ';') { tokens.Add(";"); i++; }
            else if (char.IsWhiteSpace(c)) i++;
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return null;
                if (text.AsSpan(i, end - i).Contains('\n')) tokens.Add(";");
                i = end + 2;
            }
            else if (c == '"')
            {
                var end = text.IndexOf('"', i + 1);
                if (end < 0 || text.AsSpan(i + 1, end - i - 1).IndexOfAny('\\', '\n') >= 0) return null;
                tokens.Add(text[i..(end + 1)]);
                i = end + 1;
            }
            else if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end < 0 || text.AsSpan(i + 1, end - i - 1).Contains('\n')) return null;
                tokens.Add("\"" + text[(i + 1)..end] + "\"");
                i = end + 1;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                tokens.Add(text[start..i]);
            }
            else
            {
                tokens.Add(c.ToString());
                i++;
            }

            // Past the imports once a declaration keyword appears.
            if (tokens.Count > 0 && tokens[^1] is "func" or "var" or "const" or "type") break;
        }

        var at = 0;
        void Skip() { while (at < tokens.Count && tokens[at] == ";") at++; }

        Skip();
        if (at + 1 >= tokens.Count || tokens[at] != "package" || !Identifier(tokens[at + 1])) return null;

        var package = tokens[at + 1];
        at += 2;

        var imports = new List<string>();

        bool Spec()
        {
            if (at < tokens.Count && (Identifier(tokens[at]) || tokens[at] is "." or "_")) at++;
            if (at >= tokens.Count || !tokens[at].StartsWith('"')) return false;

            imports.Add(tokens[at][1..^1]);
            at++;
            return true;
        }

        while (true)
        {
            Skip();
            if (at >= tokens.Count || tokens[at] != "import") break;
            at++;

            if (at < tokens.Count && tokens[at] == "(")
            {
                at++;

                while (true)
                {
                    Skip();
                    if (at < tokens.Count && tokens[at] == ")") { at++; break; }
                    if (!Spec()) return null;
                }
            }
            else if (!Spec())
            {
                return null;
            }
        }

        return (package, imports);
    }

    private static bool Identifier(string token) =>
        token.Length > 0 && (char.IsLetter(token[0]) || token[0] == '_') && token is not ("import" or "package" or "func" or "var" or "const" or "type");

    /// <summary>The plan for files like this one, asked for once and shared.</summary>
    /// <remarks>
    /// Asked for without the caller's cancellation, because it is shared by every check that matches, and
    /// from the file's bytes rather than its path, because the check that asked may be finished and its
    /// folder gone before the plan is.
    /// </remarks>
    internal static Task<Plan?> PlanFor(string go, string key, string fileName, byte[] content) =>
        Plans.GetOrAdd($"{go}\n{key}", _ => new Lazy<Task<Plan?>>(() => Task.Run(() => CaptureAsync(go, fileName, content)))).Value;

    private static async Task<Plan?> CaptureAsync(string go, string fileName, byte[] content)
    {
        var probeFolder = Path.Combine(CompileCheck.Root, "go-" + Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(probeFolder);

            var probe = Path.Combine(probeFolder, fileName);
            await File.WriteAllBytesAsync(probe, content);

            var spec = new TargetSpec
            {
                ExecutablePath = go,
                Arguments = $"build -n -o \"{Path.Combine(probeFolder, "check.exe")}\" \"{probe}\"",
                WorkingDirectory = probeFolder,
                Timeout = TimeSpan.FromMinutes(2),
            };

            var run = await new TargetRunner(new ParserRegistry()).RunAsync(spec, CancellationToken.None);

            if (run.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut or RunOutcome.Cancelled || run.ExitCode != 0)
                return null;

            return Read(run.Lines.OrderBy(l => l.Sequence).Select(l => l.Text).ToList(), probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(probeFolder)) Directory.Delete(probeFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// A plan from what <c>go build -n</c> printed, or null when any of it is not understood.
    /// </summary>
    /// <remarks>
    /// Only the file's own package is kept - the plan also lists every standard package it depends on, which
    /// the build cache already holds. Refusing is always safe; it only means go build.
    /// </remarks>
    internal static Plan? Read(IReadOnlyList<string> lines, string probe)
    {
        var probeFolder = Path.GetDirectoryName(probe)!;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var commands = new List<Command>();
        string? section = null;
        string? folder = null;
        const string Description = "command-line-arguments";

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line == "#" && i + 2 < lines.Count && lines[i + 1].StartsWith("# ", StringComparison.Ordinal) && lines[i + 2] == "#")
            {
                section = lines[i + 1][2..];
                i += 2;
                continue;
            }

            if (HereDocument().Match(line) is { Success: true } document)
            {
                var body = new StringBuilder();

                for (i++; i < lines.Count && lines[i] != "EOF"; i++) body.Append(lines[i]).Append('\n');
                if (i >= lines.Count) return null;

                if (section == Description) files[document.Groups["path"].Value] = body.ToString();
                continue;
            }

            if (line.StartsWith("cd ", StringComparison.Ordinal))
            {
                folder = line[3..];
                continue;
            }

            if (section != Description || !(line.Contains("compile.exe", StringComparison.OrdinalIgnoreCase) || line.Contains("link.exe", StringComparison.OrdinalIgnoreCase)))
                continue;

            if (Words(line) is not { Count: > 0 } words) return null;

            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (words.Count > 0 && Assignment().Match(words[0]) is { Success: true } assignment)
            {
                environment[assignment.Groups["name"].Value] = assignment.Groups["value"].Value;
                words.RemoveAt(0);
            }

            if (words.Count == 0 || folder is null) return null;

            var inPackageFolder = folder.Equals(probeFolder, StringComparison.OrdinalIgnoreCase);
            if (!inPackageFolder && folder != ".") return null;

            commands.Add(new Command(words[0], words.Skip(1).ToList(), environment, inPackageFolder));
        }

        // One compile of this file, then at most one link, and nothing it writes outside the work folder.
        if (commands.Count is < 1 or > 2 || !commands[0].Program.EndsWith("compile.exe", StringComparison.OrdinalIgnoreCase)) return null;
        if (commands.Count == 2 && !commands[1].Program.EndsWith("link.exe", StringComparison.OrdinalIgnoreCase)) return null;
        if (commands[0].Arguments.Count(a => a.Equals(probe, StringComparison.OrdinalIgnoreCase)) != 1) return null;
        if (files.Keys.Any(path => !path.StartsWith("$WORK\\", StringComparison.Ordinal))) return null;

        foreach (var command in commands)
        {
            if (command.Arguments.Any(a => !a.Equals(probe, StringComparison.OrdinalIgnoreCase) && a.Contains(probeFolder, StringComparison.OrdinalIgnoreCase)))
                return null;
        }

        var cached = new List<string>();

        foreach (var body in files.Values)
        {
            foreach (var entry in body.Split('\n'))
            {
                if (!entry.StartsWith("packagefile ", StringComparison.Ordinal)) continue;

                var path = entry[(entry.IndexOf('=') + 1)..];
                if (path.StartsWith("$WORK", StringComparison.Ordinal)) continue;
                if (!File.Exists(path)) return null;

                cached.Add(path);
            }
        }

        var tools = commands.Select(c => new FileInfo(c.Program)).ToList();
        if (tools.Any(t => !t.Exists)) return null;

        return new Plan(Description, files, commands, probeFolder, cached.Distinct().ToList(),
            tools.Select(t => (t.FullName, t.LastWriteTimeUtc, t.Length)).ToList());
    }

    /// <summary>
    /// What go build would report for a copy of a file at <paramref name="copy"/>, built in
    /// <paramref name="folder"/>, or null when this plan cannot say.
    /// </summary>
    internal static async Task<CheckResult?> RunAsync(Plan plan, string copy, string folder, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // A plan is only as good as the cache and tools it names: go cleans its cache, and Go gets updated.
        if (plan.CachedPackages.Any(path => !File.Exists(path))) return null;

        foreach (var (path, written, length) in plan.Tools)
        {
            var tool = new FileInfo(path);
            if (!tool.Exists || tool.LastWriteTimeUtc != written || tool.Length != length) return null;
        }

        var work = Path.Combine(folder, "work");
        Directory.CreateDirectory(Path.Combine(work, "b001", "exe"));

        string Place(string text) => text.Replace("$WORK", work, StringComparison.Ordinal);

        foreach (var (path, body) in plan.Files)
            await File.WriteAllTextAsync(Place(path), Place(body), new UTF8Encoding(false), cancellationToken);

        foreach (var command in plan.Commands)
        {
            var arguments = command.Arguments.Select(a => a.Equals(Path.Combine(plan.ProbeFolder, Path.GetFileName(copy)), StringComparison.OrdinalIgnoreCase) ? copy : Place(a));

            var spec = new TargetSpec
            {
                ExecutablePath = command.Program,
                Arguments = string.Join(" ", arguments.Select(Quote)),
                WorkingDirectory = folder,
                Timeout = timeout,
                ExtraEnvironment = command.Environment,
            };

            var run = await new TargetRunner(new ParserRegistry()).RunAsync(spec, cancellationToken);

            if (run.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut or RunOutcome.Cancelled) return null;

            var output = run.Lines.OrderBy(l => l.Sequence).Select(l => l.Text).ToList();

            if (run.ExitCode == 0)
            {
                if (output.Count > 0) return null;
                continue;
            }

            if (output.Count == 0) return null;

            var lines = new List<string> { "# " + plan.Description };

            foreach (var text in output)
            {
                var shortened = text;

                if (command.InPackageFolder)
                {
                    shortened = ReplacePrefix(shortened, folder, ".");
                    shortened = ReplacePrefix(shortened, folder.Replace('\\', '/'), ".");
                }

                lines.Add(ReplacePrefix(shortened, work, "$WORK"));
            }

            var captured = lines.Select((text, i) => new CapturedLine(i, StreamKind.StdErr, text, TimeSpan.Zero)).ToList();

            return new CheckResult(true, 1, captured, CompileCheck.ErrorsIn(new ParserRegistry(), captured));
        }

        return new CheckResult(true, 0, [], []);
    }

    /// <summary>
    /// The go command's own rewriting of a tool's output, one line at a time: a path replaced where it starts
    /// the line, follows a tab at the start of the line, or follows a space.
    /// </summary>
    internal static string ReplacePrefix(string line, string old, string replacement)
    {
        if (!line.Contains(old, StringComparison.Ordinal)) return line;

        line = line.Replace(" " + old, " " + replacement, StringComparison.Ordinal);

        if (line.StartsWith("\t" + old, StringComparison.Ordinal)) line = "\t" + replacement + line[(1 + old.Length)..];
        if (line.StartsWith(old, StringComparison.Ordinal)) line = replacement + line[old.Length..];

        return line;
    }

    /// <summary>The words of one printed command: NAME='value' assignments, Go-quoted strings, and bare words.</summary>
    internal static List<string>? Words(string line)
    {
        var words = new List<string>();

        foreach (Match match in Word().Matches(line))
        {
            var word = match.Value;

            if (word.StartsWith('"'))
            {
                var text = new StringBuilder();

                for (var i = 1; i < word.Length - 1; i++)
                {
                    if (word[i] != '\\') { text.Append(word[i]); continue; }
                    if (word[i + 1] is not ('\\' or '"')) return null;

                    text.Append(word[++i]);
                }

                word = text.ToString();
            }

            words.Add(word);
        }

        return words;
    }

    private static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) return argument;

        var quoted = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var c in argument)
        {
            if (c == '\\') { backslashes++; continue; }

            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    [GeneratedRegex(@"^cat >(?<path>\S+) << 'EOF' # internal$")]
    private static partial Regex HereDocument();

    [GeneratedRegex(@"^(?<name>\w+)='(?<value>[^']*)'$")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\w+='[^']*'|""(?:[^""\\]|\\.)*""|\S+")]
    private static partial Regex Word();
}
