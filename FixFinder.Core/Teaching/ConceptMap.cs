using System.Text.RegularExpressions;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Teaching;

/// <summary>
/// Which idea a finding is an example of, from what found it: the check's own id, or the error the language reported -
/// its exception type, its compiler's code, its words. A finding that is none of them is taught as reading what went wrong.
/// </summary>
/// <remarks>
/// The rules are read in order and the first that fits decides, so the narrow ones come first: a TypeError about None is
/// a value that is not there before it is a wrong type, and one about a missing argument is about the call. Only what the
/// finding certainly says counts - a crash that only might have been a null pointer, such as a plain segmentation fault, is
/// left as reading what went wrong rather than taught as something it may not be.
/// </remarks>
public static class ConceptMap
{
    /// <summary>One way of recognising a concept's findings: by the check that found them, or by the error reported.</summary>
    private sealed record Rule(Concept Concept)
    {
        /// <summary>The ids of the checks - FixFinder's patterns and analyses - whose findings are this concept.</summary>
        public string[] CheckIds { get; init; } = [];

        /// <summary>Exception types, short or in full: ZeroDivisionError, ArithmeticException, Division_by_zero.</summary>
        public string[] ExceptionTypes { get; init; } = [];

        /// <summary>Compilers' codes: CS0103, C2065.</summary>
        public string[] Codes { get; init; } = [];

        /// <summary>The error's words, when the type or the code alone does not say.</summary>
        public Regex? Message { get; init; }

        public bool Fits(Finding finding)
        {
            if (CheckIds.Contains(finding.RuleId, StringComparer.Ordinal)) return true;
            if (finding.Error is not { } error) return false;
            if (ExceptionTypes.Length == 0 && Codes.Length == 0 && Message is null) return false;

            var typeFits = ExceptionTypes.Length == 0 ||
                           ExceptionTypes.Contains(error.ShortExceptionType ?? "", StringComparer.Ordinal) ||
                           ExceptionTypes.Contains(error.ExceptionType ?? "", StringComparer.Ordinal);
            var codeFits = Codes.Length == 0 || Codes.Contains(error.ErrorCode ?? "", StringComparer.OrdinalIgnoreCase);
            var messageFits = Message is null || Message.IsMatch(error.Message ?? "");

            return typeFits && codeFits && messageFits;
        }
    }

    private static Regex Words(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A Rust panic, by its message - the words rustc 1.98's programs were seen to stop with.</summary>
    private static Rule RustPanic(Concept concept, string message) => new(concept) { ExceptionTypes = ["panic"], Message = Words(message) };

    private static readonly Rule[] Rules =
    [
        // Rust first: its codes - E0425 - and its lints' names are its own, and its panics are told apart by their words.
        new(Concepts.ReturnValues) { Codes = ["E0308"], Message = Words(@"found `\(\)`") },
        new(Concepts.ReturnValues) { Codes = ["unused_must_use"] },
        new(Concepts.UndefinedName) { Codes = ["E0425", "E0433", "E0412", "E0432", "E0599"] },
        new(Concepts.WrongType) { Codes = ["E0308", "E0369", "E0277"] },
        new(Concepts.ChangingAConstant) { Codes = ["E0384", "E0596"] },
        new(Concepts.MissedCase) { Codes = ["E0004"] },
        new(Concepts.Arguments) { Codes = ["E0061"] },
        new(Concepts.ReadBeforeSet) { Codes = ["E0381"] },
        new(Concepts.EndlessRecursion) { Codes = ["unconditional_recursion"] },
        new(Concepts.NeverUsed) { Codes = ["unused_variables", "unused_assignments", "unused_mut", "dead_code", "unused_imports"] },
        new(Concepts.StatementEnd) { ExceptionTypes = ["compile error"], Message = Words(@"^expected `;`") },
        new(Concepts.UnclosedPair) { ExceptionTypes = ["compile error"], Message = Words(@"^mismatched closing delimiter|^unclosed delimiter|^this file contains an unclosed delimiter") },
        RustPanic(Concepts.DivisionByZero, @"^attempt to divide by zero|^attempt to calculate the remainder with a divisor of zero"),
        RustPanic(Concepts.PositionOutOfRange, @"^index out of bounds|^removal index|^insertion index"),
        RustPanic(Concepts.NothingThere, @"Option::unwrap\(\)` on a `None` value"),
        RustPanic(Concepts.TextToNumber, @"Parse(?:Int|Float)Error"),
        RustPanic(Concepts.MissingKey, @"^no entry found for key"),

        // Ruby: nil before names, as a NoMethodError for nil is a value that is not there; then its own errors' words.
        new(Concepts.NothingThere) { ExceptionTypes = ["NoMethodError"], Message = Words(@"for nil\b") },
        new(Concepts.UndefinedName) { ExceptionTypes = ["NoMethodError", "NameError"], Message = Words(@"^undefined (?:local variable or method|method)|^uninitialized constant") },
        new(Concepts.WrongType) { ExceptionTypes = ["TypeError"], Message = Words(@"^no implicit conversion of|can't be coerced into") },
        new(Concepts.Arguments) { ExceptionTypes = ["ArgumentError"], Message = Words(@"^wrong number of arguments") },
        new(Concepts.TextToNumber) { ExceptionTypes = ["ArgumentError"], Message = Words(@"^invalid value for (?:Integer|Float)\(\)") },
        new(Concepts.ChangingAConstant) { ExceptionTypes = ["FrozenError"] },
        new(Concepts.EndlessRecursion) { ExceptionTypes = ["SystemStackError"] },
        new(Concepts.AssignOrCompare) { Message = Words(@"found [`']= literal' in conditional") },
        new(Concepts.NeverUsed) { ExceptionTypes = ["compile warning"], Message = Words(@"^assigned but unused variable") },
        new(Concepts.UnclosedPair) { ExceptionTypes = ["SyntaxError"], Message = Words(@"unterminated (?:quoted )?string") },
        new(Concepts.Blocks) { ExceptionTypes = ["SyntaxError"], Message = Words(@"unexpected end-of-input|expected an `end`|expecting [`']end'") },
        new(Concepts.ReturnValues) { CheckIds = ["logic-ruby-result-discarded", "logic-ruby-each-for-map", "logic-ruby-bang-result-kept", "logic-powershell-function-output-returned"] },

        // PHP: its fatal errors by their words, and the warnings it carries on past.
        new(Concepts.NothingThere) { ExceptionTypes = ["Error"], Message = Words(@"^Call to a member function \w+\(\) on null") },
        new(Concepts.UndefinedName) { ExceptionTypes = ["Error"], Message = Words(@"^Call to undefined (?:function|method)") },
        new(Concepts.UndefinedName) { ExceptionTypes = ["Warning"], Message = Words(@"^Undefined variable \$") },
        new(Concepts.PositionOutOfRange) { ExceptionTypes = ["Warning"], Message = Words(@"^Undefined array key \d+$") },
        new(Concepts.MissingKey) { ExceptionTypes = ["Warning"], Message = Words(@"^Undefined array key """) },
        new(Concepts.DivisionByZero) { ExceptionTypes = ["DivisionByZeroError"] },
        new(Concepts.Arguments) { ExceptionTypes = ["ArgumentCountError"] },
        new(Concepts.WrongType) { ExceptionTypes = ["TypeError"], Message = Words(@"^Unsupported operand types|must be of type") },
        new(Concepts.TextToNumber) { ExceptionTypes = ["Warning"], Message = Words(@"^A non-numeric value encountered") },
        new(Concepts.UnclosedPair) { ExceptionTypes = ["ParseError"], Message = Words(@"^Unclosed '|unexpected end of file") },
        new(Concepts.AssignOrCompare) { CheckIds = ["logic-php-assignment-in-condition"] },
        new(Concepts.ReturnValues) { CheckIds = ["logic-php-sort-result-kept"] },

        // Lua: its errors have no types, so they are told apart by their words alone.
        new(Concepts.UndefinedName) { ExceptionTypes = ["runtime error"], Message = Words(@"^attempt to (?:call|perform arithmetic on|concatenate) a nil value \(global '") },
        new(Concepts.MissingKey) { ExceptionTypes = ["runtime error"], Message = Words(@"^attempt to (?:perform arithmetic on|concatenate) a nil value \(field '") },
        new(Concepts.NothingThere) { ExceptionTypes = ["runtime error"], Message = Words(@"^attempt to (?:index|call|concatenate|perform arithmetic on|get length of) a nil value|^attempt to compare (?:\w+ with nil|nil with \w+)") },
        new(Concepts.DivisionByZero) { ExceptionTypes = ["runtime error"], Message = Words(@"^attempt to (?:divide by zero|perform 'n%%?0')") },
        new(Concepts.WrongType) { ExceptionTypes = ["runtime error"], Message = Words(@"^attempt to add a '(?:string|number)' with|^attempt to perform arithmetic on a (?:string|table) value|^attempt to compare (?:number with string|string with number)|^attempt to concatenate a table value") },
        new(Concepts.UnclosedPair) { ExceptionTypes = ["syntax error"], Message = Words(@"^'(?:end|[)\]}])' expected|^unfinished string") },
        new(Concepts.AssignOrCompare) { ExceptionTypes = ["syntax error"], Message = Words(@"^'(?:then|do)' expected near '='$") },
        new(Concepts.TextToNumber) { CheckIds = ["logic-lua-text-compared-with-number"] },
        new(Concepts.ChangingAConstant) { ExceptionTypes = ["syntax error"], Message = Words(@"^attempt to assign to const variable") },

        // Perl: what stops its programs has no type, so it is told apart by its words; so are its warnings.
        new(Concepts.UndefinedName) { Message = Words(@"^Global symbol ""[$@%]\w+"" requires explicit package name|^Undefined subroutine &|^Name "".*"" used only once: possible typo") },
        new(Concepts.UnclosedPair) { Message = Words(@"^Missing right curly or square bracket") },
        new(Concepts.DivisionByZero) { Message = Words(@"^Illegal (?:division by zero|modulus zero)") },
        new(Concepts.NothingThere) { Message = Words(@"^Can't (?:call method ""\w+"" on an undefined value|use an undefined value as)") },
        new(Concepts.ChangingAConstant) { Message = Words(@"^Can't modify constant item") },
        new(Concepts.AssignOrCompare) { Message = Words(@"^Found = in conditional") },
        new(Concepts.SameValueOrSameObject) { Message = Words(@"^Argument "".*"" isn't numeric in numeric (?:eq|ne|lt|gt|le|ge)") },
        new(Concepts.WrongType) { Message = Words(@"^Argument "".*"" isn't numeric") },
        new(Concepts.ReadBeforeSet) { Message = Words(@"^Use of uninitialized value") },
        new(Concepts.SameValueOrSameObject) { CheckIds = ["logic-perl-text-compared-as-numbers"] },
        new(Concepts.TextToNumber) { CheckIds = ["logic-perl-line-not-chomped"] },
        new(Concepts.WrongType) { CheckIds = ["logic-perl-length-of-array"], Message = Words(@"^length\(\) used on @") },

        // Dart: the analyzer's diagnostics by their names, and the exceptions that stop a program.
        new(Concepts.StatementEnd) { Codes = ["expected_token"], Message = Words(@"^Expected to find ';'") },
        new(Concepts.UnclosedPair) { Codes = ["expected_token"], Message = Words(@"^Expected to find '[)\]}]'") },
        new(Concepts.UndefinedName) { Codes = ["undefined_identifier", "undefined_function", "undefined_method", "undefined_getter"] },
        new(Concepts.WrongType) { Codes = ["invalid_assignment", "argument_type_not_assignable"] },
        new(Concepts.AssignOrCompare) { Codes = ["non_bool_condition"] },
        new(Concepts.ReturnValues) { Codes = ["body_might_complete_normally"] },
        new(Concepts.ChangingAConstant) { Codes = ["assignment_to_final_local"] },
        new(Concepts.Arguments) { Codes = ["not_enough_positional_arguments", "extra_positional_arguments"] },
        new(Concepts.NothingThere) { Codes = ["unchecked_use_of_nullable_value"] },
        new(Concepts.NeverUsed) { Codes = ["unused_local_variable", "unused_import"] },
        new(Concepts.NothingThere) { Message = Words(@"^Null check operator used on a null value") },
        new(Concepts.DivisionByZero) { ExceptionTypes = ["IntegerDivisionByZeroException"] },
        new(Concepts.PositionOutOfRange) { Message = Words(@"^Invalid value: Not in inclusive range|^Index out of range") },
        new(Concepts.TextToNumber) { ExceptionTypes = ["FormatException"] },
        new(Concepts.EndlessRecursion) { Message = Words(@"^Stack Overflow$") },
        new(Concepts.SameValueOrSameObject) { CheckIds = ["logic-dart-lists-compared-with-equals"] },

        // Fortran: gfortran's messages by their words, and what stopped the program it built.
        new(Concepts.UndefinedName) { ExceptionTypes = ["compile error"], Message = Words(@"has no IMPLICIT type") },
        new(Concepts.AssignOrCompare) { ExceptionTypes = ["compile error"], Message = Words(@"^Syntax error in IF-expression") },
        new(Concepts.UnclosedPair) { ExceptionTypes = ["compile error"], Message = Words(@"^Expecting END (?:DO|IF) statement|^Unterminated character constant") },
        new(Concepts.WrongType) { ExceptionTypes = ["compile error"], Message = Words(@"^Cannot convert \S+ to \S+") },
        new(Concepts.Arguments) { ExceptionTypes = ["compile error"], Message = Words(@"^Missing actual argument for argument") },
        new(Concepts.ChangingAConstant) { ExceptionTypes = ["compile error"], Message = Words(@"^Named constant '\w+' in variable definition context|^Dummy argument '\w+' with INTENT\(IN\)") },
        new(Concepts.OffByOne) { ExceptionTypes = [Parsing.Parsers.FortranParser.RuntimeError], Message = Words(@"^Index '-?\d+' of dimension \d+ of array") },
        new(Concepts.OffByOne) { ExceptionTypes = ["compile warning"], Message = Words(@"^Array reference at \(1\) out of bounds") },
        new(Concepts.TextToNumber) { ExceptionTypes = [Parsing.Parsers.FortranParser.RuntimeError], Message = Words(@"^Bad \w+ for item \d+ in list input") },
        new(Concepts.DivisionByZero) { ExceptionTypes = ["SIGFPE"] },
        new(Concepts.ReadBeforeSet) { ExceptionTypes = ["compile warning"], Message = Words(@"may be used uninitialized") },
        new(Concepts.ReturnValues) { ExceptionTypes = ["compile warning"], Message = Words(@"^Return value of function '\w+' at \(1\) not set") },
        new(Concepts.WholeNumberDivision) { CheckIds = ["logic-fortran-whole-number-division"] },
        new(Concepts.UndefinedName) { CheckIds = ["logic-fortran-no-implicit-none"] },
        new(Concepts.ReadBeforeSet) { CheckIds = ["logic-fortran-total-not-started"] },

        // PowerShell: its parser's and its errors' ids, and the logic checks of its scripts.
        new(Concepts.UnclosedPair) { Codes = ["MissingEndCurlyBrace", "TerminatorExpectedAtEndOfString"] },
        new(Concepts.AssignOrCompare) { Codes = ["RedirectionNotSupported", "InvalidEndOfLine"] },
        new(Concepts.UndefinedName) { Codes = ["CommandNotFoundException", "VariableIsUndefined"] },
        new(Concepts.NothingThere) { Codes = ["InvokeMethodOnNull"] },
        new(Concepts.DivisionByZero) { Codes = ["RuntimeException"], Message = Words(@"^Attempted to divide by zero") },
        new(Concepts.TextToNumber) { Codes = ["InvalidCastFromStringToInteger"] },
        new(Concepts.AssignOrCompare) { CheckIds = ["logic-powershell-greater-than-writes-a-file", "logic-powershell-assignment-in-condition"] },
        new(Concepts.Arguments) { CheckIds = ["logic-powershell-function-called-with-brackets"] },

        // Elixir: its errors by their kinds, what it refuses to compile and warns of by its words, and the logic checks of its programs.
        new(Concepts.UndefinedName) { ExceptionTypes = [Parsing.Parsers.ElixirParser.CompileError], Message = Words(@"^undefined (?:variable ""|function )") },
        new(Concepts.UndefinedName) { ExceptionTypes = ["UndefinedFunctionError"] },
        new(Concepts.UnclosedPair) { ExceptionTypes = ["TokenMissingError", "MismatchedDelimiterError"] },
        new(Concepts.Blocks) { ExceptionTypes = ["SyntaxError"], Message = Words(@"^unexpected reserved word: end") },
        new(Concepts.NothingThere) { ExceptionTypes = ["ArithmeticError"], Message = Words(@"^bad argument in arithmetic expression: (?:nil [-+*/]|.+ [-+*/] nil$)") },
        new(Concepts.DivisionByZero) { ExceptionTypes = ["ArithmeticError"], Message = Words(@"^bad argument in arithmetic expression(?:: (?:div|rem)\(.+, 0\))?$") },
        new(Concepts.WrongType) { ExceptionTypes = ["ArithmeticError", "ArgumentError", "Protocol.UndefinedError"] },
        new(Concepts.NothingThere) { ExceptionTypes = ["BadMapError"] },
        new(Concepts.MissedCase) { ExceptionTypes = ["FunctionClauseError", "CaseClauseError"] },
        new(Concepts.ChangingAConstant) { ExceptionTypes = [Parsing.Parsers.ElixirParser.Warning], Message = Words(@"^variable ""\w+"" is unused \(there is a variable with the same name in the context") },
        new(Concepts.NeverUsed) { ExceptionTypes = [Parsing.Parsers.ElixirParser.Warning], Message = Words(@"^variable ""\w+"" is unused \(if the variable|^function \S+ is unused|^this clause for .+ cannot match|^the following clause is redundant") },
        new(Concepts.WrongType) { ExceptionTypes = [Parsing.Parsers.ElixirParser.Warning], Message = Words(@"^using single-quoted strings to represent charlists") },
        new(Concepts.TextToNumber) { CheckIds = ["logic-elixir-input-not-trimmed"] },
        new(Concepts.ReturnValues) { CheckIds = ["logic-elixir-result-discarded", "logic-elixir-each-for-map"] },
        new(Concepts.AssignOrCompare) { CheckIds = ["logic-elixir-assignment-in-condition"] },
        new(Concepts.ChangingAConstant) { CheckIds = ["logic-elixir-new-variable-in-block"] },

        // Erlang: erlc's messages by their words, what stops a program by Erlang's own name for its reason, and the logic checks.
        new(Concepts.UndefinedName) { ExceptionTypes = [Parsing.Parsers.ErlangParser.CompileError], Message = Words(@"^variable '\w+' is unbound|^function \S+ undefined") },
        new(Concepts.UndefinedName) { ExceptionTypes = ["undef"] },
        new(Concepts.StatementEnd) { ExceptionTypes = [Parsing.Parsers.ErlangParser.CompileError], Message = Words(@"^syntax error before: |^head mismatch") },
        new(Concepts.UnclosedPair) { ExceptionTypes = [Parsing.Parsers.ErlangParser.CompileError], Message = Words(@"^unterminated string") },
        new(Concepts.ChangingAConstant) { ExceptionTypes = ["badmatch"] },
        new(Concepts.MissedCase) { ExceptionTypes = ["function_clause", "case_clause", "if_clause"] },
        new(Concepts.WrongType) { ExceptionTypes = ["badarg", "badmap"] },
        new(Concepts.MissingKey) { ExceptionTypes = ["badkey"] },
        new(Concepts.NeverUsed) { ExceptionTypes = [Parsing.Parsers.ErlangParser.CompileWarning], Message = Words(@"^variable '\w+' is unused|^function \S+ is unused|^this clause cannot match because a previous clause") },
        new(Concepts.TextToNumber) { CheckIds = ["logic-erlang-text-compared-with-number", "logic-erlang-input-not-trimmed"] },

        // What a program reads in, used as if it were something else - in seven languages.
        new(Concepts.TextToNumber)
        {
            CheckIds =
            [
                "logic-go-input-not-trimmed", "logic-ruby-input-not-chomped", "logic-php-input-not-trimmed", "logic-c-fgets-newline",
                "logic-rust-input-not-trimmed", "logic-java-nextline-after-nextint", "logic-powershell-read-host-compared-as-text",
            ],
        },
        new(Concepts.ReturnValues) { CheckIds = ["logic-erlang-result-discarded"] },
        new(Concepts.WrongType) { CheckIds = ["logic-erlang-number-joined-to-text", "logic-erlang-list-printed-as-text"] },

        // F#: what it refuses and warns of by its own numbers - FS0039 - what stops a script by .NET's names where F#'s own
        // library gives them, and the logic checks of its scripts. .NET's other exceptions are the rows further down.
        new(Concepts.UndefinedName) { Codes = ["FS0039"] },
        new(Concepts.ChangingAConstant) { Codes = ["FS0027"] },
        new(Concepts.AssignOrCompare) { Codes = ["FS0043"] },
        new(Concepts.AssignOrCompare) { Codes = ["FS0020"], Message = Words(@"If you intended to mutate a value") },
        new(Concepts.ReturnValues) { Codes = ["FS0020", "FS0748"] },
        new(Concepts.Blocks) { Codes = ["FS3123", "FS0589", "FS0010"] },
        new(Concepts.UnclosedPair) { Codes = ["FS0583"] },
        new(Concepts.MissedCase) { Codes = ["FS0025"] },
        new(Concepts.MissedCase) { ExceptionTypes = ["MatchFailureException"] },
        new(Concepts.WrongType) { Codes = ["FS0001", "FS3886"] },
        new(Concepts.PositionOutOfRange) { ExceptionTypes = ["ArgumentException"], Message = Words(@"^The index was outside the range of elements in the list") },
        new(Concepts.NothingThere) { ExceptionTypes = ["ArgumentException"], Message = Words(@"^The input list was empty") },
        new(Concepts.NothingThere) { ExceptionTypes = ["ArgumentNullException"] },
        new(Concepts.AssignOrCompare) { CheckIds = ["logic-fsharp-comparison-for-assignment"] },
        new(Concepts.ReturnValues) { CheckIds = ["logic-fsharp-result-discarded"] },
        new(Concepts.WholeNumberDivision) { CheckIds = ["logic-fsharp-whole-number-division"] },
        new(Concepts.TextToNumber) { CheckIds = ["logic-fsharp-input-compared-as-text"] },
        new(Concepts.OffByOne) { CheckIds = ["logic-fsharp-loop-to-length"] },
        new(Concepts.WrongType) { CheckIds = ["logic-numbers-joined-as-text", "logic-scala-numbers-joined-as-text", "logic-powershell-numbers-joined-as-text"] },

        // A value that is not there, before anything about types or calls: these are what None, null and nil look like.
        new(Concepts.NothingThere)
        {
            CheckIds = ["analysis-null-used", "logic-scala-option-get"],
            ExceptionTypes = ["NullPointerException", "NullReferenceException"],
        },
        new(Concepts.NothingThere) { Message = Words(@"'NoneType' object|NoneType|Cannot read propert(?:y|ies) of (?:undefined|null)|nil pointer dereference|invalid memory address or nil pointer|assignment to entry in nil map|None\.get") },
        new(Concepts.NothingThere) { Codes = ["CS8600", "CS8601", "CS8602", "CS8603", "CS8604", "CS8618", "CS8625"] },

        new(Concepts.DivisionByZero)
        {
            CheckIds = ["analysis-division-by-zero"],
            ExceptionTypes = ["ZeroDivisionError", "DivideByZeroException", "Division_by_zero"],
        },
        new(Concepts.DivisionByZero) { Message = Words(@"/ by zero|integer divide by zero|division by zero|divide.by.zero|Floating point exception|0xC0000094|^\[divzero\]") },

        new(Concepts.ChangingWhileLooping)
        {
            CheckIds =
            [
                "analysis-changed-while-looping", "logic-python-modified-while-looping", "logic-modified-while-looping",
                "logic-remove-while-counting-up", "logic-lua-remove-while-looping-forward",
            ],
            ExceptionTypes = ["ConcurrentModificationException"],
        },
        new(Concepts.ChangingWhileLooping) { Message = Words(@"changed size during iteration|Collection was modified") },

        new(Concepts.MissingKey)
        {
            CheckIds = ["logic-count-from-missing-key"],
            ExceptionTypes = ["KeyError", "KeyNotFoundException", "Not_found"],
        },
        new(Concepts.MissingKey) { ExceptionTypes = ["NoSuchElementException"], Message = Words(@"^key not found") },

        new(Concepts.PositionOutOfRange)
        {
            CheckIds = ["analysis-index-out-of-range", "analysis-empty-collection"],
            ExceptionTypes = ["IndexError", "ArrayIndexOutOfBoundsException", "StringIndexOutOfBoundsException", "IndexOutOfBoundsException", "IndexOutOfRangeException", "ArgumentOutOfRangeException"],
        },
        new(Concepts.PositionOutOfRange) { Message = Words(@"index out of range|index out of bounds|array subscript .* (?:is )?(?:above|outside) array bounds|heap-buffer-overflow|stack-buffer-overflow|global-buffer-overflow|out_of_range|vector::_M_range_check|(?:head|last|tail) of empty list") },

        // OCaml's List.hd and List.tl of an empty list, and List.nth past its end or before its start.
        new(Concepts.PositionOutOfRange) { ExceptionTypes = ["Failure", "Invalid_argument"], Message = Words(@"^(?:hd|tl|nth|List\.nth)$") },

        new(Concepts.EndlessRecursion)
        {
            CheckIds = ["logic-csharp-property-calls-itself"],
            ExceptionTypes = ["RecursionError", "StackOverflowError", "StackOverflowException", "Stack_overflow"],
        },
        new(Concepts.EndlessRecursion) { Message = Words(@"Maximum call stack size exceeded|goroutine stack exceeds|stack overflow") },

        // A call given the wrong arguments, before the wider TypeError and type mismatches it is also reported as.
        new(Concepts.Arguments) { CheckIds = ["analysis-wrong-arguments", "logic-go-replace-none"], Codes = ["CS7036", "CS1501", "CS1503", "ignored-partial-application"] },
        new(Concepts.Arguments) { Message = Words(@"missing \d+ required (?:positional|keyword-only) argument|takes \d+ positional arguments? but \d+ (?:were|was) given|got an unexpected keyword argument|cannot be applied to given types|no suitable (?:method|constructor) found|too (?:few|many) arguments|not enough arguments|missing argument") },

        new(Concepts.TextToNumber)
        {
            CheckIds = ["analysis-not-a-number", "logic-python-input-used-as-number", "logic-csharp-parse-unchecked", "logic-csharp-console-read-number", "logic-go-parse-error-ignored"],
            ExceptionTypes = ["NumberFormatException", "InputMismatchException", "FormatException"],
        },
        new(Concepts.TextToNumber) { Message = Words(@"invalid literal for int\(\)|could not convert string to float|int_of_string|float_of_string") },

        new(Concepts.ReadBeforeSet)
        {
            CheckIds = ["analysis-uninitialised-read", "logic-uninitialised-total", "analysis-unassigned-after-error"],
            ExceptionTypes = ["UnboundLocalError"],
        },
        new(Concepts.ReadBeforeSet) { Codes = ["CS0165", "C4700", "C4701", "C4703"] },
        new(Concepts.ReadBeforeSet) { Message = Words(@"might not have been initialized|is used uninitialized|may be used uninitialized|before initialization") },

        new(Concepts.UndefinedName) { ExceptionTypes = ["NameError"] },
        new(Concepts.UndefinedName) { Codes = ["CS0103", "CS0246", "CS1061", "CS0117", "C2065", "C3861"] },
        new(Concepts.UndefinedName) { ExceptionTypes = ["AttributeError"] },
        new(Concepts.UndefinedName) { Message = Words(@"^cannot find symbol|is not defined|undeclared|was not declared in this scope|use of undeclared identifier|implicit declaration of function|^undefined:|^Not found:|^Unbound (?:value|module|constructor)|does not contain a definition for|is not a member of") },

        new(Concepts.ChangingAConstant) { Codes = ["CS0191", "CS0198", "CS0200"] },
        new(Concepts.ChangingAConstant) { Message = Words(@"cannot assign a value to final variable|Assignment to constant variable|Reassignment to val|assignment of read-only|is not mutable") },

        new(Concepts.WholeNumberDivision) { CheckIds = ["logic-integer-division", "logic-python-floor-division-average", "logic-scala-integer-average", "logic-ocaml-integer-average", "logic-go-integer-average", "logic-rust-integer-average", "logic-ruby-integer-average"] },

        new(Concepts.OffByOne) { CheckIds = ["logic-off-by-one-length", "logic-python-range-skips-last", "logic-scala-range-to-length", "logic-ocaml-for-to-length", "logic-go-loop-to-length", "logic-rust-range-to-length", "logic-ruby-range-to-length", "logic-php-loop-to-count", "logic-lua-loop-from-zero", "logic-perl-loop-to-count", "logic-dart-loop-to-length", "logic-powershell-loop-to-count", "logic-elixir-range-to-length"] },

        new(Concepts.EndlessLoop)
        {
            CheckIds =
            [
                "analysis-loop-never-ends", "analysis-loop-can-get-stuck", "logic-python-loop-never-advances", "logic-loop-never-advances",
                "logic-python-endless-while-true", "logic-loop-steps-away", "timed-out",
            ],
        },

        new(Concepts.AssignOrCompare)
        {
            CheckIds = ["logic-assignment-in-condition", "logic-python-comparison-statement"],
        },
        new(Concepts.AssignOrCompare) { Codes = ["CS0665"] },
        new(Concepts.AssignOrCompare) { Message = Words(@"Maybe you meant '==' or ':='|suggest parentheses around assignment used as truth value|non-boolean condition|expected boolean expression") },

        new(Concepts.SameValueOrSameObject)
        {
            CheckIds =
            [
                "logic-java-string-equals", "logic-c-string-equals", "logic-java-wrapper-equality", "logic-js-loose-equality",
                "logic-python-is-literal", "logic-js-compare-with-new-array", "logic-python-none-comparison", "logic-scala-array-equals",
                "logic-ocaml-physical-equality", "logic-php-search-compared-with-false",
            ],
        },
        new(Concepts.SameValueOrSameObject) { Codes = ["CS0252", "CS0253"] },
        new(Concepts.SameValueOrSameObject) { Message = Words(@"""is"" with (?:a|'\w+') literal|""is not"" with|comparison with string literal") },

        new(Concepts.FallingThrough) { CheckIds = ["logic-switch-fallthrough"], Codes = ["CS0163"] },
        new(Concepts.FallingThrough) { Message = Words(@"^\[fallthrough\]|this statement may fall through") },

        new(Concepts.MissedCase) { ExceptionTypes = ["MatchError", "Match_failure", "SwitchExpressionException"] },
        new(Concepts.MissedCase) { Codes = ["CS8509"] },
        new(Concepts.MissedCase) { Message = Words(@"pattern-matching is not exhaustive|match may not be exhaustive|does not cover all possible input values") },

        new(Concepts.ReturnValues)
        {
            CheckIds =
            [
                "logic-python-returns-nothing-sometimes", "logic-python-print-instead-of-return", "logic-python-return-print",
                "logic-python-none-returned-assigned", "logic-python-result-discarded", "logic-result-discarded", "logic-scala-result-discarded",
                "logic-go-result-discarded",
            ],
            Codes = ["CS0161", "C4715", "C4716", "non-unit-statement"],
        },
        new(Concepts.ReturnValues) { Message = Words(@"^missing return statement|control reaches end of non-void function|no return statement in function returning non-void|^missing return") },

        new(Concepts.NeverUsed)
        {
            Codes =
            [
                "CS0168", "CS0219", "CS8321", "CS0169", "CS0414",
                "unused-var", "unused-var-strict", "unused-value-declaration", "unused-open", "unused-open-bang", "unused-rec-flag", "unused-for-index",
            ],
        },
        new(Concepts.NeverUsed) { Message = Words(@"declared and not used|declared but not used|imported and not used|unused variable|set but not used|defined but not used|is never used|^unused (?:import|local definition|private member)") },

        new(Concepts.StatementEnd) { Codes = ["CS1002"] },
        new(Concepts.StatementEnd) { Message = Words(@"^';' expected|^expected ';'|missing ';' before") },

        new(Concepts.UnclosedPair) { Codes = ["CS1026", "CS1010"] },

        // OCaml: 'Syntax error: ")" expected', said with the bracket that "might be unmatched".
        new(Concepts.UnclosedPair) { Message = Words(@"^Syntax error: ""(?:\)|\]|\}|\|\]|>\})"" expected|might be unmatched") },
        new(Concepts.Blocks) { Message = Words(@"^Syntax error: ""(?:end|done)"" expected") },
        new(Concepts.UnclosedPair) { Message = Words(@"was never closed|unexpected EOF|EOF while scanning|unmatched '[)\]}]'|does not match opening parenthesis|unterminated (?:triple-quoted )?string|EOL while scanning string|^unclosed|^'[)\]}]' expected|^Missing closing brace|missing \) after argument list|Unexpected end of input|unterminated string literal|missing terminating") },

        new(Concepts.Blocks) { ExceptionTypes = ["IndentationError", "TabError"] },
        new(Concepts.Blocks) { Codes = ["CS1513", "CS1514", "C1075"] },
        new(Concepts.Blocks) { Message = Words(@"expected ':'|expected an indented block|unexpected indent|unindent does not match|reached end of file while parsing|^'else' without 'if'|expected declaration or statement at end of input|expected '}' at end of input|this '(?:if|for|while|else)' clause does not guard") },

        new(Concepts.WrongType) { CheckIds = ["analysis-type-mismatch", "analysis-type-hint-broken"], ExceptionTypes = ["TypeError", "ClassCastException", "InvalidCastException"] },
        new(Concepts.WrongType) { Codes = ["CS0029", "CS0266", "CS0019", "CS0023", "C2440", "C2664", "C2446"] },
        new(Concepts.WrongType) { Message = Words(@"^incompatible types|^bad operand types?|invalid conversion from|cannot convert|makes (?:pointer from integer|integer from pointer)|no match for 'operator|invalid operands|cannot use .* as .* value|mismatched types|type mismatch|^Found: |has type[\s\S]*but an expression was expected of type|cannot be compared with == or !=|values of types .* using `==` will always yield|This pattern matches values of type") },
    ];

    /// <summary>The idea a finding is an example of; reading what went wrong when it is none FixFinder teaches.</summary>
    public static Concept Of(Finding finding) => Rules.FirstOrDefault(rule => rule.Fits(finding))?.Concept ?? Concepts.ReadingErrors;
}
