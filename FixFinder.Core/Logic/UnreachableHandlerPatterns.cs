using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A handler for an error that a handler before it already catches - except Exception: and then except ValueError:, rescue
/// StandardError and then rescue ArgumentError, catch (Exception $e) and then catch (InvalidArgumentException $e). The
/// first handler takes every error the second would, so the second never runs, and what it does can never be tested. Run with
/// the second's error, Python, Ruby and PHP each printed the first handler's message, with nothing said by python -W error,
/// ruby -w or PHP. Dart's analyzer warns of it, C++'s compiler warns of it, and PowerShell, Java and C# refuse it, so they are
/// not here. Which error comes from which is each language's own family of errors, and the classes the file declares itself.
/// The change moves the handler before the one that took its errors.
/// </summary>
public static partial class UnreachableHandlerPatterns
{
    /// <summary>Python's built-in exceptions, each with the one it comes from.</summary>
    private static readonly IReadOnlyDictionary<string, string> PythonFamily = Family(
        "Exception:BaseException SystemExit:BaseException KeyboardInterrupt:BaseException GeneratorExit:BaseException " +
        "ArithmeticError:Exception ZeroDivisionError:ArithmeticError OverflowError:ArithmeticError FloatingPointError:ArithmeticError " +
        "LookupError:Exception IndexError:LookupError KeyError:LookupError " +
        "OSError:Exception IOError:OSError EnvironmentError:OSError FileNotFoundError:OSError FileExistsError:OSError PermissionError:OSError " +
        "IsADirectoryError:OSError NotADirectoryError:OSError TimeoutError:OSError InterruptedError:OSError BlockingIOError:OSError " +
        "ChildProcessError:OSError ProcessLookupError:OSError ConnectionError:OSError BrokenPipeError:ConnectionError " +
        "ConnectionAbortedError:ConnectionError ConnectionRefusedError:ConnectionError ConnectionResetError:ConnectionError " +
        "ValueError:Exception UnicodeError:ValueError UnicodeDecodeError:UnicodeError UnicodeEncodeError:UnicodeError UnicodeTranslateError:UnicodeError " +
        "TypeError:Exception NameError:Exception UnboundLocalError:NameError AttributeError:Exception RuntimeError:Exception " +
        "RecursionError:RuntimeError NotImplementedError:RuntimeError ImportError:Exception ModuleNotFoundError:ImportError EOFError:Exception " +
        "AssertionError:Exception StopIteration:Exception StopAsyncIteration:Exception BufferError:Exception MemoryError:Exception " +
        "ReferenceError:Exception SyntaxError:Exception IndentationError:SyntaxError TabError:IndentationError SystemError:Exception " +
        "Warning:Exception UserWarning:Warning DeprecationWarning:Warning RuntimeWarning:Warning");

    /// <summary>Ruby's built-in exceptions, each with the one it comes from.</summary>
    private static readonly IReadOnlyDictionary<string, string> RubyFamily = Family(
        "NoMemoryError:Exception ScriptError:Exception LoadError:ScriptError NotImplementedError:ScriptError SyntaxError:ScriptError " +
        "SecurityError:Exception SignalException:Exception Interrupt:SignalException SystemExit:Exception SystemStackError:Exception " +
        "StandardError:Exception ArgumentError:StandardError UncaughtThrowError:ArgumentError EncodingError:StandardError FiberError:StandardError " +
        "IOError:StandardError EOFError:IOError IndexError:StandardError KeyError:IndexError StopIteration:IndexError ClosedQueueError:StopIteration " +
        "LocalJumpError:StandardError NameError:StandardError NoMethodError:NameError RangeError:StandardError FloatDomainError:RangeError " +
        "RegexpError:StandardError RuntimeError:StandardError FrozenError:RuntimeError SystemCallError:StandardError ThreadError:StandardError " +
        "TypeError:StandardError ZeroDivisionError:StandardError");

    /// <summary>PHP's built-in throwables, each with the one it comes from.</summary>
    private static readonly IReadOnlyDictionary<string, string> PhpFamily = Family(
        "Exception:Throwable Error:Throwable ErrorException:Exception LogicException:Exception BadFunctionCallException:LogicException " +
        "BadMethodCallException:BadFunctionCallException DomainException:LogicException InvalidArgumentException:LogicException " +
        "LengthException:LogicException OutOfRangeException:LogicException RuntimeException:Exception OutOfBoundsException:RuntimeException " +
        "OverflowException:RuntimeException RangeException:RuntimeException UnderflowException:RuntimeException " +
        "UnexpectedValueException:RuntimeException JsonException:Exception TypeError:Error ArgumentCountError:TypeError ValueError:Error " +
        "ArithmeticError:Error DivisionByZeroError:ArithmeticError CompileError:Error ParseError:CompileError UnhandledMatchError:Error");

    private static IReadOnlyDictionary<string, string> Family(string pairs) =>
        pairs.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split(':')).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-python-handler-never-reached", CodePattern.Files(".py", ".pyw"), Syntax.Python, PythonHandlers, Severity.Error, Confidence.Certain),
        new CodePattern("logic-ruby-handler-never-reached", CodePattern.Files(".rb"), Syntax.Python, RubyHandlers, Severity.Error, Confidence.Certain),
        new CodePattern("logic-php-handler-never-reached", CodePattern.Files(".php"), Syntax.CLike, PhpHandlers, Severity.Error, Confidence.Certain),
    ];

    /// <summary>Each error and the one it comes from: the language's own, and the classes the file declares from another.</summary>
    private static Dictionary<string, string> FamilyIn(IReadOnlyList<string> masked, IReadOnlyDictionary<string, string> builtIn, Regex declared)
    {
        var family = new Dictionary<string, string>(builtIn, StringComparer.Ordinal);
        foreach (var line in masked)
            if (declared.Match(line) is { Success: true } made) family[made.Groups["name"].Value] = made.Groups["parent"].Value.TrimStart('\\');
        return family;
    }

    /// <summary>Whether <paramref name="error"/> is <paramref name="caught"/>, or comes from it, however far back.</summary>
    private static bool CaughtBy(string error, string caught, IReadOnlyDictionary<string, string> family)
    {
        var steps = 0;
        for (var current = error; steps++ < 50; )
        {
            if (current == caught) return true;
            if (!family.TryGetValue(current, out var parent)) return false;
            current = parent;
        }
        return false;
    }

    /// <summary>One handler: its line, the errors it names, and the lines it covers - its line, and its body up to the next.</summary>
    private sealed record Handler(int Line, IReadOnlyList<string> Errors, int End);

    private static IEnumerable<LogicFinding> Unreached(string id, SourceFile source, IReadOnlyList<Handler> handlers, IReadOnlyDictionary<string, string> family,
        bool movable, string keyword)
    {
        for (var later = 1; later < handlers.Count; later++)
        {
            var errors = handlers[later].Errors;
            if (errors.Count == 0) continue;

            var earlier = Enumerable.Range(0, later).FirstOrDefault(before =>
                errors.All(error => handlers[before].Errors.Any(caught => CaughtBy(error, caught, family))), -1);
            if (earlier < 0) continue;

            var catcher = handlers[earlier];
            var taker = catcher.Errors.First(caught => errors.All(error => CaughtBy(error, caught, family)) || errors.Any(error => CaughtBy(error, caught, family)));
            var moved = handlers[later];
            LocalFix? change = null;
            if (movable)
            {
                // The handler's lines, moved to just before the one that took its errors.
                var lines = source.Lines.Skip(moved.Line).Take(moved.End - moved.Line)
                    .Concat(source.Lines.Skip(catcher.Line).Take(moved.Line - catcher.Line)).ToList();
                change = new LocalFix
                {
                    RuleId = id, Title = $"Put the {keyword} for {string.Join(", ", errors)} first",
                    Explanation = $"Handlers are tried in order, so the one for {string.Join(", ", errors)} must come before the one for {taker}, which " +
                                  "takes them all.",
                    File = source.Path, StartLine = catcher.Line + 1, RemoveCount = moved.End - catcher.Line, NewLines = lines,
                };
            }

            yield return new LogicFinding(id, moved.Line + 1,
                $"{string.Join(", ", errors)} {(errors.Count == 1 ? "is" : "are")} {(errors.Contains(taker) ? "" : "a kind of ")}{taker}, which the {keyword} on " +
                $"line {catcher.Line + 1} catches first - handlers are tried in order - so this one never runs, and what it does can never be tested",
                change);
        }
    }

    [GeneratedRegex(@"^\s*class\s+(?<name>[A-Za-z_]\w*)\s*\(\s*(?<parent>[A-Za-z_][\w.]*)\s*\)")]
    private static partial Regex PythonClass();

    private static IEnumerable<LogicFinding> PythonHandlers(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var family = FamilyIn(masked, PythonFamily, PythonClass());
        var handler = new Regex(@"^(?<indent>\s*)except\b\s*(?<errors>\([^)]*\)|[A-Za-z_][\w.]*)?\s*(?:as\s+\w+)?\s*:");
        for (var index = 0; index < masked.Count; index++)
        {
            if (!Regex.IsMatch(masked[index], @"^\s*try\s*:")) continue;
            var indent = masked[index].Length - masked[index].TrimStart().Length;
            var handlers = new List<(int Line, IReadOnlyList<string> Errors)>();
            var end = masked.Count;
            for (var line = index + 1; line < masked.Count; line++)
            {
                if (masked[line].Trim().Length == 0) continue;
                var lineIndent = masked[line].Length - masked[line].TrimStart().Length;
                if (lineIndent > indent) continue;
                if (lineIndent < indent) { end = line; break; }
                if (handler.Match(masked[line]) is { Success: true } found)
                {
                    var named = found.Groups["errors"].Value.Trim('(', ')', ' ');
                    handlers.Add((line, named.Length == 0 ? ["BaseException"] : named.Split(',').Select(error => error.Trim().Split('.')[^1]).Where(error => error.Length > 0).ToList()));
                    continue;
                }
                if (Regex.IsMatch(masked[line], @"^\s*(?:else|finally)\s*:") || handlers.Count > 0) { end = line; break; }
            }

            var spans = handlers.Select((each, number) => new Handler(each.Line, each.Errors,
                number + 1 < handlers.Count ? handlers[number + 1].Line : LastCodeLine(masked, each.Line, end) + 1)).ToList();
            foreach (var finding in Unreached(id, source, spans, family, true, "except")) yield return finding;
        }
    }

    /// <summary>The last line with code from <paramref name="from"/> up to, not including, <paramref name="before"/>.</summary>
    private static int LastCodeLine(IReadOnlyList<string> masked, int from, int before)
    {
        var last = from;
        for (var line = from; line < before; line++) if (masked[line].Trim().Length > 0) last = line;
        return last;
    }

    [GeneratedRegex(@"^\s*class\s+(?<name>[A-Z]\w*)\s*<\s*(?<parent>[A-Z][\w:]*)")]
    private static partial Regex RubyClass();

    private static IEnumerable<LogicFinding> RubyHandlers(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var family = FamilyIn(masked, RubyFamily, RubyClass());
        var handler = new Regex(@"^(?<indent>\s*)rescue\b\s*(?<errors>[A-Z][\w:]*(?:\s*,\s*[A-Z][\w:]*)*)?\s*(?:=>\s*\w+)?\s*$");
        for (var index = 0; index < masked.Count; index++)
        {
            if (!Regex.IsMatch(masked[index], @"^\s*(?:begin|def\s+[\w.?!]+.*)\s*$")) continue;
            var indent = masked[index].Length - masked[index].TrimStart().Length;
            var handlers = new List<(int Line, IReadOnlyList<string> Errors)>();
            var end = -1;
            for (var line = index + 1; line < masked.Count; line++)
            {
                if (masked[line].Trim().Length == 0) continue;
                var lineIndent = masked[line].Length - masked[line].TrimStart().Length;
                if (lineIndent > indent) continue;
                if (lineIndent < indent) break;
                if (handler.Match(masked[line]) is { Success: true } found)
                {
                    var named = found.Groups["errors"].Value;
                    handlers.Add((line, named.Length == 0 ? ["StandardError"] : named.Split(',').Select(error => error.Trim().Split("::")[^1]).ToList()));
                    continue;
                }
                end = line;
                break;
            }

            if (end < 0 || handlers.Count < 2) continue;
            var spans = handlers.Select((each, number) => new Handler(each.Line, each.Errors, number + 1 < handlers.Count ? handlers[number + 1].Line : end)).ToList();
            foreach (var finding in Unreached(id, source, spans, family, true, "rescue")) yield return finding;
        }
    }

    [GeneratedRegex(@"^\s*(?:(?:abstract|final)\s+)?class\s+(?<name>\w+)\s+extends\s+(?<parent>\\?[\w\\]+)")]
    private static partial Regex PhpClass();

    private static IEnumerable<LogicFinding> PhpHandlers(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var family = FamilyIn(masked, PhpFamily, PhpClass());
        var handler = new Regex(@"^\s*(?<close>\}\s*)?catch\s*\(\s*(?<errors>\\?[\w\\]+(?:\s*\|\s*\\?[\w\\]+)*)\s*(?:\$\w+)?\s*\)\s*\{\s*$");
        for (var index = 0; index < masked.Count; index++)
        {
            if (!Regex.IsMatch(masked[index], @"^\s*try\s*\{\s*$") || Brackets.FirstBlockEnd(masked, index) is not { } tryEnd) continue;

            var handlers = new List<(int Line, IReadOnlyList<string> Errors, bool OnClosingLine)>();
            var line = tryEnd;
            while (line < masked.Count && handler.Match(masked[line]) is { Success: true } found)
            {
                handlers.Add((line, found.Groups["errors"].Value.Split('|').Select(error => error.Trim().Split('\\')[^1]).ToList(), found.Groups["close"].Success));
                if (Brackets.FirstBlockEnd(masked, line) is not { } handlerEnd) break;
                line = handlerEnd;
            }

            if (handlers.Count < 2) continue;
            // Moved as whole lines only when each catch starts on the line that closes the block before it: } catch (...) {.
            var movable = handlers.All(each => each.OnClosingLine) && Brackets.FirstBlockEnd(masked, handlers[^1].Line) is { } last && masked[last].Trim() == "}";
            var spans = handlers.Select((each, number) => new Handler(each.Line, each.Errors,
                number + 1 < handlers.Count ? handlers[number + 1].Line : Brackets.FirstBlockEnd(masked, each.Line) ?? each.Line + 1)).ToList();
            foreach (var finding in Unreached(id, source, spans, family, movable, "catch")) yield return finding;
            index = line;
        }
    }
}
