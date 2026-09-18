using System.Text.RegularExpressions;
using System.Text;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing.Parsers;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// A misspelt name: an undeclared identifier, a struct member that does not exist, or a function the
/// linker cannot find.
/// </summary>
/// <remarks>
/// MSVC never suggests a name. For a misspelt function in C it does not even report an error at the
/// call - only the warning <c>C4013 'prinft' undefined</c> and then, from the linker,
/// <c>LNK2019: unresolved external symbol prinft</c>. The warning is where the line number is.
/// </remarks>
public sealed partial class CNearestName : ILocalFixRule
{
    public string Id => "c-nearest-name";

    [GeneratedRegex(@"^'(?<name>\w+)': (?:undeclared identifier|identifier not found)$")]
    private static partial Regex Undeclared();

    [GeneratedRegex(@"^'(?<member>\w+)': is not a member of '(?<type>\w+)'$")]
    private static partial Regex NotAMember();

    [GeneratedRegex(@"^unresolved external symbol (?<name>[A-Za-z_]\w*) referenced in function")]
    private static partial Regex Unresolved();

    [GeneratedRegex(@"(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex Called();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "msvc") return null;

        var message = error.Message ?? "";

        if (error.ErrorCode is "C2065" or "C3861" && Undeclared().Match(message) is { Success: true } undeclared)
        {
            var name = undeclared.Groups["name"].Value;
            if (CStandardLibrary.CHeaderOf(name) is not null) return null;

            if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

            var candidates = CodeText.Identifiers(CodeText.MaskAll(source.Lines, Syntax.CLike))
                .Where(word => !CStandardLibrary.Keywords.Contains(word))
                .Concat(CStandardLibrary.CNames);

            return Rename(source, number, name, candidates, "in this file or the C library");
        }

        if (error.ErrorCode == "C2039" && NotAMember().Match(message) is { Success: true } member &&
            member.Groups["type"].Value != "std")
        {
            if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var type = member.Groups["type"].Value;

            if (MembersOf(masked, type) is not { Count: > 0 } members) return null;

            var wrong = member.Groups["member"].Value;
            if (CodeText.Nearest(wrong, members) is not { } right || source.Line(number) is not { } line) return null;

            var access = new Regex($@"(?:\.|->)\s*(?<name>{Regex.Escape(wrong)})\b");
            var hits = access.Matches(CodeText.Mask(line, Syntax.CLike));
            if (hits.Count != 1) return null;

            var at = hits[0].Groups["name"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Change {wrong} to {right}",
                $"`{type}` has no member `{wrong}`. `{right}` is the only member it does have within a letter or two of it.",
                source.Path, number, line[..at] + right + line[(at + wrong.Length)..]);
        }

        if (error.ErrorCode == "LNK2019" && Unresolved().Match(message) is { Success: true } unresolved)
        {
            var name = unresolved.Groups["name"].Value;

            var warning = MsvcParser.ParseWarnings(context.Output).FirstOrDefault(w =>
                w.ErrorCode == "C4013" && (w.Message ?? "").StartsWith($"'{name}' undefined", StringComparison.Ordinal));

            if (warning?.Frames.FirstOrDefault() is not { Line: { } number } frame || context.Read(frame.File) is not { } source)
                return null;

            var calledHere = CodeText.MaskAll(source.Lines, Syntax.CLike)
                .SelectMany(text => Called().Matches(text).Select(m => m.Groups["name"].Value))
                .Where(word => !CStandardLibrary.Keywords.Contains(word));

            return Rename(source, number, name, calledHere.Concat(CStandardLibrary.CNames), "among this file's functions and the C library");
        }

        return null;
    }

    private LocalFix? Rename(SourceFile source, int number, string wrong, IEnumerable<string> candidates, string where)
    {
        if (CodeText.Nearest(wrong, candidates.Where(c => c != wrong)) is not { } right) return null;
        if (source.Line(number) is not { } line) return null;
        if (CodeText.ReplaceWord(line, wrong, right, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong} to {right}",
            $"Nothing called `{wrong}` exists, and MSVC does not suggest names. `{right}` is the only name {where} " +
            "within a letter or two of it.",
            source.Path, number, corrected);
    }

    /// <summary>The words declared inside <c>struct type { ... }</c>, or <c>typedef struct { ... } type;</c>.</summary>
    private static List<string>? MembersOf(IReadOnlyList<string> masked, string type)
    {
        var name = Regex.Escape(type);

        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.IsMatch(masked[i], $@"\b(?:struct|union|class)\s+{name}\b(?!\s*[;*\w])"))
                return BlockWords(masked, i, forward: true);

            if (Regex.IsMatch(masked[i], $@"\}}\s*{name}\s*;"))
                return BlockWords(masked, i, forward: false);
        }

        return null;
    }

    private static List<string>? BlockWords(IReadOnlyList<string> masked, int line, bool forward)
    {
        var text = new List<string>();
        var depth = 0;
        var started = false;

        if (forward)
        {
            for (var i = line; i < masked.Count; i++)
            {
                foreach (var c in masked[i])
                {
                    if (c == '{') { depth++; started = true; continue; }
                    if (c == '}') { depth--; if (started && depth == 0) return Words(text); continue; }
                    if (started) text.Add(c.ToString());
                }

                if (started) text.Add(" ");
            }
        }
        else
        {
            for (var i = line; i >= 0; i--)
            {
                var row = masked[i];

                for (var k = (i == line ? row.IndexOf('}') : row.Length - 1); k >= 0; k--)
                {
                    var c = row[k];
                    if (c == '}') { depth++; started = true; continue; }
                    if (c == '{') { depth--; if (started && depth == 0) return Words(text, reversed: true); continue; }
                    if (started) text.Add(c.ToString());
                }

                if (started) text.Add(" ");
            }
        }

        return null;
    }

    private static List<string> Words(List<string> characters, bool reversed = false)
    {
        if (reversed) characters.Reverse();

        return CodeText.Identifiers([string.Concat(characters)])
            .Where(word => !CStandardLibrary.Keywords.Contains(word))
            .ToList();
    }
}

/// <summary><c>C1083: Cannot open include file: 'stdoi.h'</c>, one letter from a standard header.</summary>
/// <remarks>
/// Only for <c>&lt;...&gt;</c> includes. A quoted include names the project's own header, and the
/// nearest standard header to a missing <c>"time.hpp"</c> is not what anybody meant.
/// </remarks>
public sealed partial class CHeaderTypo : ILocalFixRule
{
    public string Id => "c-header-typo";

    private static readonly HashSet<string> PlatformHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "pthread.h", "unistd.h", "semaphore.h", "sched.h", "fcntl.h", "dirent.h", "termios.h", "poll.h", "dlfcn.h", "strings.h",
        "getopt.h", "libgen.h", "pwd.h", "grp.h", "syslog.h", "spawn.h", "mqueue.h", "netdb.h", "ifaddrs.h", "regex.h", "glob.h",
        "sys/types.h", "sys/stat.h", "sys/wait.h", "sys/socket.h", "sys/mman.h", "sys/time.h", "sys/select.h", "sys/ipc.h",
        "sys/shm.h", "sys/msg.h", "sys/sem.h", "sys/resource.h", "sys/utsname.h", "sys/ioctl.h", "sys/un.h", "sys/epoll.h",
        "arpa/inet.h", "netinet/in.h", "netinet/tcp.h", "windows.h", "winsock2.h", "ws2tcpip.h", "conio.h", "io.h", "process.h",
        "omp.h", "mpi.h",
    };

    [GeneratedRegex(@"^Cannot open include file: '(?<header>[^']+)': No such file or directory$")]
    private static partial Regex Message();

    /// <summary>gcc: <c>stdoi.h: No such file or directory</c>; clang: <c>'stdoi.h' file not found</c>.</summary>
    [GeneratedRegex(@"^'?(?<header>[^':]+)'?(?: file not found|: No such file or directory)$")]
    private static partial Regex GnuMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        var message = error.LanguageId switch
        {
            "msvc" when error.ErrorCode == "C1083" => Message().Match(error.Message ?? ""),
            "gcc" => GnuMessage().Match(error.Message ?? ""),
            _ => null,
        };

        if (message is not { Success: true }) return null;

        var header = message.Groups["header"].Value;
        if (CStandardLibrary.Headers.Contains(header)) return null;

        // A POSIX or platform header is missing because the compiler does not have it - MSVC has no pthread.h - not because it
        // is misspelt, and the nearest standard name to one is a different API altogether.
        if (PlatformHeaders.Contains(header)) return null;

        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var bracketed = $"<{header}>";
        var at = line.IndexOf(bracketed, StringComparison.Ordinal);
        if (at < 0 || line.IndexOf(bracketed, at + 1, StringComparison.Ordinal) >= 0) return null;

        if (CodeText.Nearest(header, CStandardLibrary.Headers) is not { } right) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change <{header}> to <{right}>",
            $"There is no standard header <{header}>. <{right}> is the only one within a letter or two of it.",
            source.Path, number, line[..(at + 1)] + right + line[(at + 1 + header.Length)..]);
    }
}

/// <summary>
/// A standard name used without its header: <c>C2065 'bool'</c>, <c>C3861</c>, <c>C2039 'cout' is not
/// a member of 'std'</c>, or the warning <c>C4013 'malloc' undefined</c>.
/// </summary>
public sealed partial class CMissingStandardHeader : ILocalFixRule
{
    public string Id => "c-missing-standard-header";

    [GeneratedRegex(@"^'(?<name>\w+)': (?:undeclared identifier|identifier not found)$")]
    private static partial Regex Undeclared();

    [GeneratedRegex(@"^'(?<name>\w+)' undefined; assuming extern returning int$")]
    private static partial Regex Undefined();

    [GeneratedRegex(@"^'(?<name>\w+)': is not a member of 'std'$")]
    private static partial Regex NotInStd();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "msvc" || Needed(error) is not { } primary) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var c = CCode.IsC(source);

        string? HeaderFor((string Name, bool Std) need) =>
            need.Std ? (c ? null : CStandardLibrary.CppHeaderOf(need.Name)) : CStandardLibrary.CHeaderOf(need.Name);

        var included = source.Lines
            .Select(line => CCode.IncludeLine().Match(line))
            .Where(m => m.Success)
            .Select(m => m.Groups["header"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Already included: something else - a macro, a missing define - is keeping the name hidden.
        if (HeaderFor(primary) is not { } primaryHeader || included.Contains(primaryHeader)) return null;

        var headers = context.AllErrors
            .Concat(MsvcParser.ParseWarnings(context.Output))
            .Where(e => CCode.SameFile(context, e, source))
            .Select(Needed)
            .OfType<(string Name, bool Std)>()
            .Select(HeaderFor)
            .OfType<string>()
            .Where(header => !included.Contains(header))
            .Append(primaryHeader)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        var lastInclude = -1;
        for (var i = 0; i < source.Count; i++)
            if (CCode.IncludeLine().IsMatch(source.Lines[i])) lastInclude = i;

        var isWarning = error.ExceptionType == "compile warning";
        var name = primary.Name;

        var explanation = isWarning && CStandardLibrary.ReturnsPointer.Contains(name)
            ? $"The compiler warned `'{name}' undefined; assuming extern returning int`, and that warning is the crash. " +
              $"Without <{primaryHeader}>, C assumes {name} returns a 32-bit int, so the 64-bit pointer it really returns " +
              "is cut in half - and the program then uses a broken pointer and is killed without a message."
            : $"`{name}` is declared in <{primaryHeader}>, which this file does not include.";

        return LocalFix.Insert(
            Id,
            headers.Count == 1 ? $"Add #include <{headers[0]}>" : $"Add {headers.Count} missing #includes",
            explanation,
            source.Path,
            lastInclude + 2,
            [.. headers.Select(header => $"#include <{header}>")]) with
        {
            ResolvesWarning = isWarning ? $"'{name}' undefined" : null,
        };
    }

    private static (string Name, bool Std)? Needed(ParsedError error)
    {
        var message = error.Message ?? "";

        return error.ErrorCode switch
        {
            "C2065" or "C3861" when Undeclared().Match(message) is { Success: true } m => (m.Groups["name"].Value, false),
            "C4013" when Undefined().Match(message) is { Success: true } m => (m.Groups["name"].Value, false),
            "C2039" when NotInStd().Match(message) is { Success: true } m => (m.Groups["name"].Value, true),
            _ => null,
        };
    }
}

/// <summary><c>#include &lt;iostream&gt;</c> in a C file.</summary>
public sealed partial class CIostreamInC : ILocalFixRule
{
    public string Id => "c-iostream-in-c";

    [GeneratedRegex(@"^iostream: No such file or directory$|^'iostream' file not found$")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        var recognised =
            (CCode.IsMsvc(error, "C1189") && (error.Message ?? "").Contains("expected C++ compiler", StringComparison.Ordinal)) ||
            (CCode.IsMsvc(error, "C1083") && (error.Message ?? "").StartsWith("Cannot open include file: 'iostream'", StringComparison.Ordinal)) ||
            CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised) return null;

        // MSVC reports this one from inside its own C++ headers, so the file that included them is
        // not in the error. It is the one C file in the project that includes <iostream>.
        var source = context.Read(context.Frame?.File) is { } framed && CCode.IsC(framed)
            ? framed
            : OnlyCFileIncludingIostream(context.SourceRoot);

        if (source is null || !CCode.IsC(source)) return null;

        var index = source.Lines.ToList().FindIndex(l => CCode.IncludeLine().Match(l) is { Success: true } m && m.Groups["header"].Value == "iostream");
        if (index < 0) return null;

        const string explanation = "<iostream> is C++ - a C compiler cannot use it. In C, `printf` and the rest of console " +
                                   "input and output come from <stdio.h>.";

        return CCode.Includes(source, "stdio.h")
            ? CCode.RemoveLine(Id, "Remove #include <iostream>", explanation, source.Path, index + 1)
            : LocalFix.ReplaceLine(Id, "Include <stdio.h> instead of <iostream>", explanation, source.Path, index + 1, "#include <stdio.h>");
    }

    private static SourceFile? OnlyCFileIncludingIostream(string? root)
    {
        if (root is null || !Directory.Exists(root)) return null;

        var matches = Directory.EnumerateFiles(root, "*.c", SearchOption.TopDirectoryOnly)
            .Take(200)
            .Select(SourceFile.Read)
            .OfType<SourceFile>()
            .Where(file => CCode.Includes(file, "iostream"))
            .Take(2)
            .ToList();

        return matches is [var only] ? only : null;
    }
}

/// <summary><c>cout &lt;&lt; "hi";</c> in a C file, which only has printf.</summary>
public sealed partial class CCoutInC : ILocalFixRule
{
    public string Id => "c-cout-in-c";

    [GeneratedRegex(@"^'cout': undeclared identifier$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^'cout' undeclared")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?:std::)?cout\s*<<(?<parts>.+);(?<tail>\s*(?://.*)?)$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = CCode.MsvcMessage(error, "C2065", MsvcMessage()) is not null || CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || CCode.Locate(context) is not { } at || !CCode.IsC(at.Source)) return null;

        var (source, number, line) = at;
        if (Statement().Match(line) is not { Success: true } statement) return null;

        var parts = statement.Groups["parts"];
        var masked = CodeText.Mask(line, Syntax.CLike).Substring(parts.Index, parts.Length);
        var text = new System.Text.StringBuilder();
        var start = 0;

        foreach (var piece in masked.Split("<<").Select(p => (Masked: p, Start: 0)))
        {
            var raw = line.Substring(parts.Index + start, piece.Masked.Length).Trim();
            start += piece.Masked.Length + 2;

            if (raw is "endl" or "std::endl" or "'\\n'") text.Append("\\n");
            else if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"') text.Append(raw[1..^1].Replace("%", "%%"));
            else return null;
        }

        return LocalFix.ReplaceLine(
            Id, "Print it with printf",
            "`cout` is C++. A C program prints with `printf` from <stdio.h>.",
            source.Path, number, $"{statement.Groups["lead"].Value}printf(\"{text}\");{statement.Groups["tail"].Value}");
    }
}

/// <summary>No <c>main</c>: MSVC's <c>LNK1561 entry point must be defined</c>, MinGW's <c>undefined reference to 'WinMain'</c>.</summary>
public sealed partial class CMainName : ILocalFixRule
{
    public string Id => "c-main-name";

    [GeneratedRegex(@"^\s*(?:static\s+)?(?:int|void)\s+(?<name>[A-Za-z_]\w*)\s*\([^;{)]*\)\s*\{?\s*$")]
    private static partial Regex Function();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = error.Message ?? "";

        var noMain = error.LanguageId switch
        {
            "gcc" => message is "undefined reference to 'WinMain'" or "undefined reference to 'main'",
            "msvc" => error.ErrorCode == "LNK1561" ||
                      error.ErrorCode == "LNK2019" && Regex.IsMatch(message, @"^unresolved external symbol (?:main|WinMain) "),
            _ => false,
        };

        if (!noMain || context.SourceRoot is not { } root || !Directory.Exists(root)) return null;

        var functions = new List<(SourceFile Source, int Index, Group Name)>();

        foreach (var path in Directory.EnumerateFiles(root).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".c" or ".cpp" or ".cc" or ".cxx").Take(200))
        {
            if (SourceFile.Read(path) is not { } source) continue;

            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var depths = Brackets.BraceDepths(masked);

            for (var i = 0; i < masked.Count; i++)
            {
                if (depths[i] != 0 || Function().Match(masked[i]) is not { Success: true } function) continue;

                // A real main means the missing entry point is something else - a project setting, not a name.
                if (function.Groups["name"].Value == "main") return null;

                functions.Add((source, i, function.Groups["name"]));
            }
        }

        if (CodeText.Nearest("main", functions.Select(f => f.Name.Value).Distinct()) is not { } wrong) return null;
        if (functions.Where(f => f.Name.Value == wrong).ToList() is not [var (file, index, name)]) return null;

        var line = file.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Rename {wrong} to main",
            $"A C program starts at a function called exactly `main`, in lower case, and there is none - `{wrong}` is a letter away - so the " +
            "linker had nowhere to start the program" +
            (error.LanguageId == "gcc" ? ". MinGW then looked for `WinMain`, the entry point of a Windows GUI program, and named that instead." : "."),
            file.Path, index + 1, line[..name.Index] + "main" + line[(name.Index + name.Length)..]);
    }
}

/// <summary>A function called above its definition: <c>C2371 redefinition; different basic types</c> / <c>conflicting types</c>.</summary>
/// <remarks>gcc 14 and later stop earlier, at <c>implicit declaration of function</c> on the call itself.</remarks>
public sealed partial class CFunctionPrototype : ILocalFixRule
{
    public string Id => "c-function-prototype";

    [GeneratedRegex(@"^'(?<name>\w+)': redefinition; different basic types$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^conflicting types for '(?<name>\w+)'")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^implicit declaration of function '(?<name>\w+)'")]
    private static partial Regex GccImplicit();

    /// <summary>C++ has no implicit declarations: g++ says the name was never declared, MSVC <c>C3861 identifier not found</c>.</summary>
    [GeneratedRegex(@"^'(?<name>\w+)' was not declared in this scope")]
    private static partial Regex GccUndeclared();

    [GeneratedRegex(@"^'(?<name>\w+)': identifier not found$")]
    private static partial Regex MsvcNotFound();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var undeclared = CCode.GccMessage(error, GccUndeclared()) ?? CCode.MsvcMessage(error, "C3861", MsvcNotFound());
        var message = CCode.MsvcMessage(error, "C2371", MsvcMessage()) ?? CCode.GccMessage(error, GccMessage()) ??
                      CCode.GccMessage(error, GccImplicit()) ?? undeclared;

        if (message is null || context.Read(context.Frame?.File) is not { } source || !CCode.IsNative(source)) return null;

        var name = message.Groups["name"].Value;
        var escaped = Regex.Escape(name);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var depths = Brackets.BraceDepths(masked);

        var signature = new Regex($@"^\s*(?<sig>(?:(?:static|inline)\s+)*[A-Za-z_][\w\s]*?[\s*]{escaped}\s*\([^()]*\))\s*\{{?\s*$");
        var definition = -1;
        Match? found = null;

        for (var i = 0; i < masked.Count && definition < 0; i++)
        {
            if (depths[i] != 0 || signature.Match(masked[i]) is not { Success: true } m) continue;

            var opensHere = masked[i].TrimEnd().EndsWith('{');
            var opensNext = i + 1 < masked.Count && masked[i + 1].Trim().StartsWith('{');
            if (!opensHere && !opensNext) continue;

            (definition, found) = (i, m);
        }

        if (found is null) return null;

        var call = new Regex($@"(?<![\w.>]){escaped}\s*\(");
        var use = Enumerable.Range(0, definition).FirstOrDefault(i => depths[i] > 0 && call.IsMatch(masked[i]), -1);
        if (use < 0) return null;

        var (header, _) = CCode.EnclosingFunction(masked, use);
        var group = found.Groups["sig"];
        var prototype = source.Lines[definition].Substring(group.Index, group.Length).Trim() + ";";

        return LocalFix.Insert(
            Id, $"Declare {name} before it is used",
            undeclared is not null
                ? $"`{name}` is called on line {use + 1} but only defined on line {definition + 1}. The compiler reads from the top, so at " +
                  $"the call nothing called `{name}` exists yet. A declaration above the first use tells it about `{name}` in time."
                : $"`{name}` is called on line {use + 1} but only defined on line {definition + 1}. C reads from the top, so at the call " +
                  $"it had to guess what `{name}` returns - and the real definition then contradicts the guess. A declaration above " +
                  "the first use tells it the truth in time.",
            source.Path, header + 1, [prototype]);
    }
}

/// <summary>
/// A fix gcc or clang worked out itself and printed as <c>fix-it:"app.c":{4:20-4:27}:"average"</c>.
/// </summary>
/// <remarks>
/// gcc and clang already know a great many answers - the header that declares <c>bool</c>, the
/// member a misspelt one was nearest to, the function a misspelt call meant - and print them for a
/// person to read. FixFinder builds with <c>-fdiagnostics-parseable-fixits</c>, which prints the same
/// answers a second time in a form a program can apply exactly: a file, a range and the text to put
/// there. Nothing here is inferred. The edit is the compiler's, and it is still compiled before it
/// is offered.
/// <para>
/// <b>Columns in a fix-it are bytes, not characters</b>, so a line with anything outside ASCII in it
/// is converted rather than indexed directly - indexing a UTF-16 string by a UTF-8 byte count would
/// put the change in the wrong place on exactly the lines where nobody would spot it.
/// </para>
/// </remarks>
public sealed partial class CompilerFixIt : ILocalFixRule
{
    public string Id => "c-compiler-fix-it";

    [GeneratedRegex(@"^fix-it:""(?<file>(?:[^""\\]|\\.)*)"":\{(?<l1>\d+):(?<c1>\d+)-(?<l2>\d+):(?<c2>\d+)\}:""(?<text>(?:[^""\\]|\\.)*)""\s*$")]
    private static partial Regex FixItLine();

    /// <summary>The next error or warning, which is where this diagnostic's fix-its stop. Notes belong to it.</summary>
    [GeneratedRegex(@"^(?:[A-Za-z]:)?[^\s:][^:]*?:\d+:\d+:\s*(?:fatal error|error|warning):")]
    private static partial Regex NextDiagnostic();

    [GeneratedRegex(@"^undefined reference to '(?<symbol>[^']+)'$")]
    private static partial Regex UndefinedReference();

    private sealed record FixIt(string File, int StartLine, int StartColumn, int EndLine, int EndColumn, string Text);

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "gcc") return null;

        var output = context.Output;
        var at = IndexOf(output, error.FirstLineSequence);
        if (at < 0) return null;

        var fixIts = After(output, at);
        var explanation = $"The compiler worked this out itself and printed it as a fix-it: {error.Message}.";

        // A misspelt function is only a warning to the compiler. The error is the linker's, which
        // knows nothing about source, and the fix-it hangs off the warning.
        if (fixIts.Count == 0 && UndefinedReference().Match(error.Message ?? "") is { Success: true } undefined)
        {
            var symbol = undefined.Groups["symbol"].Value;
            var warning = -1;

            for (var i = 0; i < output.Count && warning < 0; i++)
                if (output[i].Text.Contains($"implicit declaration of function '{symbol}'", StringComparison.Ordinal)) warning = i;

            if (warning >= 0)
            {
                fixIts = After(output, warning);
                explanation =
                    $"`{symbol}` was never declared, so the compiler only warned - naming the function it was nearest to - " +
                    $"and then the linker could not find `{symbol}` anywhere.";
            }
        }

        fixIts = fixIts.Distinct().ToList();
        if (fixIts.Count == 0) return null;

        var files = fixIts.Select(f => context.Resolve(f.File)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files is not [{ } path] || context.Read(path) is not { } source) return null;

        var first = fixIts.Min(f => f.StartLine);
        var last = fixIts.Max(f => f.EndLine);
        if (first < 1 || last > source.Count) return null;

        var block = string.Join("\n", source.Lines.Skip(first - 1).Take(last - first + 1));
        var edits = new List<(int Start, int End, string Text)>();

        foreach (var fixIt in fixIts)
        {
            if (Offset(source, first, fixIt.StartLine, fixIt.StartColumn) is not { } start ||
                Offset(source, first, fixIt.EndLine, fixIt.EndColumn) is not { } end ||
                end < start)
                return null;

            edits.Add((start, end, fixIt.Text));
        }

        edits.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Two fix-its over the same text disagree about it, and neither can be applied after the other.
        for (var i = 1; i < edits.Count; i++)
            if (edits[i].Start < edits[i - 1].End) return null;

        var edited = block;
        foreach (var edit in Enumerable.Reverse(edits)) edited = edited[..edit.Start] + edit.Text + edited[edit.End..];

        var replaced = edits.Select(e => block[e.Start..e.End]).ToList();

        var title = edits.Count == 1 && replaced[0].Length > 0 && !edits[0].Text.Contains('\n')
            ? $"Change {replaced[0]} to {edits[0].Text}"
            : edits.All(e => e.Start == e.End && e.Text.TrimStart().StartsWith("#include", StringComparison.Ordinal))
                ? $"Add {string.Join(", ", edits.Select(e => e.Text.Trim()).Distinct())}"
                : "Make the change the compiler suggested";

        return new LocalFix
        {
            RuleId = Id,
            Title = title,
            Explanation = explanation,
            File = source.Path,
            StartLine = first,
            RemoveCount = last - first + 1,
            NewLines = edited.Split('\n'),
            ResolvesWarning = error.ExceptionType == "compile warning" ? error.Message : null,
        };
    }

    private static int IndexOf(IReadOnlyList<CapturedLine> output, int sequence)
    {
        for (var i = 0; i < output.Count; i++)
            if (output[i].Sequence == sequence) return i;

        return -1;
    }

    private static List<FixIt> After(IReadOnlyList<CapturedLine> output, int index)
    {
        var found = new List<FixIt>();

        for (var i = index + 1; i < output.Count; i++)
        {
            var text = output[i].Text;

            if (FixItLine().Match(text) is { Success: true } m)
            {
                found.Add(new FixIt(
                    Unescape(m.Groups["file"].Value),
                    int.Parse(m.Groups["l1"].Value), int.Parse(m.Groups["c1"].Value),
                    int.Parse(m.Groups["l2"].Value), int.Parse(m.Groups["c2"].Value),
                    Unescape(m.Groups["text"].Value)));

                continue;
            }

            if (NextDiagnostic().IsMatch(text)) break;
        }

        return found;
    }

    /// <summary>Where a line and byte column land in the block of lines starting at <paramref name="first"/>.</summary>
    private static int? Offset(SourceFile source, int first, int line, int byteColumn)
    {
        if (source.Line(line) is not { } text || CharIndex(text, byteColumn) is not { } column) return null;

        var offset = 0;
        for (var k = first; k < line; k++) offset += source.Lines[k - 1].Length + 1;

        return offset + column;
    }

    private static int? CharIndex(string line, int byteColumn)
    {
        var target = byteColumn - 1;
        var bytes = 0;

        for (var i = 0; i < line.Length;)
        {
            if (bytes == target) return i;
            if (bytes > target) return null;

            var width = char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? 2 : 1;
            bytes += Encoding.UTF8.GetByteCount(line.AsSpan(i, width));
            i += width;
        }

        return bytes == target ? line.Length : null;
    }

    /// <summary>The compiler's escaping undone: backslash sequences, and octal bytes for anything non-ASCII.</summary>
    private static string Unescape(string text)
    {
        var result = new StringBuilder();
        var pending = new List<byte>();

        void Flush()
        {
            if (pending.Count == 0) return;
            result.Append(Encoding.UTF8.GetString(pending.ToArray()));
            pending.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                Flush();
                result.Append(text[i]);
                continue;
            }

            var c = text[++i];

            if (c is >= '0' and <= '7')
            {
                var value = c - '0';

                for (var digits = 1; digits < 3 && i + 1 < text.Length && text[i + 1] is >= '0' and <= '7'; digits++)
                    value = value * 8 + (text[++i] - '0');

                pending.Add((byte)value);
                continue;
            }

            Flush();

            result.Append(c switch
            {
                'n' => '\n',
                't' => '\t',
                _ => c,
            });
        }

        Flush();
        return result.ToString();
    }
}
