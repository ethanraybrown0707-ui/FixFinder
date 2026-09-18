using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>index out of range [3] with length 3</c> - a loop that runs to <c>&lt;= len(...)</c>.</summary>
public sealed partial class GoIndexLoop : ILocalFixRule
{
    public string Id => "go-index-loop";

    [GeneratedRegex(@"index out of range \[(?<index>\d+)\] with length (?<length>\d+)")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.RuntimeMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;
        if (message.Groups["index"].Value != message.Groups["length"].Value) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var loop = new Regex(@"\bfor\s+(?<var>\w+)\s*:=\s*0\s*;\s*\k<var>\s*(?<op><=)\s*len\((?<slice>[^)]+)\)\s*;");

        for (var k = at.Number - 1; k >= Math.Max(0, at.Number - 16); k--)
        {
            if (loop.Match(masked[k]) is not { Success: true } header) continue;

            var op = header.Groups["op"];
            var original = at.Source.Lines[k];

            return LocalFix.ReplaceLine(
                Id, "Stop the loop at the last index: <",
                $"`{header.Groups["slice"].Value}` has {message.Groups["length"].Value} elements, numbered 0 to " +
                $"{int.Parse(message.Groups["length"].Value) - 1}. The loop on line {k + 1} runs while `{header.Groups["var"].Value} <= len(...)`, " +
                "so its last step asks for one past the end.",
                at.Source.Path, k + 1, original[..op.Index] + "<" + original[(op.Index + op.Length)..]);
        }

        return null;
    }
}

/// <summary><c>assignment to entry in nil map</c> - a map declared but never made.</summary>
public sealed partial class GoNilMap : ILocalFixRule
{
    public string Id => "go-nil-map";

    [GeneratedRegex(@"^assignment to entry in nil map$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.RuntimeMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Regex.Matches(masked[at.Number - 1], @"(?<![\w.])(?<map>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?)\s*\[").ToList() is not [var use]) return null;

        var map = use.Groups["map"].Value;

        if (!map.Contains('.'))
        {
            var declaration = new Regex($@"^(?<lead>\s*)var\s+{Regex.Escape(map)}\s+(?<type>map\[[^\]]+\]\S+)\s*$");
            var lines = Enumerable.Range(0, at.Number - 1).Where(i => declaration.IsMatch(masked[i])).ToList();
            if (lines is not [var index]) return null;

            var match = declaration.Match(source.Lines[index]);

            return LocalFix.ReplaceLine(
                Id, $"Make the map: {map} := make({match.Groups["type"].Value})",
                $"`var {map} {match.Groups["type"].Value}` declares a map but does not make one - it is `nil`, and storing into a nil map panics. " +
                "`make` creates the map to store into.",
                source.Path, index + 1, $"{match.Groups["lead"].Value}{map} := make({match.Groups["type"].Value})");
        }

        var parts = map.Split('.');
        var literal = new Regex($@"(?<![\w.]){Regex.Escape(parts[0])}\s*:=\s*(?<type>[A-Z]\w*)\{{\s*\}}");

        for (var i = at.Number - 2; i >= 0; i--)
        {
            if (literal.Match(masked[i]) is not { Success: true } made) continue;

            var type = made.Groups["type"].Value;
            var field = masked.Select(text => Regex.Match(text, $@"^\s*{Regex.Escape(parts[1])}\s+(?<type>map\[[^\]]+\]\S+)\s*$")).FirstOrDefault(m => m.Success);
            if (field is null || !masked.Any(text => Regex.IsMatch(text, $@"^type\s+{Regex.Escape(type)}\s+struct\b"))) return null;

            var braces = made.Index + made.Length;
            var original = source.Lines[i];
            var open = original.LastIndexOf('{', braces - 1);

            return LocalFix.ReplaceLine(
                Id, $"Make the map in {type}{{...}}",
                $"`{type}{{}}` leaves its `{parts[1]}` map `nil`, and storing into a nil map panics. Making it as the struct is created - " +
                $"`{parts[1]}: make({field.Groups["type"].Value})` - gives it a map to store into.",
                source.Path, i + 1, original[..(open + 1)] + $"{parts[1]}: make({field.Groups["type"].Value})" + original[(braces - 1)..]);
        }

        return null;
    }
}

/// <summary><c>all goroutines are asleep - deadlock!</c> from a channel nothing else will ever touch.</summary>
public sealed partial class GoChannelDeadlock : ILocalFixRule
{
    public string Id => "go-channel-deadlock";

    [GeneratedRegex(@"^all goroutines are asleep - deadlock!$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.RuntimeMessage(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var raw = context.Error.RawText;
        var line = masked[at.Number - 1];

        if (GoCode.EnclosingFunction(masked, at.Number - 1) is not { } function) return null;
        var body = Enumerable.Range(function.Header + 1, function.Close - function.Header - 1).ToList();

        if (raw.Contains("(nil chan)", StringComparison.Ordinal))
        {
            if (Regex.Match(line, @"<-\s*(?<ch>[A-Za-z_]\w*)|(?<ch>[A-Za-z_]\w*)\s*<-") is not { Success: true } use) return null;

            var declaration = new Regex($@"^(?<lead>\s*)var\s+{Regex.Escape(use.Groups["ch"].Value)}\s+(?<type>chan\s+\S+)\s*$");
            var lines = body.Where(i => declaration.IsMatch(masked[i])).ToList();
            if (lines is not [var index]) return null;

            var match = declaration.Match(source.Lines[index]);

            return LocalFix.ReplaceLine(
                Id, "Make the channel",
                $"`var {use.Groups["ch"].Value} {match.Groups["type"].Value}` declares a channel without making one. A nil channel blocks every send " +
                "and receive for ever, so nothing could move. `make` creates the channel.",
                source.Path, index + 1, $"{match.Groups["lead"].Value}{use.Groups["ch"].Value} := make({match.Groups["type"].Value})");
        }

        if (body.Any(i => Regex.IsMatch(masked[i], @"^\s*go\s"))) return null;

        if (raw.Contains("[chan send]", StringComparison.Ordinal) && Regex.Match(line, @"^\s*(?<ch>[A-Za-z_]\w*)\s*<-") is { Success: true } send)
        {
            var ch = Regex.Escape(send.Groups["ch"].Value);
            var made = new Regex($@"(?<![\w.]){ch}\s*:=\s*make\(\s*chan\s+(?<type>[^,)]+)(?<close>\))");
            var lines = body.Where(i => made.IsMatch(masked[i])).ToList();
            if (lines is not [var index]) return null;

            var receive = body.FirstOrDefault(i => Regex.IsMatch(masked[i], $@"<-\s*{ch}\b"), -1);
            var sends = body.Count(i => i > index && (receive < 0 || i < receive) && Regex.IsMatch(masked[i], $@"^\s*{ch}\s*<-"));
            if (sends == 0) return null;

            var close = made.Match(masked[index]).Groups["close"].Index;
            var original = source.Lines[index];

            return LocalFix.ReplaceLine(
                Id, $"Give the channel room for {sends}: make(chan ..., {sends})",
                "A send on an unbuffered channel waits until another goroutine receives it - and here nothing else is running, so it waits " +
                $"for ever. A buffer of {sends} lets the sends complete before this function goes on to receive them.",
                source.Path, index + 1, original[..close] + $", {sends}" + original[close..]);
        }

        if (raw.Contains("[chan receive]", StringComparison.Ordinal) && Regex.Match(line, @"^\s*for\s+.*\brange\s+(?<ch>[A-Za-z_]\w*)\s*\{") is { Success: true } range)
        {
            var ch = Regex.Escape(range.Groups["ch"].Value);
            if (body.Any(i => Regex.IsMatch(masked[i], $@"\bclose\(\s*{ch}\s*\)"))) return null;
            if (!body.Any(i => i < at.Number - 1 && Regex.IsMatch(masked[i], $@"^\s*{ch}\s*<-"))) return null;

            return LocalFix.Insert(
                Id, $"Close {range.Groups["ch"].Value} before ranging over it",
                "`range` over a channel keeps receiving until the channel is closed. Nothing closes this one, so once the values sent are " +
                "used up it waits for ever. `close` after the last send lets the loop finish.",
                source.Path, at.Number, [CodeText.Indentation(source.Lines[at.Number - 1]) + $"close({range.Groups["ch"].Value})"]);
        }

        return null;
    }
}

/// <summary>A deadlock at <c>wg.Wait()</c> - a <c>sync.WaitGroup</c> passed by value, so each goroutine marks its own copy done.</summary>
public sealed partial class GoWaitGroupByValue : ILocalFixRule
{
    public string Id => "go-waitgroup-by-value";

    [GeneratedRegex(@"^all goroutines are asleep - deadlock!$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.RuntimeMessage(context.Error, Message()) is null || !context.Error.RawText.Contains("WaitGroup", StringComparison.Ordinal)) return null;

        var folders = context.Error.Frames
            .Select(f => f.File)
            .Where(f => f is not null && f.EndsWith(".go", StringComparison.OrdinalIgnoreCase) && !Parsing.FrameClassifier.IsVendored(f) && File.Exists(f))
            .Select(f => Path.GetDirectoryName(f!)!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var sources = folders
            .SelectMany(d => Directory.EnumerateFiles(d, "*.go").Take(200))
            .Select(SourceFile.Read)
            .OfType<SourceFile>()
            .Concat(GoCode.SourceFiles(context))
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First());

        foreach (var source in sources)
        {
            var lines = source.Lines;
            var masked = CodeText.MaskAll(lines, Syntax.CLike);
            var definitions = Enumerable.Range(0, masked.Count)
                .Select(i => (Index: i, Match: Regex.Match(masked[i], @"^func\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)")))
                .Where(x => x.Match.Success && Regex.IsMatch(x.Match.Groups["parameters"].Value, @"\b\w+\s+sync\.WaitGroup\b"))
                .ToList();

            if (definitions.Count == 0) continue;
            if (definitions is not [var definition]) return null;

            var function = definition.Match.Groups["function"].Value;
            var parameters = definition.Match.Groups["parameters"].Value.Split(',').Select(p => p.Trim()).ToList();
            var position = parameters.FindIndex(p => Regex.IsMatch(p, @"^\w+\s+sync\.WaitGroup$"));
            if (position < 0) return null;

            var calls = Enumerable.Range(0, masked.Count)
                .Where(i => i != definition.Index && Regex.IsMatch(masked[i], $@"(?<![\w.]){Regex.Escape(function)}\s*\("))
                .ToList();

            if (calls.Count == 0) return null;

            var changed = new Dictionary<int, string>
            {
                [definition.Index] = Regex.Replace(lines[definition.Index], @"(?<=\b\w+\s+)sync\.WaitGroup\b", "*sync.WaitGroup"),
            };

            foreach (var call in calls)
            {
                var text = masked[call];
                var at = Regex.Match(text, $@"(?<![\w.]){Regex.Escape(function)}\s*\(");
                var open = at.Index + at.Length - 1;
                if (Brackets.ClosingParenthesis(text, open) is not { } close) return null;

                var arguments = CppCode.SplitTopLevel(text, open + 1, close, ',');
                if (arguments.Count != parameters.Count) return null;

                var (start, end) = arguments[position];
                var argument = lines[call][start..end];
                var trimmed = argument.Trim();
                if (!Regex.IsMatch(trimmed, @"^[A-Za-z_]\w*$")) return null;

                var offset = start + argument.IndexOf(trimmed, StringComparison.Ordinal);
                changed[call] = lines[call][..offset] + "&" + lines[call][offset..];
            }

            var first = changed.Keys.Min();
            var last = changed.Keys.Max();

            return new LocalFix
            {
                RuleId = Id,
                Title = $"Share one WaitGroup: pass &wg, take *sync.WaitGroup",
                Explanation =
                    $"`{function}` takes its `sync.WaitGroup` by value, so every goroutine gets a copy and calls `Done` on the copy. The " +
                    "WaitGroup that `Wait` is watching never hears about it and waits forever - Go notices nothing else can run and stops " +
                    "with a deadlock. A pointer shares the one WaitGroup.",
                File = source.Path, StartLine = first + 1, RemoveCount = last - first + 1,
                NewLines = Enumerable.Range(first, last - first + 1).Select(i => changed.TryGetValue(i, out var line) ? line : lines[i]).ToList(),
            };
        }

        return null;
    }
}

/// <summary>A deadlock at <c>for v := range ch</c> - the goroutine sending on the channel never closes it, so the loop never
/// ends.</summary>
public sealed partial class GoCloseChannel : ILocalFixRule
{
    public string Id => "go-close-channel";

    [GeneratedRegex(@"^all goroutines are asleep - deadlock!$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.RuntimeMessage(context.Error, Message()) is null || !context.Error.RawText.Contains("[chan receive]", StringComparison.Ordinal)) return null;
        if (GoCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);

        if (Regex.Match(masked[at.Number - 1], @"\bfor\s+(?:[\w\s,]*:?=\s*)?range\s+(?<channel>[A-Za-z_]\w*)\s*\{") is not { Success: true } loop) return null;
        if (GoCode.EnclosingFunction(masked, at.Number - 1) is not { } function) return null;

        var channel = loop.Groups["channel"].Value;

        var starts = Enumerable.Range(function.Header, at.Number - 1 - function.Header)
            .Select(i => Regex.Match(masked[i], $@"^\s*go\s+(?<function>[A-Za-z_]\w*)\s*\((?<arguments>[^)]*)\)"))
            .Where(m => m.Success && Regex.IsMatch(m.Groups["arguments"].Value, $@"(?<![\w.]){Regex.Escape(channel)}(?!\w)"))
            .ToList();

        if (starts is not [var start]) return null;

        var arguments = start.Groups["arguments"].Value.Split(',').Select(a => a.Trim()).ToList();
        var position = arguments.IndexOf(channel);
        if (position < 0) return null;

        var sender = start.Groups["function"].Value;
        var headers = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^func\s+{Regex.Escape(sender)}\s*\(")).ToList();
        if (headers is not [var header] || GoCode.EnclosingFunction(masked, header) is not { } body) return null;

        var parameters = Regex.Match(masked[header], @"\((?<parameters>[^)]*)\)").Groups["parameters"].Value.Split(',').Select(p => p.Trim()).ToList();
        if (parameters.Count != arguments.Count || Regex.Match(parameters[position], @"^(?<name>\w+)\s+(?:chan|chan<-)\s") is not { Success: true } parameter) return null;

        var name = parameter.Groups["name"].Value;
        var inside = Enumerable.Range(header + 1, body.Close - header - 1).ToList();

        if (!inside.Any(i => Regex.IsMatch(masked[i], $@"(?<![\w.]){Regex.Escape(name)}\s*<-"))) return null;
        if (inside.Any(i => Regex.IsMatch(masked[i], $@"\bclose\s*\(\s*{Regex.Escape(name)}\s*\)|\breturn\b"))) return null;

        var indent = inside.Select(i => lines[i]).FirstOrDefault(l => l.Trim().Length > 0) is { } first ? CodeText.Indentation(first) : "\t";

        return LocalFix.Insert(
            Id, $"Close {name} when {sender} has sent everything",
            $"`for ... range {channel}` keeps receiving until the channel is closed, and `{sender}` sends its values and then returns " +
            $"without closing it - so the loop waits for a value that never comes, and Go stops the program as a deadlock. `close({name})` " +
            "at the end of the sender tells the loop there is nothing more.",
            source.Path, body.Close + 1, [$"{indent}close({name})"]);
    }
}
