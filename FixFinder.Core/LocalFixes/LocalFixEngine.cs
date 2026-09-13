using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;
using FixFinder.Core.Sources;

namespace FixFinder.Core.LocalFixes;

/// <summary>Reads one kind of error and proposes the change it asks for, or nothing.</summary>
public interface ILocalFixRule
{
    string Id { get; }

    /// <summary>A proposed change, or null when this error is not one this rule reads.</summary>
    /// <remarks>
    /// A proposal is not an answer. It goes to <see cref="CompileCheck"/> before anybody sees it,
    /// so a rule's job is to be exact about what the error said, not certain about the fix.
    /// </remarks>
    LocalFix? Propose(LocalFixContext context);
}

/// <summary>How a proposed change fared when a copy with it made was compiled.</summary>
public sealed record LocalFixVerdict(bool Accepted, string Reason);

/// <summary>A checked fix, and the file it changes.</summary>
/// <remarks>
/// The file travels with it because the patch planner will only apply a change to a file the error
/// names - right for a stranger's diff, and wrong for a linker error, which names no file at all.
/// </remarks>
public sealed record LocalFixFound(FixCandidate Candidate, string File);

/// <summary>
/// Works out a fix from the code itself, for the mistakes whose error message pins the answer down.
/// </summary>
/// <remarks>
/// Search can only ever answer a problem somebody else also had, and the commonest errors in Python,
/// Java and C are not that: a missing import, a missing semicolon, a loop that runs one step too far
/// - in code nobody else has seen. The error message usually says exactly what is wrong, and often
/// exactly where. What it never does is make the change.
/// <para>
/// <b>Every rule here is deterministic and every answer is checked.</b> A rule reads the error and
/// the lines it names and proposes one change. A copy of the file with that change in it is then
/// compiled somewhere outside the project, and the change is offered only if the compiler agrees:
/// the error it was meant to fix is gone, nothing new went wrong in the lines that changed, and -
/// for a compiler that reports everything at once - nothing new went wrong anywhere.
/// </para>
/// <para>
/// Nothing is ever run. Checking a fix for a crash only proves the file still compiles; the answer
/// says so, rather than implying it proved the crash was gone.
/// </para>
/// </remarks>
public static partial class LocalFixEngine
{
    /// <summary>Every rule, in the order they are tried. The first one whose proposal survives wins.</summary>
    public static IReadOnlyList<ILocalFixRule> Rules { get; } =
    [
        new PythonThisForSelf(),
        new PythonForgottenImport(),
        new PythonStdlibModuleTypo(),
        new PythonRelativeImportInScript(),
        new PythonNullToNone(),
        new PythonTwoToThreeName(),
        new PythonElseIf(),
        new PythonElseWithCondition(),
        new PythonExpectedColon(),
        new PythonPrintStatement(),
        new PythonAssignmentInCondition(),
        new PythonArrowOperator(),
        new PythonSlashComment(),
        new PythonLambdaReturn(),
        new PythonUnclosedBracket(),
        new PythonIndentedBlock(),
        new PythonStrConcatenation(),
        new PythonUnboundGlobal(),
        new PythonDatetimeClass(),
        new PythonSuperCall(),
        new PythonInitTypo(),

        new PythonForeignSyntax(),
        new PythonExceptComma(),
        new PythonImportFromBackwards(),
        new PythonGlobalAssignment(),
        new PythonDefWithoutParentheses(),
        new PythonFStringBrace(),
        new PythonUnterminatedString(),
        new PythonUnmatchedClosing(),
        new PythonUnexpectedIndent(),
        new PythonUnindentMismatch(),
        new PythonTabsAndSpaces(),
        new PythonRaiseString(),
        new PythonPrintRedirect(),
        new PythonForeignMethod(),
        new PythonMissingSelf(),
        new PythonDunderStrReturn(),
        new PythonRangeForInt(),
        new PythonFloatDivision(),
        new PythonComparisonTypes(),
        new PythonJoinNonStrings(),
        new PythonSortedNotSort(),
        new PythonInPlaceResult(),
        new PythonCalledConstant(),
        new PythonIsinstanceString(),
        new PythonChangedDuringIteration(),

        // Last of Python's: a nearest name is the weakest claim here, and every rule above is exact.
        new PythonExceptAs(),
        new PythonComprehensionCondition(),
        new PythonAwaitOutsideAsync(),
        new PythonCoroutineNotCalled(),
        new PythonModuleCalled(),
        new PythonMissingFromImport(),
        new PythonPropertyCalled(),
        new PythonMissingSelfAttribute(),
        new PythonSuperArguments(),
        new PythonNonlocal(),
        new PythonMissingReturn(),
        new PythonIndexWithElement(),
        new PythonTupleToList(),
        new PythonStringItemAssignment(),
        new PythonNotSubscriptable(),
        new PythonLoopUnpack(),
        new PythonUnhashableList(),
        new PythonFormatCodeOnText(),
        new PythonSequenceTimesFloat(),
        new PythonNearestName(),

        new JavaIfSemicolon(),
        new JavaUnclosedString(),
        new JavaCatchOrder(),
        new JavaWeakerAccess(),
        new JavaOverrideTypo(),
        new JavaExtendsImplements(),
        new JavaSuperFirst(),
        new JavaConstructorReturnType(),
        new JavaIllegalModifier(),
        new JavaLongLiteral(),
        new JavaRedefinition(),
        new JavaGenericArray(),
        new JavaArrayStream(),
        new JavaRemoveInForEach(),
        new JavaSplitRegex(),
        new JavaElif(),
        new JavaForEach(),
        new JavaForeignWord(),
        new JavaLowercaseClass(),
        new JavaLengthAndSize(),
        new JavaIndexing(),
        new JavaMissingNew(),
        new JavaCharAndString(),
        new JavaMissingClosingBrace(),
        new JavaPrimitiveMethod(),
        new JavaAssignmentInCondition(),
        new JavaForCounter(),
        new JavaGenericPrimitive(),
        new JavaCast(),
        new JavaStringArithmetic(),
        new JavaUninitialised(),
        new JavaMissingImport(),
        new JavaNearestName(),
        new JavaMissingSemicolon(),
        new JavaUnreportedException(),
        new JavaNonStaticMember(),
        new JavaPublicClassName(),
        new JavaOffByOneLoop(),
        new JavaStringConversion(),

        new CSharpElif(),
        new CSharpJavaPrint(),
        new CSharpConditionParentheses(),
        new CSharpMissingSemicolon(),
        new CSharpMissingClosingBrace(),
        new CSharpCharLiteralString(),
        new CSharpForeachType(),
        new CSharpNameMissing(),
        new CSharpMissingMember(),
        new CSharpNonInvocable(),
        new CSharpImplicitConversion(),
        new CSharpNonStaticMember(),
        new CSharpTypeNotFound(),
        new CSharpNamespaceTypo(),
        new CSharpAwaitWithoutAsync(),
        new CSharpInaccessible(),
        new CSharpOffByOneLoop(),
        new CSharpUnassignedLocal(),
        new CSharpMissingReturnType(),
        new CSharpInterfaceMemberPublic(),
        new CSharpVirtualBase(),
        new CSharpOverrideTypo(),
        new CSharpConstructorReturnType(),
        new CSharpInconsistentAccessibility(),
        new CSharpReadOnlyProperty(),
        new CSharpStaticThroughInstance(),
        new CSharpMethodGroup(),
        new CSharpRedefinition(),
        new CSharpOperandTypes(),
        new CSharpArgumentConversion(),
        new CSharpCollectionConversion(),
        new CSharpSwitchFallThrough(),
        new CSharpCatchOrder(),
        new CSharpIfSemicolon(),
        new CSharpRemoveInForEach(),

        new CFormatArgument(),
        new CompilerFixIt(),
        new CDefineSemicolon(),
        new CMainName(),
        new CFreeNonHeap(),
        new CIfSemicolon(),
        new CArrayParameterSize(),
        new CVoidPointerDereference(),
        new CElif(),
        new CWordOperators(),
        new CConditionParentheses(),
        new CUnterminatedString(),
        new CMissingClosingParenthesis(),
        new CExtraClosingBrace(),
        new CStringType(),
        new CStructKeyword(),
        new CMemberOperator(),
        new CForCounter(),
        new CFunctionPrototype(),
        new CArrayAssignString(),
        new CRedefinition(),
        new CIostreamInC(),
        new CCoutInC(),
        new CDoubleFree(),
        new CArrayBoundLoop(),
        new CFormatSpecifier(),
        new CStructSemicolon(),
        new CMissingStandardHeader(),
        new CNearestName(),
        new CHeaderTypo(),
        new CMissingSemicolon(),
        new CMissingClosingBrace(),
    ];

    [GeneratedRegex(@"(?:[A-Za-z]:)?[\\/](?:[^\\/'""\s]+[\\/])+")]
    private static partial Regex DirectoryPrefix();

    [GeneratedRegex(@"expected$|^illegal start of|^reached end of file while parsing|^not a statement|^unclosed|^class, interface, enum, or record expected")]
    private static partial Regex JavaSyntaxMessage();

    /// <summary>The first checked fix for this error, as a candidate ready to be shown, or null.</summary>
    public static async Task<FixCandidate?> ForAsync(
        LocalFixContext context, Action<string>? log = null, CancellationToken cancellationToken = default) =>
        (await FindAsync(context, log, cancellationToken))?.Candidate;

    /// <summary>The first checked fix for this error, with the file it changes, or null.</summary>
    public static async Task<LocalFixFound?> FindAsync(
        LocalFixContext context, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        foreach (var rule in Rules)
        {
            LocalFix? fix;

            try
            {
                fix = rule.Propose(context);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A rule is a handful of regexes over somebody's source file. One that throws costs
                // this rule, never the run.
                log?.Invoke($"{rule.Id}: gave up reading the code - {ex.Message}");
                continue;
            }

            if (fix is null) continue;

            if (!CompileCheck.CanCheck(fix.File) || context.Read(fix.File) is not { } source ||
                fix.ApplyTo(source) is not { } lines)
            {
                log?.Invoke($"{rule.Id}: proposed a change to {Path.GetFileName(fix.File)} that cannot be checked, so it is not offered");
                continue;
            }

            var path = RuntimeSuggestion.RelativePath(source.Path, context.SourceRoot);

            if (LocalFixDiff.Render(source, fix, path) is not { } diff) continue;

            log?.Invoke($"{rule.Id}: proposes \"{fix.Title}\" - compiling a copy to check it");

            var check = await CompileCheck.RunAsync(source, lines, context.PythonInterpreter, cancellationToken);
            var verdict = Judge(context, fix, check);

            log?.Invoke($"{rule.Id}: {(verdict.Accepted ? "accepted" : "refused")} - {verdict.Reason}");

            if (verdict.Accepted) return new LocalFixFound(ToCandidate(context, fix, source, diff), source.Path);
        }

        return null;
    }

    /// <summary>
    /// For a program that crashed without a word: a fix for a build warning that explains why.
    /// </summary>
    /// <remarks>
    /// On 64-bit Windows, calling <c>malloc</c> without <c>&lt;stdlib.h&gt;</c> compiles with a
    /// warning and then crashes with nothing printed at all, because C assumes an undeclared
    /// function returns a 32-bit <c>int</c> and the pointer is cut in half. The warning is the
    /// entire explanation, and it scrolled past in a build that succeeded.
    /// </remarks>
    public static async Task<LocalFixFound?> ForBuildWarningsAsync(
        IReadOnlyList<CapturedLine> buildOutput,
        string? sourceRoot,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        // Only the warnings that are themselves crashes: a function used undeclared, whose pointer
        // result C cut in half, and a printf conversion that reads a number as an address.
        var warnings = MsvcParser.ParseWarnings(buildOutput)
            .Where(w => w.ErrorCode is "C4013" or "C4477")
            .Concat(GccClangParser.ParseWarnings(buildOutput)
                .Where(w => (w.Message ?? "").StartsWith("format '", StringComparison.Ordinal)));

        foreach (var warning in warnings)
        {
            var context = new LocalFixContext { Error = warning, Output = buildOutput, SourceRoot = sourceRoot };

            if (await FindAsync(context, log, cancellationToken) is { } found) return found;
        }

        return null;
    }

    /// <summary>Whether a change did what it claimed, judged from compiling a copy with it made.</summary>
    public static LocalFixVerdict Judge(LocalFixContext context, LocalFix fix, CheckResult check)
    {
        if (!check.Ran) return new(false, "there was nothing to compile the copy with");

        // A compiler that failed without reporting anything a parser recognised has checked
        // nothing. Reading that as "no errors" is exactly how a check passes while proving nothing -
        // which is what happened when a packaged Python could not see the copy at all.
        if (check.ExitCode != 0 && check.Errors.Count == 0)
            return new(false, $"the compiler exited {check.ExitCode} without reporting why, so the change could not be checked");

        var editedFrom = fix.StartLine;
        var editedTo = fix.StartLine + Math.Max(fix.NewLines.Count, 1) - 1;

        if (fix.ResolvesWarning is { } warning)
        {
            if (check.ExitCode != 0 || check.Errors.Count > 0)
                return new(false, $"the copy does not compile: {check.Errors.FirstOrDefault()?.Message}");

            return check.Lines.Any(line => line.Text.Contains(warning, StringComparison.Ordinal))
                ? new(false, $"the compiler still warns {warning}")
                : new(true, $"the copy compiles and the warning {warning} is gone");
        }

        // An error found by running the program has nothing to compare against: the file compiled
        // before, and all a check can say is that it still does.
        var syntax = IsSyntaxPhase(context.Error);

        // A C# file run with `dotnet run` is compiled as part of running, so its compile errors
        // arrive as a run - but they are compile errors, and every one the build reported counts.
        var compiled = context.FromBuild || IsCompileError(context.Error);
        var baseline = compiled || syntax ? context.AllErrors.ToList() : [];

        if (baseline.Count == 0)
        {
            return check.Clean
                ? new(true, "the copy still compiles")
                : new(false, $"the copy no longer compiles: {check.Errors.FirstOrDefault()?.Message ?? "exit " + check.ExitCode}");
        }

        var inEdit = check.Errors.FirstOrDefault(e => LineOf(e) is { } line && line >= editedFrom - 1 && line <= editedTo + 1);
        if (inEdit is not null) return new(false, $"the changed lines still fail: {inEdit.Message}");

        var key = KeyOf(context.Error);

        // Python stops at its first syntax error, so a second one elsewhere in the file keeps the
        // count at one however right the fix was. For Python, the error leaving the changed lines
        // is the evidence; for compilers that report everything, the count has to fall.
        if (context.Error.LanguageId != "python")
        {
            var before = baseline.Count(e => KeyOf(e) == key);
            var after = check.Errors.Count(e => KeyOf(e) == key);

            if (after >= before) return new(false, $"the copy still reports: {context.Error.Message}");
        }

        // A syntax error hides everything after it - javac and cl stop reading sense into a file
        // they cannot parse - so fixing one legitimately uncovers errors nobody could see before.
        // For anything else, a new error means the change broke something.
        var known = baseline.Select(KeyOf).ToHashSet(StringComparer.Ordinal);

        if (!syntax)
        {
            if (check.Errors.FirstOrDefault(e => !known.Contains(KeyOf(e))) is { } added)
                return new(false, $"the copy reports something new: {added.Message}");
        }

        // The allowance above is for errors a syntax error hid. A link error hides nothing - it means the
        // copy compiled from the top to the bottom - so one the original did not have was made by the change.
        // gcc's own fix-it for `} elif (x == 1) {` is a semicolon, which compiles, and then fails to link
        // as a call to a function called elif.
        if (check.Errors.FirstOrDefault(e => IsLinkError(e) && !known.Contains(KeyOf(e))) is { } unlinked)
            return new(false, $"the copy compiles but no longer links: {unlinked.Message}");

        return new(true, check.Clean
            ? "the copy compiles cleanly"
            : $"the error is gone from the copy; {check.Errors.Count} other error(s) remain, none in the changed lines");
    }

    private static bool IsLinkError(ParsedError error) =>
        error.ExceptionType == "link error" || error.ErrorCode?.StartsWith("LNK", StringComparison.Ordinal) == true;

    /// <summary>What an error is, without where it is: the same mistake reads the same in the copy.</summary>
    public static string KeyOf(ParsedError error) =>
        $"{error.ErrorCode}|{error.ExceptionType}|{DirectoryPrefix().Replace(error.Message ?? "", "")}";

    private static int? LineOf(ParsedError error) => (error.CulpritFrame ?? error.Frames.FirstOrDefault())?.Line;

    /// <summary>A Roslyn error: `dotnet run` compiles before it runs, so these come from a run but are compile errors.</summary>
    public static bool IsCompileError(ParsedError error) =>
        error.LanguageId == "msvc" && error.ErrorCode?.StartsWith("CS", StringComparison.Ordinal) == true;

    /// <summary>True for errors that come from reading the file rather than from understanding it.</summary>
    public static bool IsSyntaxPhase(ParsedError error) => error.LanguageId switch
    {
        "python" => error.ExceptionType is "SyntaxError" or "IndentationError" or "TabError",
        "java" => error.ExceptionType == "compile error" && JavaSyntaxMessage().IsMatch(error.Message ?? ""),
        "msvc" => error.ErrorCode is "C2143" or "C2146" or "C2059" or "C1075" or "C1004" or "C2061"
            or "CS1002" or "CS1003" or "CS1513" or "CS1001" or "CS1012" or "CS1026" or "CS1525" or "CS1514"
            // A header that cannot be read stops the compiler dead, so everything after it went unread.
            or "C1083" or "C1189",
        "gcc" => (error.Message ?? "") is var message &&
                 (message.StartsWith("expected ", StringComparison.Ordinal) ||
                  message.EndsWith("No such file or directory", StringComparison.Ordinal) ||
                  message.EndsWith("file not found", StringComparison.Ordinal)),
        _ => false,
    };

    private static FixCandidate ToCandidate(LocalFixContext context, LocalFix fix, SourceFile source, string diff)
    {
        var how = Path.GetExtension(source.Path).ToLowerInvariant() switch
        {
            ".py" => "byte-compiled it with py_compile, which runs none of it",
            ".java" => "compiled it with javac",
            ".cs" => "built it with dotnet build",
            _ => "compiled it",
        };

        // A crash happens when the program runs, and nothing here runs it. Saying the check proved
        // the crash was gone would be claiming something nobody established.
        var ranIntoItRunning = !context.FromBuild && !IsCompileError(context.Error) && !IsSyntaxPhase(context.Error) && fix.ResolvesWarning is null;

        var outcome = ranIntoItRunning
            ? "it still compiles. The crash happens when the program runs, so run it again to confirm."
            : fix.ResolvesWarning is { } warning
                ? $"it compiles without the warning {warning}."
                : "the error is gone.";

        var body = new StringBuilder()
            .AppendLine(fix.Explanation)
            .AppendLine()
            .AppendLine($"FixFinder made this change to a copy of {Path.GetFileName(source.Path)}, outside your project, and {how}: {outcome}")
            .AppendLine()
            .AppendLine("```diff")
            .Append(diff)
            .AppendLine("```")
            .ToString();

        var now = DateTimeOffset.UtcNow;

        var candidate = new FixCandidate
        {
            SourceName = "Worked out from your code",
            Id = $"local:{fix.RuleId}",
            Title = fix.Title,
            Url = "",
            BodyText = body,
            RawBody = body,
            RawBodyIsHtml = false,
            Tier = FixTier.AutoAppliable,
            CreatedAt = now,
            LastActivityAt = now,
            AnswerNoun = "checks",
        };

        // Not ranked against search results, because it is not one: it came out of this file and
        // this error, and a compiler has already agreed with it.
        candidate.Score = 100;
        candidate.ScoreComponents =
        [
            new ScoreComponent(
                "Worked out from your code, then checked", 1.0, 1.0,
                $"{fix.RuleId}: a copy with this change was compiled - {outcome.TrimEnd('.')}"),
        ];

        return candidate;
    }
}
