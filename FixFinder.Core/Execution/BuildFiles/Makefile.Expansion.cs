using System.Text;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>How make expands $(...): variables, substitution references, automatic variables and the text functions.</summary>
internal sealed partial class Makefile
{
    /// <summary>How deep one reference may lead to another before it is taken to refer to itself.</summary>
    private const int MostExpansionDepth = 64;

    /// <summary>The variables GNU make starts with - the ones its own rules are written in - and their values.</summary>
    private static readonly Dictionary<string, string> BuiltInVariables = new(StringComparer.Ordinal)
    {
        ["CC"] = "cc",
        ["CXX"] = "g++",
        ["AR"] = "ar",
        ["ARFLAGS"] = "rv",
        ["RM"] = "rm -f",
        ["MAKE"] = "make",
        ["SHELL"] = "/bin/sh",
        ["OUTPUT_OPTION"] = "-o $@",
        ["COMPILE.c"] = "$(CC) $(CFLAGS) $(CPPFLAGS) $(TARGET_ARCH) -c",
        ["COMPILE.cc"] = "$(CXX) $(CXXFLAGS) $(CPPFLAGS) $(TARGET_ARCH) -c",
        ["COMPILE.cpp"] = "$(COMPILE.cc)",
        ["LINK.o"] = "$(CC) $(LDFLAGS) $(TARGET_ARCH)",
        ["LINK.c"] = "$(CC) $(CFLAGS) $(CPPFLAGS) $(LDFLAGS) $(TARGET_ARCH)",
        ["LINK.cc"] = "$(CXX) $(CXXFLAGS) $(CPPFLAGS) $(LDFLAGS) $(TARGET_ARCH)",
        ["LINK.cpp"] = "$(LINK.cc)",
    };

    private static readonly HashSet<string> FunctionNames = new(StringComparer.Ordinal)
    {
        "subst", "patsubst", "strip", "findstring", "filter", "filter-out", "sort", "word", "wordlist", "words", "firstword",
        "lastword", "dir", "notdir", "suffix", "basename", "addsuffix", "addprefix", "join", "wildcard", "realpath", "abspath",
        "if", "or", "and", "foreach", "call", "value", "eval", "origin", "flavor", "shell", "info", "warning", "error", "file",
        "guile", "let", "intcmp",
    };

    /// <summary>What a recipe is expanded for: the target and its prerequisites, for $@, $&lt;, $^ and $*.</summary>
    private sealed record Automatic(string Target, IReadOnlyList<string> Prerequisites, IReadOnlyList<string> OrderOnly, string? Stem);

    /// <summary>
    /// Where text is expanded: for which target, with what $(call ...) passed and what $(foreach ...) has set - and how
    /// deep in references it already is.
    /// </summary>
    private sealed record Scope(
        MakeTarget? Target,
        Automatic? Automatic,
        IReadOnlyList<string>? CallArguments,
        IReadOnlyDictionary<string, string>? LoopVariables,
        int Depth)
    {
        public static readonly Scope Everywhere = new(null, null, null, null, 0);

        public Scope Deeper => this with { Depth = Depth + 1 };
    }

    private string Expand(string text, Scope scope)
    {
        if (!text.Contains('$')) return text;
        if (scope.Depth > MostExpansionDepth) return Unknowable.Mark("a variable that refers to itself");

        var expanded = new StringBuilder();

        for (var at = 0; at < text.Length; at++)
        {
            var character = text[at];

            if (character != '$' || at + 1 == text.Length)
            {
                expanded.Append(character);
                continue;
            }

            var next = text[++at];

            if (next is '(' or '{')
            {
                var closing = Closing(text, at);

                if (closing < 0)
                {
                    expanded.Append(text, at - 1, text.Length - at + 1);
                    break;
                }

                expanded.Append(Reference(text[(at + 1)..closing], scope.Deeper));
                at = closing;
            }
            else if (next == '$')
            {
                expanded.Append('$');
            }
            else
            {
                expanded.Append(Reference(next.ToString(), scope.Deeper));
            }
        }

        return expanded.ToString();
    }

    /// <summary>Where the reference opened at <paramref name="opening"/> closes, counting the brackets of its own kind inside it.</summary>
    private static int Closing(string text, int opening)
    {
        var open = text[opening];
        var close = open == '(' ? ')' : '}';
        var depth = 0;

        for (var at = opening; at < text.Length; at++)
        {
            if (text[at] == open) depth++;
            else if (text[at] == close && --depth == 0) return at;
        }

        return -1;
    }

    private string Reference(string inside, Scope scope)
    {
        var space = inside.IndexOfAny([' ', '\t']);
        if (space > 0 && FunctionNames.Contains(inside[..space])) return Function(inside[..space], inside[(space + 1)..].TrimStart(), scope);

        if (SubstitutionReference(inside) is { } substitution)
        {
            var from = Expand(substitution.From, scope);
            var to = Expand(substitution.To, scope);
            var pattern = from.Contains('%') ? from : "%" + from;
            var replacement = from.Contains('%') ? to : "%" + to;

            return string.Join(" ", Words(Value(Expand(substitution.Name, scope), scope)).Select(word => PatternSubstitute(pattern, replacement, word)));
        }

        return Value(Expand(inside, scope), scope);
    }

    /// <summary>$(SOURCES:.c=.o) and $(SOURCES:src/%.c=obj/%.o): a variable's words, each changed as patsubst would.</summary>
    private static (string Name, string From, string To)? SubstitutionReference(string inside)
    {
        var colon = TopLevelIndexOf(inside, ':');
        if (colon <= 0) return null;

        var equals = TopLevelIndexOf(inside[(colon + 1)..], '=');
        return equals < 0 ? null : (inside[..colon], inside[(colon + 1)..(colon + 1 + equals)], inside[(colon + 2 + equals)..]);
    }

    /// <summary>A variable's value as it is used, expanded: from a loop or a $(call ...), automatic, the target's own, the Makefile's, the environment's, or make's own.</summary>
    private string Value(string name, Scope scope)
    {
        if (scope.LoopVariables?.TryGetValue(name, out var looped) == true) return looped;

        if (scope.CallArguments is { } arguments && name.Length > 0 && name.All(char.IsAsciiDigit))
            return int.TryParse(name, out var position) && position < arguments.Count ? arguments[position] : "";

        if (scope.Automatic is { } automatic && AutomaticValue(name, automatic) is { } automaticValue) return automaticValue;

        if (scope.Target is { } target && target.OwnVariables.Any(own => own.Name == name)) return TargetValue(name, target, scope);

        if (_variables.TryGetValue(name, out var variable)) return variable.IsSimple ? variable.Value : Expand(variable.Value, scope);

        if (FromTheEnvironment(name) is { } environmentValue) return environmentValue;

        return BuiltInVariables.TryGetValue(name, out var builtIn) ? Expand(builtIn, scope) : "";
    }

    /// <summary>A variable given a value for one target: its value everywhere, then each of the target's own assignments in turn.</summary>
    private string TargetValue(string name, MakeTarget target, Scope scope)
    {
        var everywhere = scope with { Target = null };
        var value = _variables.ContainsKey(name) || FromTheEnvironment(name) is not null || BuiltInVariables.ContainsKey(name)
            ? Value(name, everywhere)
            : null;

        foreach (var assignment in target.OwnVariables.Where(own => own.Name == name))
        {
            var assigned = Expand(assignment.Value, everywhere);

            value = assignment.Operator switch
            {
                "+=" => Joined(value ?? "", assigned),
                "?=" => value ?? assigned,
                "!=" => Unknowable.Mark($"{name} != {assignment.Value}"),
                _ => assigned,
            };
        }

        return value ?? "";
    }

    /// <summary>A variable's value as written, before it is expanded, or null when it has none.</summary>
    private string? UnexpandedValue(string name) =>
        _variables.TryGetValue(name, out var variable) ? variable.Value
        : FromTheEnvironment(name) ?? (BuiltInVariables.TryGetValue(name, out var builtIn) ? builtIn : null);

    /// <summary>make takes every environment variable as a variable the Makefile can use - OS is Windows_NT on Windows.</summary>
    private static string? FromTheEnvironment(string name) =>
        name.Length > 0 && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') ? Environment.GetEnvironmentVariable(name) : null;

    private static string? AutomaticValue(string name, Automatic automatic)
    {
        if (name.Length is 0 or > 2 || (name.Length == 2 && name[1] is not ('D' or 'F'))) return null;

        IReadOnlyList<string>? words = name[0] switch
        {
            '@' => [automatic.Target],
            '<' => automatic.Prerequisites.Take(1).ToList(),
            '^' or '?' => automatic.Prerequisites.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            '+' => automatic.Prerequisites,
            '*' => automatic.Stem is { } stem ? [stem] : [],
            '|' => automatic.OrderOnly,
            _ => null,
        };

        if (words is null) return null;

        var parts = name.Length == 1 ? words : name[1] == 'D' ? words.Select(DirectoryPart) : words.Select(NamePart);
        return string.Join(" ", parts);
    }

    /// <summary>$(@D): the folder part, without the slash after it, or . when there is none.</summary>
    private static string DirectoryPart(string path)
    {
        var slash = path.Replace('\\', '/').LastIndexOf('/');
        return slash < 0 ? "." : slash == 0 ? "/" : path[..slash];
    }

    private static string NamePart(string path) => path[(path.Replace('\\', '/').LastIndexOf('/') + 1)..];

    private string Function(string name, string arguments, Scope scope)
    {
        switch (name)
        {
            case "subst":
            {
                var parts = Arguments(arguments, 3);
                if (parts.Count < 3) return "";

                var from = Expand(parts[0], scope);
                var text = Expand(parts[2], scope);
                return from.Length == 0 ? text : text.Replace(from, Expand(parts[1], scope), StringComparison.Ordinal);
            }

            case "patsubst":
            {
                var parts = Arguments(arguments, 3);
                if (parts.Count < 3) return "";

                var pattern = Expand(parts[0], scope).Trim();
                var replacement = Expand(parts[1], scope).Trim();
                return string.Join(" ", Words(Expand(parts[2], scope)).Select(word => PatternSubstitute(pattern, replacement, word)));
            }

            case "strip":
                return string.Join(" ", Words(Expand(arguments, scope)));

            case "findstring":
            {
                var parts = Arguments(arguments, 2);
                if (parts.Count < 2) return "";

                var wanted = Expand(parts[0], scope);
                return Expand(parts[1], scope).Contains(wanted, StringComparison.Ordinal) ? wanted : "";
            }

            case "filter" or "filter-out":
            {
                var parts = Arguments(arguments, 2);
                if (parts.Count < 2) return "";

                var patterns = Words(Expand(parts[0], scope));
                var keep = name == "filter";
                return string.Join(" ", Words(Expand(parts[1], scope)).Where(word => patterns.Any(pattern => StemOf(pattern, word) is not null) == keep));
            }

            case "sort":
                return string.Join(" ", Words(Expand(arguments, scope)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

            case "word":
            {
                var parts = Arguments(arguments, 2);
                var words = parts.Count < 2 ? [] : Words(Expand(parts[1], scope));
                return parts.Count == 2 && int.TryParse(Expand(parts[0], scope).Trim(), out var position) && position >= 1 && position <= words.Count
                    ? words[position - 1]
                    : "";
            }

            case "wordlist":
            {
                var parts = Arguments(arguments, 3);
                if (parts.Count < 3 || !int.TryParse(Expand(parts[0], scope).Trim(), out var first) ||
                    !int.TryParse(Expand(parts[1], scope).Trim(), out var last) || first < 1) return "";

                return string.Join(" ", Words(Expand(parts[2], scope)).Skip(first - 1).Take(Math.Max(0, last - first + 1)));
            }

            case "words":
                return Words(Expand(arguments, scope)).Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

            case "firstword":
                return Words(Expand(arguments, scope)).FirstOrDefault() ?? "";

            case "lastword":
                return Words(Expand(arguments, scope)).LastOrDefault() ?? "";

            case "dir":
                return string.Join(" ", Words(Expand(arguments, scope)).Select(word => word.Replace('\\', '/').LastIndexOf('/') is var slash and >= 0 ? word[..(slash + 1)] : "./"));

            case "notdir":
                return string.Join(" ", Words(Expand(arguments, scope)).Select(NamePart));

            case "suffix":
                return string.Join(" ", Words(Expand(arguments, scope)).Select(word => NamePart(word).LastIndexOf('.') is var dot and >= 0 ? NamePart(word)[dot..] : "").Where(suffix => suffix.Length > 0));

            case "basename":
                return string.Join(" ", Words(Expand(arguments, scope)).Select(word => NamePart(word).LastIndexOf('.') is var dot and >= 0 ? word[..(word.Length - NamePart(word).Length + dot)] : word));

            case "addsuffix" or "addprefix":
            {
                var parts = Arguments(arguments, 2);
                if (parts.Count < 2) return "";

                var added = Expand(parts[0], scope);
                return string.Join(" ", Words(Expand(parts[1], scope)).Select(word => name == "addsuffix" ? word + added : added + word));
            }

            case "join":
            {
                var parts = Arguments(arguments, 2);
                if (parts.Count < 2) return "";

                var firsts = Words(Expand(parts[0], scope));
                var seconds = Words(Expand(parts[1], scope));
                return string.Join(" ", Enumerable.Range(0, Math.Max(firsts.Count, seconds.Count))
                    .Select(position => (position < firsts.Count ? firsts[position] : "") + (position < seconds.Count ? seconds[position] : "")));
            }

            case "wildcard":
                return string.Join(" ", Words(Expand(arguments, scope)).SelectMany(pattern =>
                    Unknowable.IsIn(pattern) ? [pattern]
                    : PathPattern.HasWildcards(pattern) ? PathPattern.Matches(_folder, pattern)
                    : File.Exists(Full(pattern, _folder)) || Directory.Exists(Full(pattern, _folder)) ? [pattern] : []));

            case "abspath" or "realpath":
                return string.Join(" ", Words(Expand(arguments, scope))
                    .Select(word => Full(word, _folder))
                    .Where(path => name == "abspath" || File.Exists(path) || Directory.Exists(path))
                    .Select(path => path.Replace('\\', '/')));

            case "if":
            {
                var parts = Arguments(arguments, 3);
                return Expand(parts[0], scope).Trim().Length > 0
                    ? parts.Count > 1 ? Expand(parts[1], scope) : ""
                    : parts.Count > 2 ? Expand(parts[2], scope) : "";
            }

            case "or":
                return Arguments(arguments, int.MaxValue).Select(part => Expand(part, scope).Trim()).FirstOrDefault(value => value.Length > 0) ?? "";

            case "and":
            {
                var last = "";

                foreach (var part in Arguments(arguments, int.MaxValue))
                {
                    last = Expand(part, scope).Trim();
                    if (last.Length == 0) return "";
                }

                return last;
            }

            case "foreach":
            {
                var parts = Arguments(arguments, 3);
                if (parts.Count < 3) return "";

                var variable = Expand(parts[0], scope).Trim();
                var results = Words(Expand(parts[1], scope)).Select(word =>
                {
                    var loop = new Dictionary<string, string>(scope.LoopVariables ?? new Dictionary<string, string>(), StringComparer.Ordinal) { [variable] = word };
                    return Expand(parts[2], scope with { LoopVariables = loop });
                });

                return string.Join(" ", results);
            }

            case "call":
            {
                var parts = Arguments(arguments, int.MaxValue);
                var called = Expand(parts[0], scope).Trim();
                var passed = parts.Skip(1).Select(part => Expand(part, scope)).Prepend(called).ToList();

                if (FunctionNames.Contains(called)) return Function(called, string.Join(",", passed.Skip(1)), scope);

                return UnexpandedValue(called) is { } body ? Expand(body, scope with { CallArguments = passed }) : "";
            }

            case "value":
                return UnexpandedValue(Expand(arguments, scope).Trim()) ?? "";

            case "origin":
            {
                var variable = Expand(arguments, scope).Trim();
                return _variables.ContainsKey(variable) ? "file"
                    : FromTheEnvironment(variable) is not null ? "environment"
                    : BuiltInVariables.ContainsKey(variable) ? "default"
                    : "undefined";
            }

            case "flavor":
            {
                var variable = Expand(arguments, scope).Trim();
                return _variables.TryGetValue(variable, out var found) ? found.IsSimple ? "simple" : "recursive"
                    : UnexpandedValue(variable) is not null ? "recursive"
                    : "undefined";
            }

            case "eval":
                Evaluate(Expand(arguments, scope));
                return "";

            case "info" or "warning":
                Expand(arguments, scope);
                return "";

            case "shell":
                return Unknowable.Mark($"$(shell {arguments.Trim()})");

            case "error":
                NotFollowed($"it stops with $(error {arguments.Trim()}), which make would stop at too");
                return "";

            default:
                return Unknowable.Mark($"$({name} {arguments.Trim()})");
        }
    }

    /// <summary>$(eval ...): text that is read as if it were written in the Makefile there - how a template makes a rule for each program.</summary>
    private void Evaluate(string text)
    {
        if (++_evaluations > MostEvaluations) return;

        var owner = _recipeOwner;
        _recipeOwner = null;
        ReadLines(LogicalLines(text), depth: 0);
        _recipeOwner = owner;
    }

    /// <summary>A function's arguments, split at the commas outside any $(...) - into at most <paramref name="most"/>, the last keeping any commas left.</summary>
    private static List<string> Arguments(string text, int most)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var at = 0; at < text.Length; at++)
        {
            switch (text[at])
            {
                case '(' or '{':
                    depth++;
                    break;
                case ')' or '}':
                    depth--;
                    break;
                case ',' when depth == 0 && parts.Count < most - 1:
                    parts.Add(text[start..at]);
                    start = at + 1;
                    break;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>What % matched when <paramref name="name"/> matches <paramref name="pattern"/> - the whole name for a pattern without % that equals it - or null.</summary>
    private static string? StemOf(string pattern, string name)
    {
        var percent = pattern.IndexOf('%');
        if (percent < 0) return string.Equals(pattern, name, StringComparison.Ordinal) ? "" : null;

        var prefix = pattern[..percent];
        var suffix = pattern[(percent + 1)..];

        return name.Length >= prefix.Length + suffix.Length &&
               name.StartsWith(prefix, StringComparison.Ordinal) &&
               name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[prefix.Length..(name.Length - suffix.Length)]
            : null;
    }

    /// <summary>A prerequisite pattern with its % - the first one - standing for the stem.</summary>
    private static string WithStem(string pattern, string stem)
    {
        var percent = pattern.IndexOf('%');
        return percent < 0 ? pattern : pattern[..percent] + stem + pattern[(percent + 1)..];
    }

    private static string PatternSubstitute(string pattern, string replacement, string word)
    {
        if (StemOf(pattern, word) is not { } stem) return word;

        return pattern.Contains('%') ? WithStem(replacement, stem) : replacement;
    }
}
