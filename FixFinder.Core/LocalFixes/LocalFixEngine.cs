using System.Security.Cryptography;
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

    LocalFix? Propose(LocalFixContext context);
}

/// <summary>How a proposed change fared when a copy with it made was compiled.</summary>
public sealed record LocalFixVerdict(bool Accepted, string Reason);

/// <summary>A checked fix, and the file it changes.</summary>
public sealed record LocalFixFound(FixCandidate Candidate, string File)
{
    public LocalFix? Fix => Candidate.LocalFix;
}

/// <summary>Works out a fix from the code itself, for the mistakes whose error message pins the answer down.</summary>
public static partial class LocalFixEngine
{
    public static IReadOnlyList<ILocalFixRule> Rules { get; } =
    [
        new Logic.LogicPatternRule(),
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

        new PythonSmartQuotes(),
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
        new PythonSetMethod(),
        new PythonDequePopLeft(),
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
        new PythonInputNotNumber(),
        new PythonJsonLoadOrLoads(),
        new PythonUnwrittenAbstractMethod(),
        new PythonDataclassDefaultFactory(),
        new PythonHashWithEq(),
        new PythonSortCmp(),
        new PythonArgsTuple(),
        new PythonSqlParameterTuple(),
        new PythonSocketAddress(),
        new PythonTextToBytes(),
        new PythonMainGuard(),
        new PythonGatherList(),
        new PythonCoroutineNotAwaited(),
        new PythonMatchArgs(),
        new PythonGeneratorLen(),
        new PythonJsonSet(),
        new PythonDivisionGuard(),
        new PythonRecursionBaseCase(),
        new PythonMissingKeyGet(),
        new PythonNearestName(),

        new JavaSmartQuotes(),
        new JavaFallthroughBreak(),
        new JavaDivisionGuard(),
        new JavaRecursionBaseCase(),
        new JavaMainSignature(),
        new JavaGenericMethodParameter(),
        new JavaRawComparable(),
        new JavaInterfaceDefault(),
        new JavaRecordAccessor(),
        new JavaMapIncrement(),
        new JavaFormatConversion(),
        new JavaFixedSizeCollection(),
        new JavaWaitWithoutMonitor(),
        new JavaNotSerializable(),
        new JavaIfSemicolon(),
        new JavaUnclosedString(),
        new JavaCatchOrder(),
        new JavaWeakerAccess(),
        new JavaOverrideTypo(),
        new JavaUnwrittenMethod(),
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

        new CSharpSmartQuotes(),
        new CSharpDivisionGuard(),
        new CSharpMissingKeyDefault(),
        new CSharpEmptySequenceDefault(),
        new CSharpGenericInterface(),
        new CSharpStructInCollection(),
        new CSharpIteratorReturnType(),
        new CSharpRecordWith(),
        new CSharpDelegateCalled(),
        new CSharpAsyncMain(),
        new CSharpTaskNotAwaited(),
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
        new CSharpUnwrittenMember(),
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

        new GoWaitGroupByValue(),
        new GoCloseChannel(),
        new GoUnmarshalPointer(),
        new GoMainName(),
        new GoPackageMain(),
        new GoImportQuotes(),
        new GoBraceOnNextLine(),
        new GoElseOnNextLine(),
        new GoWhile(),
        new GoForParentheses(),
        new GoUnclosedString(),
        new GoMissingClosingParen(),
        new GoMissingClosingBrace(),
        new GoOutsideFunction(),
        new GoRuneLiteral(),
        new GoAssignmentInCondition(),
        new GoRedeclared(),
        new GoMissingImport(),
        new GoForeignPrint(),
        new GoForeignWord(),
        new GoShortDeclare(),
        new GoNearestName(),
        new GoUnusedImport(),
        new GoUnusedVariable(),
        new GoPackageMember(),
        new GoSelector(),
        new GoStringPlusNumber(),
        new GoByteComparedWithString(),
        new GoNumericConversion(),
        new GoMissingReturnType(),
        new GoNotEnoughReturnValues(),
        new GoTwoValues(),
        new GoAppendNotUsed(),
        new GoPointerReceiver(),
        new GoMethodCase(),
        new GoIndexLoop(),
        new GoNilMap(),
        new GoChannelDeadlock(),
        new JsBuiltinNotLoaded(),
        new JsCallbackApiUsedForValue(),
        new JsCallbackCalledTooSoon(),
        new JsPromiseCombinatorArray(),
        new JsApostrophe(),
        new JsMissingClosingParen(),
        new JsUnclosedString(),
        new JsMissingClosingBrace(),
        new JsExtraClosingBrace(),
        new JsElif(),
        new JsConditionParentheses(),
        new JsForOf(),
        new JsThinArrow(),
        new JsKeywordTypo(),
        new JsMissingComma(),
        new JsRedeclared(),
        new JsAwaitOutsideAsync(),
        new JsNamedExportTypo(),
        new JsRequireInModule(),
        new JsCoreModuleTypo(),
        new JsForeignPrint(),
        new JsForeignWord(),
        new JsLen(),
        new JsSuperMissing(),
        new JsMissingThis(),
        new JsNearestName(),
        new JsClassWithoutNew(),
        new JsConstReassigned(),
        new JsGetterCalled(),
        new JsStaticOnInstance(),
        new JsModuleExportsTypo(),
        new JsPromiseNotAwaited(),
        new JsArrayCalled(),
        new JsMemberNotFunction(),
        new JsReduceWithoutInitial(),
        new JsForEachResult(),
        new JsMapBracket(),
        new JsConstructorWithoutThis(),
        new JsThisInCallback(),
        new JsDetachedMethod(),
        new JsSetterRecursion(),
        new CppStreamOperatorFriend(),
        new CppConstMapIndex(),
        new CppOverrideMissingConst(),
        new CppThreadReference(),
        new CppVirtualDestructor(),
        new CppCatchByReference(),
        new CScanfArrayAddress(),
        new CMallocWrongSizeof(),
        new CFreeWhileWalking(),
        new CHeaderGuard(),
        new CPthreadStartRoutine(),
        new CStringCompare(),
        new CQsortComparator(),
        new CUninitialisedAccumulator(),
        new CSmartQuotes(),
        new CFallthroughBreak(),
        new CFormatArgument(),
        new CppStdPrefix(),
        new CppStdNameTypo(),
        new CppForeignPrint(),
        new CppForeignWord(),
        new CppStreamArrows(),
        new CppContainerMember(),
        new CppMethodWithoutCall(),
        new CppNewWithoutPointer(),
        new CppMemberOperator(),
        new CppPrivateMember(),
        new CppMainReturnsInt(),
        new CppCharForString(),
        new CppMultiCharString(),
        new CppVexingParse(),
        new CppLiteralConcatenation(),
        new CppStringPlusNumber(),
        new CppAtOutOfRange(),
        new CppMissingReturnType(),
        new CppVirtualBase(),
        new CppOverrideTypo(),
        new CppUnwrittenOverride(),
        new CppPrivateInheritance(),
        new CppMemberWithoutClassName(),
        new CppMoveUniquePtr(),
        new CppConstMethod(),
        new CppTypename(),
        new CppLambdaCapture(),
        new CppExplicitConstructor(),
        new CppStaticMemberDefinition(),
        new CppCharComparedWithString(),
        new CppSortList(),
        new CppAutoParameter(),
        new CppDefaultArgumentRepeated(),
        new CppConstMemberInitialiser(),
        new CppThreadNotJoined(),
        new CppIndexEmptyVector(),
        new CppEraseInLoop(),
        new CppReturnLocalReference(),
        new CppDeleteArray(),
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
        new CGets(),
        new CStringTooLong(),
        new CReturnLocalAddress(),
        new CMallocElementSize(),
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

    public static async Task<FixCandidate?> ForAsync(
        LocalFixContext context, Action<string>? log = null, CancellationToken cancellationToken = default) =>
        (await FindAsync(context, log, cancellationToken))?.Candidate;

    public static int ChecksAtOnce { get; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    public static Task<LocalFixFound?> FindAsync(
        LocalFixContext context, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        if (context.Read(context.Frame?.File) is { } erring) CompileCheck.Prepare(erring);

        return FindAsync(
            context, context.Language.IsAny ? Rules : Rules.Where(context.Language.Reads).ToList(),
            (source, lines, ct) => CompileCheck.RunAsync(source, lines, context.PythonInterpreter, ct),
            ChecksAtOnce, log, cancellationToken);
    }

    internal static async Task<LocalFixFound?> FindAsync(
        LocalFixContext context,
        IReadOnlyList<ILocalFixRule> rules,
        Func<SourceFile, IReadOnlyList<string>, CancellationToken, Task<CheckResult>> check,
        int checksAtOnce,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var waiting = new Queue<(Proposal Proposal, Task<CheckResult> Check)>();

        var started = new Dictionary<string, Task<CheckResult>>(StringComparer.Ordinal);

        var next = 0;
        LocalFixFound? found = null;

        try
        {
            while (true)
            {
                while (waiting.Count < Math.Max(checksAtOnce, 1) && next < rules.Count)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (Propose(context, rules[next++], log) is not { } proposal) continue;

                    var content = $"{proposal.Source.Path}\n{Convert.ToHexString(SHA256.HashData(proposal.Source.Render(proposal.Lines)))}";

                    if (started.TryGetValue(content, out var same))
                    {
                        log?.Invoke($"{proposal.Rule.Id}: proposes \"{proposal.Fix.Title}\" - the same file an earlier rule's change makes, so that check is used");
                        waiting.Enqueue((proposal, same));
                        continue;
                    }

                    log?.Invoke($"{proposal.Rule.Id}: proposes \"{proposal.Fix.Title}\" - compiling a copy to check it");

                    var compile = Task.Run(() => check(proposal.Source, proposal.Lines, stop.Token), CancellationToken.None);

                    started[content] = compile;
                    waiting.Enqueue((proposal, compile));
                }

                if (!waiting.TryDequeue(out var earliest)) return null;

                var (rule, fix, source, _, diff) = earliest.Proposal;
                var verdict = Judge(context, fix, await earliest.Check);

                log?.Invoke($"{rule.Id}: {(verdict.Accepted ? "accepted" : "refused")} - {verdict.Reason}");

                if (verdict.Accepted)
                {
                    found = new LocalFixFound(ToCandidate(context, fix, source, diff), source.Path);
                    return found;
                }
            }
        }
        finally
        {
            if (waiting.Count > 0)
            {
                if (found is not null)
                {
                    foreach (var (unneeded, _) in waiting)
                        log?.Invoke($"{unneeded.Rule.Id}: check stopped - an earlier rule's fix was accepted");
                }

                stop.Cancel();

                try
                {
                    await Task.WhenAll(waiting.Select(w => w.Check));
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private sealed record Proposal(ILocalFixRule Rule, LocalFix Fix, SourceFile Source, IReadOnlyList<string> Lines, string Diff);

    private static Proposal? Propose(LocalFixContext context, ILocalFixRule rule, Action<string>? log)
    {
        LocalFix? fix;

        try
        {
            fix = rule.Propose(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke($"{rule.Id}: gave up reading the code - {ex.Message}");
            return null;
        }

        if (fix is null) return null;

        if (!CompileCheck.CanCheck(fix.File) || context.Read(fix.File) is not { } source ||
            fix.ApplyTo(source) is not { } lines)
        {
            log?.Invoke($"{rule.Id}: proposed a change to {Path.GetFileName(fix.File)} that cannot be checked, so it is not offered");
            return null;
        }

        var path = RuntimeSuggestion.RelativePath(source.Path, context.SourceRoot);
        fix = fix.Unambiguous(source);

        return LocalFixDiff.Render(source, fix, path) is { } diff ? new Proposal(rule, fix, source, lines, diff) : null;
    }

    public static async Task<LocalFixFound?> ForBuildWarningsAsync(
        IReadOnlyList<CapturedLine> buildOutput,
        string? sourceRoot,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        CodeLanguage? language = null)
    {
        var warnings = MsvcParser.ParseWarnings(buildOutput)
            .Where(w => w.ErrorCode is "C4013" or "C4477" or "C4172")
            .Concat(GccClangParser.ParseWarnings(buildOutput)
                .Where(w => (w.Message ?? "").StartsWith("format '", StringComparison.Ordinal) ||
                            (w.Message ?? "").StartsWith("reference to local variable '", StringComparison.Ordinal)));

        foreach (var warning in warnings)
        {
            var context = new LocalFixContext
            {
                Error = warning, Output = buildOutput, SourceRoot = sourceRoot, Language = language ?? CodeLanguage.Any,
            };

            if (await FindAsync(context, log, cancellationToken) is { } found) return found;
        }

        return null;
    }

    public static LocalFixVerdict Judge(LocalFixContext context, LocalFix fix, CheckResult check)
    {
        if (!check.Ran) return new(false, "there was nothing to compile the copy with");

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

        var syntax = IsSyntaxPhase(context.Error);

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

        if (context.Error.LanguageId != "python")
        {
            var before = baseline.Count(e => KeyOf(e) == key);
            var after = check.Errors.Count(e => KeyOf(e) == key);

            if (after >= before) return new(false, $"the copy still reports: {context.Error.Message}");
        }

        var known = baseline.Select(KeyOf).ToHashSet(StringComparer.Ordinal);

        if (!syntax)
        {
            if (check.Errors.FirstOrDefault(e => !known.Contains(KeyOf(e))) is { } added)
                return new(false, $"the copy reports something new: {added.Message}");
        }

        if (check.Errors.FirstOrDefault(e => IsLinkError(e) && !known.Contains(KeyOf(e))) is { } unlinked)
            return new(false, $"the copy compiles but no longer links: {unlinked.Message}");

        return new(true, check.Clean
            ? "the copy compiles cleanly"
            : $"the error is gone from the copy; {check.Errors.Count} other error(s) remain, none in the changed lines");
    }

    private static bool IsLinkError(ParsedError error) =>
        error.ExceptionType == "link error" || error.ErrorCode?.StartsWith("LNK", StringComparison.Ordinal) == true;

    public static string KeyOf(ParsedError error) =>
        $"{error.ErrorCode}|{error.ExceptionType}|{DirectoryPrefix().Replace(error.Message ?? "", "")}";

    private static int? LineOf(ParsedError error) => (error.CulpritFrame ?? error.Frames.FirstOrDefault())?.Line;

    public static bool IsCompileError(ParsedError error) =>
        error.LanguageId == "msvc" && error.ErrorCode?.StartsWith("CS", StringComparison.Ordinal) == true;

    public static bool IsSyntaxPhase(ParsedError error) => error.LanguageId switch
    {
        "python" => error.ExceptionType is "SyntaxError" or "IndentationError" or "TabError",
        "node" => error.ExceptionType == "SyntaxError" && !(error.Message ?? "").StartsWith("The requested module", StringComparison.Ordinal),
        "java" => error.ExceptionType == "compile error" && JavaSyntaxMessage().IsMatch(error.Message ?? ""),
        "msvc" => error.ErrorCode is "C2143" or "C2146" or "C2059" or "C1075" or "C1004" or "C2061" or "C2760"
            or "CS1002" or "CS1003" or "CS1513" or "CS1001" or "CS1012" or "CS1026" or "CS1525" or "CS1514"
            or "C1083" or "C1189",
        "gcc" => (error.Message ?? "") is var message &&
                 (message.StartsWith("expected ", StringComparison.Ordinal) ||
                  message.EndsWith("No such file or directory", StringComparison.Ordinal) ||
                  message.EndsWith("file not found", StringComparison.Ordinal)),
        _ => false,
    };

    public static FixCandidate CandidateFor(LocalFix fix, SourceFile source, string diff, string howChecked, string? explanation = null) =>
        Build(fix, source, diff, explanation ?? fix.Explanation,
            $"FixFinder made this change to a copy of {Path.GetFileName(source.Path)}, outside your project, and ran it. {howChecked}",
            "the changed copy was run, and printed what was expected");

    private static FixCandidate ToCandidate(LocalFixContext context, LocalFix fix, SourceFile source, string diff)
    {
        var how = Path.GetExtension(source.Path).ToLowerInvariant() switch
        {
            ".py" => "byte-compiled it with py_compile, which runs none of it",
            ".java" => "compiled it with javac",
            ".cs" => "compiled it with the C# compiler and settings dotnet build uses",
            ".js" or ".mjs" or ".cjs" => "checked it with node --check, which parses it and runs none of it",
            ".go" => "built it with go build",
            _ => "compiled it",
        };

        var ranIntoItRunning = !context.FromBuild && !IsCompileError(context.Error) && !IsSyntaxPhase(context.Error) && fix.ResolvesWarning is null;

        var outcome = context.Error.LanguageId == "logic"
            ? "it still compiles. The mistake shows in what the program does rather than as an error, so run it again and check what it prints."
            : ranIntoItRunning
            ? "it still compiles. The crash happens when the program runs, so run it again to confirm."
            : fix.ResolvesWarning is { } warning
                ? $"it compiles without the warning {warning}."
                : "the error is gone.";

        return Build(fix, source, diff, fix.Explanation,
            $"FixFinder made this change to a copy of {Path.GetFileName(source.Path)}, outside your project, and {how}: {outcome}",
            $"a copy with this change was compiled - {outcome.TrimEnd('.')}");
    }

    private static FixCandidate Build(LocalFix fix, SourceFile source, string diff, string explanation, string checkedText, string component)
    {
        var body = new StringBuilder()
            .AppendLine(explanation)
            .AppendLine()
            .AppendLine(checkedText)
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
            LocalFix = fix,
            CheckedBy = checkedText,
        };

        candidate.Score = 100;
        candidate.ScoreComponents =
        [
            new ScoreComponent(
                "Worked out from your code, then checked", 1.0, 1.0,
                $"{fix.RuleId}: {component}"),
        ];

        return candidate;
    }
}
