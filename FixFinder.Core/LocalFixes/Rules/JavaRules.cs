using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

internal static class Java
{
    public static bool IsCompileError(ParsedError error) =>
        error.LanguageId == "java" && error.ExceptionType == "compile error";
}

/// <summary><c>cannot find symbol (symbol: class List)</c> for a class that only needed importing.</summary>
public sealed partial class JavaMissingImport : ILocalFixRule
{
    public string Id => "java-missing-import";

    // A class used for a static call - Arrays.sort(values) - is reported as a variable, not a class.
    [GeneratedRegex(@"^cannot find symbol \(symbol:\s+(?:class|variable) (?<name>[A-Z][\w$]*)")]
    private static partial Regex MissingClass();

    [GeneratedRegex(@"^\s*import\s+[\w.$]+(?:\.\*)?\s*;")]
    private static partial Regex ImportLine();

    [GeneratedRegex(@"^\s*package\s+[\w.]+\s*;")]
    private static partial Regex PackageLine();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!Java.IsCompileError(error)) return null;
        if (MissingClass().Match(error.Message ?? "") is not { Success: true } primary) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = primary.Groups["name"].Value;
        if (!JavaTypes.Packages.ContainsKey(name) || JavaTypes.IsImported(name, source.Lines)) return null;

        // Every missing class in this file the table knows, in one change: List and ArrayList
        // nearly always go missing together, and fixing them one at a time is two rounds for one mistake.
        var imports = context.AllErrors
            .Where(e => Java.IsCompileError(e) && SameFile(context, e, source))
            .Select(e => MissingClass().Match(e.Message ?? ""))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .Where(n => JavaTypes.Packages.ContainsKey(n) && !JavaTypes.IsImported(n, source.Lines))
            .Distinct(StringComparer.Ordinal)
            .Select(n => $"{JavaTypes.Packages[n]}.{n}")
            .Order(StringComparer.Ordinal)
            .ToList();

        var lines = source.Lines;
        var lastImport = LastMatch(lines, ImportLine());
        var package = LastMatch(lines, PackageLine());

        List<string> added = [.. imports.Select(i => $"import {i};")];
        int before;

        if (lastImport >= 0)
        {
            before = lastImport + 2;
        }
        else if (package >= 0)
        {
            before = package + 2;
            added.Insert(0, "");
        }
        else
        {
            before = 1;
            if (lines.Count > 0 && lines[0].Trim().Length > 0) added.Add("");
        }

        return LocalFix.Insert(
            Id,
            imports.Count == 1 ? $"Add import {imports[0]}" : $"Add {imports.Count} missing imports",
            imports.Count == 1
                ? $"`{name}` lives in {JavaTypes.Packages[name]}, and Java only sees classes outside java.lang once they are imported."
                : $"{string.Join(", ", imports.Select(i => i[(i.LastIndexOf('.') + 1)..]))} all live outside java.lang, " +
                  "and Java only sees those once they are imported.",
            source.Path, before, added);
    }

    private static int LastMatch(IReadOnlyList<string> lines, Regex pattern)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
            if (pattern.IsMatch(lines[i])) return i;

        return -1;
    }

    private static bool SameFile(LocalFixContext context, ParsedError error, SourceFile source) =>
        context.Resolve((error.CulpritFrame ?? error.Frames.FirstOrDefault())?.File) is { } path &&
        string.Equals(path, source.Path, StringComparison.OrdinalIgnoreCase);
}

/// <summary><c>cannot find symbol (symbol: variable avarage)</c>, and the name it was one letter from.</summary>
/// <remarks>
/// javac, unlike Python, gcc and clang, never suggests a name. The candidates are what the file
/// itself declares and uses, or - when the missing name is a member of a JDK class, like
/// <c>System.out.printn</c> - that class's real methods, read from the JDK with javap.
/// </remarks>
public sealed partial class JavaNearestName : ILocalFixRule
{
    public string Id => "java-nearest-name";

    [GeneratedRegex(@"^cannot find symbol \(symbol:\s+(?<kind>variable|method|class)\s+(?<name>[A-Za-z_$][\w$]*)(?:\([^)]*\))?(?:,\s*location:\s*(?<location>.+))?\)$")]
    private static partial Regex Symbol();

    [GeneratedRegex(@"^(?:variable [\w$]+ of type|class|interface) (?<type>[\w.$]+)")]
    private static partial Regex LocationType();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!Java.IsCompileError(error)) return null;
        if (Symbol().Match(error.Message ?? "") is not { Success: true } symbol) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var kind = symbol.Groups["kind"].Value;
        var name = symbol.Groups["name"].Value;

        // A class the import table knows is a missing import, not a misspelling.
        if (kind == "class" && JavaTypes.Packages.ContainsKey(name)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        IEnumerable<string> candidates;
        string where;

        var location = LocationType().Match(symbol.Groups["location"].Value);
        var type = location.Success ? location.Groups["type"].Value : null;

        if (type is not null && !DeclaresType(masked, type) && JavaTypes.Fqn(type, source.Lines) is { } fqn)
        {
            candidates = JavaTypes.Members(fqn, methods: kind == "method");
            where = $"on {type}";
        }
        else
        {
            candidates = CodeText.Identifiers(masked).Where(word => !JavaTypes.Keywords.Contains(word));
            if (kind == "class") candidates = candidates.Concat(JavaTypes.Lang).Concat(JavaTypes.Packages.Keys);
            where = "in this file";
        }

        if (CodeText.Nearest(name, candidates.Where(c => c != name)) is not { } right) return null;

        var caret = CodeText.Caret(context.Output, error);
        int? column = caret is { } found && found.Echo == line ? found.Column : null;

        if (CodeText.ReplaceWord(line, name, right, Syntax.CLike, column) is not { } corrected) return null;

        return LocalFix.ReplaceLine(
            Id,
            $"Change {name} to {right}",
            $"javac could not find the {kind} `{name}`, and does not suggest names. `{right}` is the only name {where} " +
            "within a letter or two of it.",
            source.Path, number, corrected);
    }

    private static bool DeclaresType(IReadOnlyList<string> masked, string type) =>
        masked.Any(text => Regex.IsMatch(text, $@"\b(?:class|interface|enum|record)\s+{Regex.Escape(type)}\b"));
}

/// <summary><c>';' expected</c>, placed exactly where javac's caret points.</summary>
public sealed class JavaMissingSemicolon : ILocalFixRule
{
    public string Id => "java-missing-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!Java.IsCompileError(error) || error.Message != "';' expected") return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        string corrected;

        if (CodeText.Caret(context.Output, error) is { } caret && caret.Echo == line && caret.Column <= line.Length)
        {
            corrected = line[..caret.Column] + ";" + line[caret.Column..];
        }
        else
        {
            var (code, tail) = CodeText.SplitComment(line, Syntax.CLike);
            if (code.Length == 0 || code.EndsWith(';')) return null;

            corrected = code + ";" + tail;
        }

        return LocalFix.ReplaceLine(
            Id,
            "Add the missing semicolon",
            $"javac expected a `;` at the end of the statement on line {number}.",
            source.Path, number, corrected);
    }
}

/// <summary><c>unreported exception InterruptedException; must be caught or declared to be thrown</c></summary>
/// <remarks>
/// Java requires a checked exception to be caught or declared, and declaring it on the enclosing
/// method is the one-place change. The walk outward stops at anything that cannot declare an
/// exception - a lambda, an anonymous class, an initialiser - rather than declaring it somewhere it
/// would not help.
/// </remarks>
public sealed partial class JavaUnreportedException : ILocalFixRule
{
    public string Id => "java-unreported-exception";

    [GeneratedRegex(@"^unreported exception (?<type>[\w.$]+); must be caught or declared to be thrown$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?:\}\s*)?(?:if|else|for|while|do|switch|try|catch|finally|synchronized)\b")]
    private static partial Regex ControlBlock();

    [GeneratedRegex(@"\b(?:class|interface|enum|record)\b|->\s*$|\bnew\s+[\w.$<>]+\s*\(.*\)\s*$|^static$|^default$")]
    private static partial Regex CannotDeclare();

    [GeneratedRegex(@"(?<name>[A-Za-z_$][\w$]*)\s*\((?:[^()]|\([^()]*\))*\)\s*(?:throws\s+[\w.$,\s]+?)?\s*$")]
    private static partial Regex MethodHeader();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!Java.IsCompileError(error)) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unmatchedClosers = 0;

        for (var k = number - 2; k >= 0; k--)
        {
            var text = masked[k];

            for (var c = text.Length - 1; c >= 0; c--)
            {
                if (text[c] == '}')
                {
                    unmatchedClosers++;
                    continue;
                }

                if (text[c] != '{') continue;

                if (unmatchedClosers > 0)
                {
                    unmatchedClosers--;
                    continue;
                }

                var headerLine = k;
                var header = text[..c].Trim();

                if (header.Length == 0 && k > 0)
                {
                    headerLine = k - 1;
                    header = masked[k - 1].Trim();
                }

                if (CannotDeclare().IsMatch(header)) return null;
                if (ControlBlock().IsMatch(header)) continue;
                if (MethodHeader().Match(header) is not { Success: true } method) return null;

                return Declare(source, masked, headerLine, headerLine == k ? c : -1,
                    method.Groups["name"].Value, Together(message.Groups["type"].Value, masked[number - 1]), number);
            }
        }

        return null;
    }

    /// <summary>
    /// The exception javac reported, and the others the same call is known to throw. javac reports one unreported exception
    /// per call at a time, so declaring only <c>InterruptedException</c> for <c>future.get()</c> just uncovers
    /// <c>ExecutionException</c> on the same line - a fix that looks like it broke something.
    /// </summary>
    private static IReadOnlyList<string> Together(string type, string maskedLine)
    {
        (string Call, string[] Types)[] companions =
        [
            (@"\.get\s*\(", ["InterruptedException", "ExecutionException"]),
            (@"\.invoke\s*\(", ["IllegalAccessException", "InvocationTargetException"]),
            (@"\.newInstance\s*\(", ["InstantiationException", "IllegalAccessException", "InvocationTargetException", "NoSuchMethodException"]),
        ];

        foreach (var (call, types) in companions)
            if (types.Contains(type) && Regex.IsMatch(maskedLine, call)) return types;

        return [type];
    }

    private LocalFix? Declare(
        SourceFile source, IReadOnlyList<string> masked, int headerLine, int braceColumn,
        string method, IReadOnlyList<string> types, int errorLine)
    {
        var type = string.Join(", ", types);
        var line = source.Lines[headerLine];
        var code = masked[headerLine];
        var end = braceColumn >= 0 ? braceColumn : code.TrimEnd().Length;

        if (end <= 0) return null;

        var closeParen = code.LastIndexOf(')', end - 1);
        if (closeParen < 0) return null;

        // The name as javac printed it compiles only if it is visible here; otherwise the package
        // goes in front, which keeps this to one line rather than adding an import as well.
        var declared = string.Join(", ", types.Select(one =>
            !one.Contains('.') && !JavaTypes.Lang.Contains(one) && !JavaTypes.IsImported(one, source.Lines) &&
            JavaTypes.Packages.TryGetValue(one, out var package)
                ? $"{package}.{one}"
                : one));

        var between = code[(closeParen + 1)..end];
        string corrected;

        if (between.Contains("throws", StringComparison.Ordinal))
        {
            var listEnd = closeParen + 1 + between.TrimEnd().Length;
            corrected = line[..listEnd] + ", " + declared + line[listEnd..];
        }
        else
        {
            corrected = line[..(closeParen + 1)] + " throws " + declared + line[(closeParen + 1)..];
        }

        return LocalFix.ReplaceLine(
            Id,
            $"Declare that {method} throws {type}",
            $"Line {errorLine} can throw {type}, which Java requires to be caught or declared. Declaring it on {method} " +
            "passes it on to whatever calls it - for main, that means the program stops with the exception, as a crash " +
            $"would. To handle it where it happens instead, wrap line {errorLine} in try/catch.",
            source.Path, headerLine + 1, corrected);
    }
}

/// <summary><c>non-static method total() cannot be referenced from a static context</c></summary>
/// <remarks>
/// Making the member static is right exactly when it uses nothing belonging to an object - and that
/// is what the compile check establishes, because a static method that touches instance state does
/// not compile.
/// </remarks>
public sealed partial class JavaNonStaticMember : ILocalFixRule
{
    public string Id => "java-non-static-member";

    [GeneratedRegex(@"^non-static (?<kind>method|variable) (?<name>[A-Za-z_$][\w$]*)(?:\(.*\))? cannot be referenced from a static context$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\b(?:static|abstract)\b")]
    private static partial Regex AlreadyDecided();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!Java.IsCompileError(error)) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var kind = message.Groups["kind"].Value;
        var name = message.Groups["name"].Value;
        var word = Regex.Escape(name);

        var pattern = kind == "method"
            ? new Regex($@"^(?<lead>\s*(?:@\w+(?:\([^)]*\))?\s+)*(?:(?:public|protected|private|final|synchronized|native|strictfp)\s+)*)(?<type>(?:<[^>]+>\s*)?[\w$.]+(?:<[^()]*>)?(?:\[\])*)\s+{word}\s*\(")
            : new Regex($@"^(?<lead>\s*(?:(?:public|protected|private|final|transient|volatile)\s+)*)(?<type>[\w$.]+(?:<[^()=;]*>)?(?:\[\])*)\s+{word}\s*(?:=|;)");

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var declarations = masked
            .Select((text, index) => (Text: text, Index: index, Match: pattern.Match(text)))
            .Where(d => d.Match.Success)
            .Where(d => d.Match.Groups["type"].Value is not ("return" or "new" or "throw" or "else"))
            .Where(d => kind != "method" || !d.Text.TrimEnd().EndsWith(';'))
            .ToList();

        if (declarations.Count != 1 || AlreadyDecided().IsMatch(declarations[0].Text)) return null;

        var (_, index, match) = declarations[0];
        var line = source.Lines[index];
        var lead = match.Groups["lead"].Length;

        return LocalFix.ReplaceLine(
            Id,
            $"Make {name} static",
            $"main is static, so there is no object for it to use `{name}` on. `{name}` uses nothing that belongs to an " +
            "object - it still compiles once static - so that is the direct fix. If it should keep per-object state, " +
            "create an instance with new and use it on that instead.",
            source.Path, index + 1, line[..lead] + "static " + line[lead..]);
    }
}

/// <summary><c>class Main is public, should be declared in a file named Main.java</c></summary>
public sealed partial class JavaPublicClassName : ILocalFixRule
{
    public string Id => "java-public-class-name";

    [GeneratedRegex(@"^class (?<name>[A-Za-z_$][\w$]*) is public, should be declared in a file named \k<name>\.java$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*$")]
    private static partial Regex Identifier();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!Java.IsCompileError(error)) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var name = message.Groups["name"].Value;
        var fileName = Path.GetFileNameWithoutExtension(source.Path);

        if (!Identifier().IsMatch(fileName) || JavaTypes.Keywords.Contains(fileName)) return null;

        // Named anywhere else - a constructor, a static call - and renaming one line breaks the rest.
        var mentions = CodeText.MaskAll(source.Lines, Syntax.CLike)
            .Sum(text => Regex.Matches(text, $@"(?<![\w$]){Regex.Escape(name)}(?![\w$])").Count);

        if (mentions != 1 || source.Line(number) is not { } line) return null;
        if (CodeText.ReplaceWord(line, name, fileName, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(
            Id,
            $"Rename class {name} to {fileName}",
            $"A public class has to live in a file with its own name. The file is {fileName}.java, so the class " +
            $"becomes {fileName}; renaming the file to {name}.java would work just as well.",
            source.Path, number, corrected);
    }
}

/// <summary>
/// <c>ArrayIndexOutOfBoundsException: Index 3 out of bounds for length 3</c> from a loop written with <c>&lt;=</c>.
/// </summary>
/// <remarks>
/// Only when the index equals the length - one past the last valid index, which is exactly what
/// <c>i &lt;= values.length</c> produces - and only when the loop that drives the failing index is
/// right there with its bound being that length. Any other out-of-bounds index is a different bug.
/// </remarks>
public sealed partial class JavaOffByOneLoop : ILocalFixRule
{
    public string Id => "java-off-by-one-loop";

    [GeneratedRegex(@"(?:Index|index)\s*:?\s*(?<index>\d+)\D+?(?:length|Size|size)\s*:?\s*(?<length>\d+)")]
    private static partial Regex Bounds();

    [GeneratedRegex(@"\[\s*(?<var>[A-Za-z_$][\w$]*)\s*\]|\.(?:get|charAt|set|remove)\(\s*(?<var>[A-Za-z_$][\w$]*)\s*[,)]")]
    private static partial Regex IndexUse();

    [GeneratedRegex(@"\bfor\s*\(\s*(?:int\s+|long\s+|var\s+)?(?<var>[A-Za-z_$][\w$]*)\s*=[^;]*;\s*\k<var>\s*(?<op><=)\s*(?<bound>[^;]+?)\s*;")]
    private static partial Regex Loop();

    [GeneratedRegex(@"(?:\.length|\.size\(\)|\.length\(\))$")]
    private static partial Regex LengthBound();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "java") return null;
        if (error.ExceptionType is not ("java.lang.ArrayIndexOutOfBoundsException" or
            "java.lang.StringIndexOutOfBoundsException" or "java.lang.IndexOutOfBoundsException")) return null;
        if (Bounds().Match(error.Message ?? "") is not { Success: true } bounds) return null;
        if (bounds.Groups["index"].Value != bounds.Groups["length"].Value) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (number < 1 || number > masked.Count) return null;

        var indexes = IndexUse().Matches(masked[number - 1]).Select(m => m.Groups["var"].Value).ToHashSet(StringComparer.Ordinal);
        if (indexes.Count == 0) return null;

        for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
        {
            if (Loop().Match(masked[k]) is not { Success: true } loop || !indexes.Contains(loop.Groups["var"].Value)) continue;

            var bound = loop.Groups["bound"].Value.Trim();
            if (!LengthBound().IsMatch(bound) && bound != bounds.Groups["length"].Value) return null;

            var op = loop.Groups["op"];
            var line = source.Lines[k];
            var variable = loop.Groups["var"].Value;

            return LocalFix.ReplaceLine(
                Id,
                "Stop the loop at the last element: < instead of <=",
                $"Index {bounds.Groups["index"].Value} is one past the end of something with {bounds.Groups["length"].Value} " +
                $"elements - the valid indexes stop at {int.Parse(bounds.Groups["length"].Value) - 1}. The loop on line {k + 1} " +
                $"keeps going while {variable} <= {bound}, which reaches the length itself; < stops one before it.",
                source.Path, k + 1, line[..op.Index] + "<" + line[(op.Index + 2)..]);
        }

        return null;
    }
}

/// <summary><c>incompatible types: String cannot be converted to int</c>, and the reverse.</summary>
public sealed partial class JavaStringConversion : ILocalFixRule
{
    public string Id => "java-string-conversion";

    [GeneratedRegex(@"^incompatible types: (?<from>String|int|long|double|float|boolean|char) cannot be converted to (?<to>String|int|long|double|float|boolean|short|byte)$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, string> Parsers = new(StringComparer.Ordinal)
    {
        ["int"] = "Integer.parseInt",
        ["long"] = "Long.parseLong",
        ["double"] = "Double.parseDouble",
        ["float"] = "Float.parseFloat",
        ["boolean"] = "Boolean.parseBoolean",
        ["short"] = "Short.parseShort",
        ["byte"] = "Byte.parseByte",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!Java.IsCompileError(error)) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var from = message.Groups["from"].Value;
        var to = message.Groups["to"].Value;

        string method;

        if (from == "String" && Parsers.TryGetValue(to, out var parser)) method = parser;
        else if (to == "String" && from != "String") method = "String.valueOf";
        else return null;

        if (CodeText.Caret(context.Output, error) is not { } caret || caret.Echo != line) return null;

        var start = caret.Column;
        var before = line[..start].TrimEnd();

        var assigned = before.EndsWith('=') && !before.EndsWith("==") && (before.Length < 2 || !"!<>+-*/%&|^".Contains(before[^2]));
        if (!assigned && !before.EndsWith("return", StringComparison.Ordinal)) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var depth = 0;
        var end = -1;

        for (var i = start; i < masked.Length; i++)
        {
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (masked[i] == ';' && depth == 0) { end = i; break; }
        }

        if (end < 0) return null;

        var expression = line[start..end].TrimEnd();
        if (expression.Length == 0) return null;

        var explanation = method == "String.valueOf"
            ? $"A {from} is not a String in Java, and is not turned into one automatically here; String.valueOf makes its text form."
            : $"A String holding digits is still text to Java. {method} reads the {to} out of it - and throws " +
              "NumberFormatException when the program runs if the text is not one.";

        return LocalFix.ReplaceLine(
            Id,
            $"Convert it with {method}",
            explanation,
            source.Path, number, line[..start] + $"{method}({expression})" + line[(start + expression.Length)..]);
    }
}
