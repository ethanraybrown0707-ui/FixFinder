using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Failures the code expects and deals with itself: a line inside a try whose handler catches what it raises - trying
/// first and handling what goes wrong, as int(text) inside try/except ValueError does - or inside a test's
/// with pytest.raises(ValueError) or with self.assertRaises(ValueError), which checks the code refuses what it should.
/// Such a failure is how the program works, not a mistake in it, so it is not reported.
/// </summary>
/// <remarks>
/// Only a handler written in the same function counts - a caller's try around the call may not always be there - and
/// only one that certainly catches: its type is the exception raised or one the language's own documentation lists
/// above it, it has no when test that could let the exception through, and it does not raise the exception again.
/// The program's own exception classes are followed up to the language's through what each is declared to extend.
/// <para>
/// A test that hands a lambda to its framework's assertion expects the lambda to raise too - JUnit's
/// assertThrows(IllegalArgumentException.class, () -> ...), Assert.Throws&lt;FormatException&gt;(() => ...), Jest's
/// expect(() => ...).toThrow(RangeError) - and the lambda is a function of its own, so the assertion around it is
/// found where the lambda is written.
/// </para>
/// </remarks>
internal static class CaughtFailures
{
    /// <summary>What a test's assertion expects the lambda it is handed to raise.</summary>
    /// <param name="Types">The exceptions expected, or null for any exception at all.</param>
    /// <param name="Exactly">Whether the exception has to be that very type, rather than it or one below it.</param>
    private sealed record Expectation(IReadOnlyList<string>? Types, bool Exactly);

    /// <summary>
    /// Each language's exceptions that a handler can catch by a more general type, with the type directly above each -
    /// only those the failures this analysis reports can raise, and each as the language's own documentation lists it.
    /// </summary>
    private static readonly Dictionary<SourceLanguage, Dictionary<string, string>> Above = new()
    {
        [SourceLanguage.Python] = new(StringComparer.Ordinal)
        {
            ["Exception"] = "BaseException",
            ["ArithmeticError"] = "Exception", ["ZeroDivisionError"] = "ArithmeticError", ["OverflowError"] = "ArithmeticError",
            ["LookupError"] = "Exception", ["IndexError"] = "LookupError", ["KeyError"] = "LookupError",
            ["AssertionError"] = "Exception", ["AttributeError"] = "Exception", ["TypeError"] = "Exception", ["ValueError"] = "Exception",
            ["RuntimeError"] = "Exception", ["NotImplementedError"] = "RuntimeError",
        },
        [SourceLanguage.Java] = new(StringComparer.Ordinal)
        {
            ["Exception"] = "Throwable", ["Error"] = "Throwable", ["RuntimeException"] = "Exception", ["AssertionError"] = "Error",
            ["ArithmeticException"] = "RuntimeException", ["NullPointerException"] = "RuntimeException",
            ["IllegalArgumentException"] = "RuntimeException", ["NumberFormatException"] = "IllegalArgumentException",
            ["IndexOutOfBoundsException"] = "RuntimeException", ["ArrayIndexOutOfBoundsException"] = "IndexOutOfBoundsException",
            ["NoSuchElementException"] = "RuntimeException", ["EmptyStackException"] = "RuntimeException",
        },
        [SourceLanguage.CSharp] = new(StringComparer.Ordinal)
        {
            ["SystemException"] = "Exception",
            ["ArithmeticException"] = "SystemException", ["DivideByZeroException"] = "ArithmeticException",
            ["FormatException"] = "SystemException", ["NullReferenceException"] = "SystemException",
            ["IndexOutOfRangeException"] = "SystemException", ["InvalidOperationException"] = "SystemException",
            ["ArgumentException"] = "SystemException", ["ArgumentOutOfRangeException"] = "ArgumentException", ["ArgumentNullException"] = "ArgumentException",
        },
        [SourceLanguage.JavaScript] = new(StringComparer.Ordinal)
        {
            ["RangeError"] = "Error", ["TypeError"] = "Error",
        },
    };

    /// <summary>The findings that are not about a failure the function catches itself.</summary>
    /// <param name="declared">The types the function's names are declared with, which say what some failures raise.</param>
    public static IEnumerable<AnalysisFinding> Uncaught(
        IEnumerable<AnalysisFinding> findings, IrFunction function, IrProgram program, IReadOnlyDictionary<string, IrType> declared)
    {
        foreach (var finding in findings)
        {
            var raised = finding.Raises ?? Failures.Raised(finding.CheckId, program.Language, Failing(function, finding.Span), declared);
            if (raised is { Count: > 0 } && Caught(function, finding.Span, raised, program)) continue;

            yield return finding;
        }
    }

    /// <summary>The expression a finding is about, found by where it is written.</summary>
    private static Expr? Failing(IrFunction function, SourceSpan span) =>
        IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within).FirstOrDefault(expression => expression.Span == span);

    /// <summary>
    /// Whether a try around the failing expression, a test's with block, or a test's assertion the function is handed to
    /// as a lambda catches or expects everything it can raise.
    /// </summary>
    private static bool Caught(IrFunction function, SourceSpan span, IReadOnlyList<string> raised, IrProgram program)
    {
        // A test method the framework expects to raise - JUnit 4's @Test(expected = ...) - passes when it does.
        if (function.ExpectedToRaise.Count > 0 && raised.All(exception => function.ExpectedToRaise.Any(expected => Catches(expected, exception, program))))
            return true;

        if (function.EnclosedBy is not null && ExpectedOf(function, program) is { } expectation &&
            raised.All(exception => expectation.Types is null ||
                                    expectation.Types.Any(type => expectation.Exactly ? Simple(type) == Simple(exception) : Catches(type, exception, program))))
            return true;

        foreach (var statement in IrWalk.Statements(function.Body))
        {
            switch (statement)
            {
                case Try attempt when Holds(attempt.Body, span) &&
                                      raised.All(exception => attempt.Handlers.Any(handler => Handles(handler, exception, program))):
                    return true;

                case Using used when program.Language == SourceLanguage.Python && Holds(used.Body, span) && Expected(used.Resource) is { } expected &&
                                     raised.All(exception => expected.Any(caught => Catches(caught, exception, program))):
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether the expression at this place is written somewhere in the block, however deeply.</summary>
    private static bool Holds(IReadOnlyList<Stmt> block, SourceSpan span) =>
        IrWalk.Statements(block).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within).Any(expression => expression.Span == span);

    /// <summary>
    /// Whether a handler certainly deals with the exception: it names the exception or a type above it, or names none -
    /// a bare except or catch - and neither tests before catching nor raises the exception again.
    /// </summary>
    private static bool Handles(Handler handler, string exception, IrProgram program) =>
        !handler.Filtered && !RaisesAgain(handler) &&
        (handler.ExceptionTypes.Count == 0 || handler.ExceptionTypes.Any(caught => Catches(caught, exception, program)));

    /// <summary>A handler that raises what it caught - raise on its own, throw; or throw e - passes the exception on.</summary>
    private static bool RaisesAgain(Handler handler) =>
        IrWalk.Statements(handler.Body).OfType<Throw>().Any(thrown =>
            thrown.Exception is null || thrown.Exception is Name { Identifier: var rethrown } && rethrown == handler.Variable);

    /// <summary>Whether catching one type catches the other: the same type, or one above it.</summary>
    private static bool Catches(string caught, string raised, IrProgram program)
    {
        var wanted = Simple(caught);
        var type = Simple(raised);

        for (var steps = 0; type is not null && steps < 32; steps++)
        {
            if (type == wanted) return true;
            type = Parent(type, program);
        }

        return false;
    }

    /// <summary>The type directly above an exception: the one the program's own class extends, or the language's own.</summary>
    private static string? Parent(string type, IrProgram program)
    {
        if (program.Classes.FirstOrDefault(declared => declared.Name == type) is { } own)
            return own.Bases.Count > 0 ? Simple(own.Bases[0]) : null;

        return Above.TryGetValue(program.Language, out var above) && above.TryGetValue(type, out var parent) ? parent : null;
    }

    /// <summary>A type's own name, without the module or package it is written with: builtins.ValueError, java.lang.Exception.</summary>
    private static string Simple(string name) => name[(name.LastIndexOf('.') + 1)..];

    /// <summary>What a test's assertion expects of the function, when the function is a lambda handed to one; null otherwise.</summary>
    private static Expectation? ExpectedOf(IrFunction function, IrProgram program) =>
        program.AllFunctions
            .Where(other => !ReferenceEquals(other, function) && string.Equals(other.Span.File, function.Span.File, StringComparison.OrdinalIgnoreCase))
            .SelectMany(other => IrWalk.Statements(other.Body)).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within).OfType<Call>()
            .Select(call => Expecting(call, function))
            .FirstOrDefault(expectation => expectation is not null);

    private static Expectation? Expecting(Call call, IrFunction function) => call switch
    {
        // JUnit's assertThrows passes for the type or one below it; assertThrowsExactly only for the type itself.
        { CalleeName: "assertThrows" or "assertThrowsExactly", Arguments: [{ Value: Member { MemberName: "class", Target: var type } }, var handed, ..] }
            when Hands(handed, function) && TypeName(type) is { } expected =>
            new Expectation([expected], Exactly: call.CalleeName == "assertThrowsExactly"),

        { CalleeName: var method, TypeArguments: [var type], Arguments: [var handed, ..] } when Hands(handed, function) && ExpectsExactly(method) is { } exactly =>
            new Expectation([type.Name], exactly),

        // Jest's expect(...).toThrow() expects any error; toThrow(RangeError) an instance of the class; a message, any error that has it.
        { Callee: Member { MemberName: "toThrow", Target: Call { CalleeName: "expect", Arguments: [var handed] } }, Arguments: var expected } when Hands(handed, function) =>
            expected is [{ Value: Name { Identifier: var errorClass } }] ? new Expectation([errorClass], Exactly: false) : new Expectation(null, Exactly: false),

        _ => null,
    };

    /// <summary>Whether this argument is the lambda the function was written as.</summary>
    private static bool Hands(Argument argument, IrFunction function) =>
        argument.Value is Opaque { What: "lambda expression" } lambda && lambda.Span == function.Span;

    /// <summary>
    /// Whether a C# test framework's assertion wants exactly the type it names. MSTest's ThrowsException and
    /// ThrowsExactly say they do. The rest - Throws, ThrowsAny, Catch - are taken to accept a type below it as well,
    /// the more lenient reading, so a test that may pass is never said to fail. Null for any other method.
    /// </summary>
    private static bool? ExpectsExactly(string? method) => method switch
    {
        "ThrowsException" or "ThrowsExceptionAsync" or "ThrowsExactly" or "ThrowsExactlyAsync" => true,
        "Throws" or "ThrowsAsync" or "ThrowsAny" or "ThrowsAnyAsync" or "Catch" or "CatchAsync" => false,
        _ => null,
    };

    /// <summary>The class a Java class literal names: IllegalArgumentException in IllegalArgumentException.class.</summary>
    private static string? TypeName(Expr type) => type switch
    {
        Name { Identifier: var named } => named,
        Member { MemberName: var named } => named,
        _ => null,
    };

    /// <summary>
    /// The exceptions a test's with block expects to be raised inside it: pytest.raises(ValueError),
    /// self.assertRaises((ValueError, TypeError)), self.assertRaisesRegex(ValueError, "percentage").
    /// </summary>
    private static IReadOnlyList<string>? Expected(Expr resource) => resource switch
    {
        Call { Callee: Name { Identifier: "raises" } or Member { MemberName: "raises" or "assertRaises" or "assertRaisesRegex" }, Arguments: [{ Name: null, Value: var expected }, ..] } =>
            expected switch
            {
                Name { Identifier: var named } => [named],
                Member { MemberName: var named } => [named],
                CollectionLiteral { Kind: CollectionKind.Tuple, Items: var several } when several.All(item => item is Name or Member) =>
                    several.Select(item => item is Name { Identifier: var named } ? named : ((Member)item).MemberName).ToList(),
                _ => null,
            },
        _ => null,
    };
}
