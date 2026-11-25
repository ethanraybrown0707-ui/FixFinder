using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>
/// What a C++ call can change of the variables it is given. A function that takes int&amp; n writes to the caller's
/// variable when it writes to n, though the call - grow(rows) - shows nothing of it. So each variable a call may change
/// is written as the C# reader writes ref rows: as a value the call sets, which is not known. The call is still given
/// the variable - it may read it, or what a pointer in it points at - so that value is made from it.
/// </summary>
/// <remarks>
/// A call may change a variable it is given by name, or an element or member of one, when what it calls takes that
/// argument by a reference it can write through: one of the program's own functions declaring int&amp; there - any of
/// them by that name that could take that many arguments - or a library function known to, as std::swap does both. A
/// call to a function the program does not write and the library is not known to leave alone - one taken from another
/// file, a lambda held in a variable, a pointer to a function - may change everything it is given. A C array is never
/// given another value, whatever the call does with what is in it.
/// </remarks>
internal static class CppReferenceArguments
{
    /// <summary>
    /// Library functions - C's own, and C++'s from std - that take every argument by value, by const reference or by
    /// pointer, so that a variable handed to one by name holds what it held before.
    /// </summary>
    private static readonly HashSet<string> LeaveAlone = new(StringComparer.Ordinal)
    {
        // C: stdio, stdlib, string, ctype, math, time, assert
        "printf", "fprintf", "sprintf", "snprintf", "vprintf", "vfprintf", "vsprintf", "vsnprintf", "puts", "fputs", "putchar", "fputc", "putc",
        "scanf", "sscanf", "fscanf", "getchar", "fgetc", "getc", "fgets", "gets", "ungetc", "fopen", "fclose", "fread", "fwrite", "fflush",
        "fseek", "ftell", "rewind", "feof", "ferror", "perror", "remove", "rename", "malloc", "calloc", "realloc", "free", "exit", "_Exit",
        "abort", "atexit", "atoi", "atol", "atoll", "atof", "strtol", "strtoll", "strtoul", "strtoull", "strtod", "strtof", "strtold", "rand",
        "srand", "qsort", "bsearch", "abs", "labs", "llabs", "div", "ldiv", "system", "getenv", "strlen", "strcpy", "strncpy", "strcat",
        "strncat", "strcmp", "strncmp", "strchr", "strrchr", "strstr", "strtok", "strdup", "strndup", "memset", "memcpy", "memmove", "memcmp",
        "memchr", "strspn", "strcspn", "strpbrk", "strerror", "isdigit", "isalpha", "isalnum", "isspace", "isupper", "islower", "ispunct",
        "isxdigit", "isprint", "iscntrl", "isgraph", "toupper", "tolower", "sqrt", "pow", "exp", "exp2", "log", "log2", "log10", "sin", "cos",
        "tan", "asin", "acos", "atan", "atan2", "sinh", "cosh", "tanh", "floor", "ceil", "round", "trunc", "fabs", "fmod", "hypot", "cbrt",
        "fmin", "fmax", "lround", "llround", "rint", "nearbyint", "copysign", "isnan", "isinf", "isfinite", "signbit", "time", "clock",
        "difftime", "localtime", "gmtime", "strftime", "mktime", "assert",

        // C++: std's algorithms take iterators and values by value; its other helpers take what they read by const reference.
        "min", "max", "minmax", "clamp", "gcd", "lcm", "to_string", "to_wstring", "stoi", "stol", "stoll", "stoul", "stoull", "stof", "stod",
        "stold", "sort", "stable_sort", "partial_sort", "nth_element", "reverse", "rotate", "unique", "fill", "fill_n", "iota", "copy",
        "copy_if", "copy_n", "transform", "accumulate", "reduce", "inner_product", "partial_sum", "adjacent_difference", "count", "count_if",
        "find", "find_if", "find_if_not", "search", "binary_search", "lower_bound", "upper_bound", "equal_range", "max_element",
        "min_element", "minmax_element", "all_of", "any_of", "none_of", "for_each", "replace", "replace_if", "next_permutation",
        "prev_permutation", "equal", "mismatch", "is_sorted", "merge", "make_pair", "make_tuple", "make_shared", "make_unique", "move",
        "forward", "begin", "end", "cbegin", "cend", "rbegin", "rend", "size", "empty", "data", "distance", "next", "prev", "setw",
        "setprecision", "setfill", "fixed", "endl", "get", "ref", "cref", "back_inserter",

        // The reader's own: delete p frees what p points at, and leaves p pointing there.
        "delete",
    };

    /// <summary>
    /// Library functions that do write through some of what they are given: the positions they take by reference. What
    /// std::tie gives is written through in std::tie(low, high) = range, so everything it is given may change.
    /// </summary>
    private static readonly Dictionary<string, int[]> WriteThrough = new(StringComparer.Ordinal)
    {
        ["tie"] = [0, 1, 2, 3, 4, 5, 6, 7],
        ["swap"] = [0, 1],
        ["exchange"] = [0],
        ["getline"] = [1],
        ["advance"] = [0],
        ["shuffle"] = [2],
        ["regex_search"] = [1],
        ["regex_match"] = [1],
        ["from_chars"] = [2],
    };

    public static IrProgram Marked(IrProgram program)
    {
        var own = program.AllFunctions.ToLookup(function => function.Name, StringComparer.Ordinal);
        if (!program.AllFunctions.Any(HasCalls)) return program;

        IrFunction Mark(IrFunction function)
        {
            var arrays = IrWalk.Statements(function.Body).OfType<Declare>().Where(declare => declare.Type.Name == "array")
                .Select(declare => declare.Variable).ToHashSet(StringComparer.Ordinal);
            return new IrRewrite(expression => expression is Call call ? Marked(call, own, arrays) : expression).Function(function);
        }

        return program with
        {
            Functions = program.Functions.Select(Mark).ToList(),
            Classes = program.Classes.Select(type => type.Methods.Count == 0 ? type : type with { Methods = type.Methods.Select(Mark).ToList() }).ToList(),
        };
    }

    private static bool HasCalls(IrFunction function) =>
        IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within).Any(expression => expression is Call { Arguments.Count: > 0 });

    private static Call Marked(Call call, ILookup<string, IrFunction> own, IReadOnlySet<string> arrays)
    {
        List<Argument>? marked = null;
        for (var position = 0; position < call.Arguments.Count; position++)
        {
            var argument = call.Arguments[position];
            if (Changeable(argument.Value, arrays) && MayChange(call, position, own))
            {
                marked ??= [.. call.Arguments];
                var value = argument.Value;
                marked[position] = argument with { Value = new AssignValue(value.Span, value, Opaque.Of(value.Span, "set by the call", value)) };
            }
        }

        return marked is null ? call : call with { Arguments = marked };
    }

    /// <summary>
    /// Whether an argument is something a reference can be bound to and written through: a variable, or an element or
    /// member of one. Not an array, which can never be given another value, nor this, nor anything std holds - std::cin.
    /// </summary>
    private static bool Changeable(Expr argument, IReadOnlySet<string> arrays) => argument switch
    {
        Name { Identifier: var name } => name != "this" && !arrays.Contains(name),
        ElementAccess => true,
        Member { Target: Name { Identifier: "std" } } => false,
        Member => true,
        _ => false,
    };

    private static bool MayChange(Call call, int position, ILookup<string, IrFunction> own)
    {
        var called = call.Callee switch
        {
            Name { Identifier: var name } => name,
            Member { MemberName: var name } => name,
            _ => null,
        };

        // A call through a pointer to a function, or one an expression gives back: what it takes is not known.
        if (called is null) return true;

        if (own[called].ToList() is { Count: > 0 } candidates)
        {
            var fitting = candidates.Where(function => function.Parameters.Count >= call.Arguments.Count ||
                                                       function.Parameters.Any(parameter => parameter.Kind == ParameterKind.Rest)).ToList();
            return (fitting.Count > 0 ? fitting : candidates).Any(function =>
                position < function.Parameters.Count && function.Parameters[position].ByReference);
        }

        // A method of something the library gives - v.push_back(x), s.find(t) - reads what it is given, but for a.swap(b)
        // and std::cin.get(letter).
        if (call.Callee is Member { Target: var receiver } && !IsStd(receiver)) return called is "swap" or "get" && position == 0;

        if (WriteThrough.TryGetValue(called, out var positions)) return positions.Contains(position);
        return !LeaveAlone.Contains(called);
    }

    /// <summary>Whether a call's receiver is std, or a namespace within it: std::swap, std::ranges::sort.</summary>
    private static bool IsStd(Expr receiver) => receiver switch
    {
        Name { Identifier: "std" } => true,
        Member { Target: var outer } => IsStd(outer),
        _ => false,
    };
}
