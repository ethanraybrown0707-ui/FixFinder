using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Two mistakes of the sorts that split a list and sort each part by calling themselves.
/// <para>
/// A quicksort whose parts leave out what equals the pivot: it keeps the values below the pivot - x &lt; pivot - and the values
/// above it - x &gt; pivot - and puts only the pivot itself back between them, so any other value equal to the pivot is never
/// put back. QuixBugs' quicksort is this mistake, in Python and in Java. The change keeps them with the values above it,
/// x &gt;= pivot.
/// </para>
/// <para>
/// And a split in half that stops only when nothing is left - merge sort's, or any function that halves what it is given and
/// calls itself on the halves. Once one item, or one place, is left, the half from the middle on is all of it, and the
/// function calls itself with it again, and so on for ever:
/// </para>
/// <list type="bullet">
/// <item>a list: <c>if len(arr) == 0: return arr</c>, <c>middle = len(arr) // 2</c>, <c>mergesort(arr[middle:])</c> - with one
/// item, middle is 0, and arr[middle:] is that item again. QuixBugs' mergesort is this mistake, in Python and in Java;</item>
/// <item>a count: <c>if (n == 0) return;</c>, <c>mid = n / 2</c>, <c>sort(a + mid, n - mid)</c> - with n 1, n - mid is 1 again;</item>
/// <item>a range with both its ends in it: <c>if (lo &gt; hi) return;</c>, <c>sort(a, lo, mid)</c>, <c>sort(a, mid + 1, hi)</c> - with
/// lo and hi the same, mid is lo, and sort(a, lo, mid) is the call it was made from;</item>
/// <item>a range that ends before hi: <c>if (lo == hi) return;</c>, <c>sort(a, lo, mid)</c>, <c>sort(a, mid, hi)</c> - with hi one
/// more than lo, mid is lo, and sort(a, mid, hi) is the call it was made from.</item>
/// </list>
/// The change stops at one item, or one place, too.
/// </summary>
/// <remarks>
/// The quicksort's mistake is said only when the file shows all of it: two parts keep, from the same list, the items below
/// the pivot and the items above it, each by a strict comparison - a filter, or the if and else if of one loop - and nothing
/// else is drawn from that list against the pivot: no part for what equals it, no else; the pivot is an item of the list -
/// items[0], a pattern's head - and is named again outside the parts, as it is put back; and the function calls itself with
/// both parts, in the same branch, so both are sorted on every pass. A quickselect, which goes on into one part chosen by a
/// test, is left alone. The change is offered when the parts are drawn from the items other than the pivot.
/// <para>
/// The merge sort's is said only when the file shows all of it: the middle is worked out from the list's length, or the two ends, halved and
/// rounded down; the call is given the part from the middle on, every other argument as it came; and each test on the way
/// to the call, read with one item - or one place - left, lets it through, while one of them stops an empty list. A test of
/// anything else on the way to the call, or a call inside a loop, leaves the function alone.
/// </para>
/// </remarks>
public static partial class SortingPatterns
{
    /// <summary>How a language writes a function's parameters: name first, type first, with a $, or Go's names sharing a type.</summary>
    private enum Parameters { NameFirst, TypeFirst, Dollar, GoGroups, PerlCopy, Spaced }

    /// <summary>What a bare list stands for in a test: whether it has anything in it, or always true, or nothing read here.</summary>
    private enum Bare { None, HasItems, Always }

    /// <summary>How one language writes what this check reads.</summary>
    /// <param name="Header">A function's first line - its indentation as "indent", its name as "function", its parameters as "parameters".</param>
    /// <param name="Lengths">How the list's length is asked for, with {x} for the list - and {xb} for its name without a sigil.</param>
    /// <param name="Halves">Halving rounded down, with what is halved as "e".</param>
    /// <param name="Uppers">The part of the list {x} from the middle {m} on, written as a form - {len} is its length, {t} a type.</param>
    /// <param name="Splits">A line splitting the list {x} at {m} into two names, the second as {n}, the part from the middle on.</param>
    /// <param name="Empties">Tests that hold when the list {x} is empty.</param>
    /// <param name="Fills">Tests that hold when the list {x} has anything in it.</param>
    /// <param name="Adds">A line putting {u}, a part of the list, into the list named {n} - right.addAll(arr.subList(middle, arr.size())).</param>
    /// <param name="SlashFloors">Whether / between whole numbers rounds down, as it does in Java, C and Go.</param>
    /// <param name="ColonEnds">Whether a colon ends a test, as Python's does.</param>
    /// <param name="Modifiers">Whether a test may come after what it guards, as Ruby's and Perl's may.</param>
    /// <param name="Words">Whether comparisons are words - -le, -gt - as PowerShell's are.</param>
    /// <param name="Commands">Whether a call is written as a command - Merge-Sort $right - as PowerShell's are.</param>
    /// <param name="Clauses">Whether a function is written as clauses, each for what its parameters match - def merge_sort([]), do: [] -
    /// so the clauses before the one making the calls are its stops; the header's "guard" is what a clause's when says.</param>
    /// <param name="Arms">Whether the stops may be the arms of a match before the one making the calls - | [] -> [] - as F#'s are.</param>
    /// <param name="Juxtaposed">Whether a call gives its arguments one after another, merge_sort left - as F#'s does.</param>
    /// <param name="EqualsCompares">Whether a single = compares in a test, as F#'s does.</param>
    /// <param name="OneItem">The clause, or arm, for one item that the change puts after the one for none - {indent}, {def}, {name}.</param>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex Header, Parameters Parameters,
        string[] Lengths, Regex[] Halves, string[] Uppers, string[]? Splits = null, string[]? Empties = null, string[]? Fills = null,
        string[]? Adds = null, bool SlashFloors = false, bool ColonEnds = false, bool Modifiers = false, bool Words = false, bool Commands = false,
        Bare Bare = Bare.None, bool IgnoreCase = false, bool Clauses = false, bool Arms = false, bool Juxtaposed = false,
        bool EqualsCompares = false, string? OneItem = null);

    private static Regex Halving(string pattern, RegexOptions options = RegexOptions.None) => new($"^{pattern}$", options);

    private static readonly Regex[] Slashed = [Halving(@"(?<e>.+)\s*/\s*2"), Halving(@"(?<e>.*[^>\s])\s*>>>?\s*1")];

    private const string Integer = @"\b(?:int|long|short|size_t|ssize_t|unsigned|signed|Integer|Long|Short|Int|Int32|Int64|nint|uint|ulong|ushort|i8|i16|i32|i64|i128|isize|u8|u16|u32|u64|u128|usize|integer|byte|sbyte)\b";

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-halving-never-stops", CodePattern.Files(".py", ".pyw"), Syntax.Python,
            new(@"^(?<indent>\s*)def\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Parameters.NameFirst,
            ["len( {x} )"], [Halving(@"(?<e>.+)\s*//\s*2"), Halving(@"(?<e>.*[^>\s])\s*>>\s*1"), Halving(@"int\(\s*(?<e>.+)\s*/\s*2\s*\)"), Halving(@"math\.floor\(\s*(?<e>.+)\s*/\s*2\s*\)")],
            ["{x}[ {m} : ]", "{x}[ {m} : {len} ]"], Empties: ["{x} == [ ]", "[ ] == {x}"], Fills: ["{x} != [ ]"],
            Adds: ["{n}.extend( {u} )", "{n} += {u}"], ColonEnds: true, Bare: Bare.HasItems),
        new("logic-java-halving-never-stops", CodePattern.Files(".java"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?!(?:return|new|else|throw|case)\b)(?:(?:public|private|protected|static|final|synchronized)\s+)*(?:<[^>]*>\s+)?[\w.]+(?:\s*<[^()]*>)?(?:\s*\[\s*\])*\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*(?:throws\s+[\w.,\s]+)?\{?\s*$"), Parameters.TypeFirst,
            ["{x}.length", "{x}.size()"], Slashed,
            ["Arrays.copyOfRange( {x} , {m} , {len} )", "java.util.Arrays.copyOfRange( {x} , {m} , {len} )", "{x}.subList( {m} , {len} )",
             "new ArrayList<>( {x}.subList( {m} , {len} ) )", "new ArrayList< {t} >( {x}.subList( {m} , {len} ) )", "new {t}[ {len} - {m} ]"],
            Empties: ["{x}.isEmpty()"], Adds: ["{n}.addAll( {u} );"], SlashFloors: true),
        new("logic-csharp-halving-never-stops", CodePattern.Files(".cs"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?!(?:return|new|else|throw|case|await)\b)(?:(?:public|private|protected|internal|static|async|override|virtual)\s+)*[\w.]+(?:\s*<[^()]*>)?(?:\s*\[\s*\])*\??\s+(?<function>[A-Za-z_]\w*)\s*(?:<[^()]*>)?\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Parameters.TypeFirst,
            ["{x}.Length", "{x}.Count", "{x}.Count()"], Slashed,
            ["{x}[ {m} .. ]", "{x}[ {m} .. {len} ]", "{x}[ {m} .. ^0 ]", "{x}.Skip( {m} ).ToArray()", "{x}.Skip( {m} ).ToList()", "{x}.Skip( {m} )",
             "{x}.GetRange( {m} , {len} - {m} )", "new {t}[ {len} - {m} ]"],
            Empties: ["!{x}.Any()"], Fills: ["{x}.Any()"], Adds: ["{n}.AddRange( {u} );"], SlashFloors: true),
        new("logic-js-halving-never-stops", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:export\s+)?(?:async\s+)?function\s*\*?\s*(?<function>[A-Za-z_$][\w$]*)\s*\((?<parameters>[^)]*)\)|(?:export\s+)?(?:const|let|var)\s+(?<function>[A-Za-z_$][\w$]*)\s*=\s*(?:async\s+)?(?:function\s*)?\((?<parameters>[^)]*)\)\s*(?:=>\s*)?\{|(?:static\s+)?(?!(?:if|for|while|switch|catch|function|return)\b)(?<function>[A-Za-z_$][\w$]*)\s*\((?<parameters>[^)]*)\)\s*\{\s*$)"), Parameters.NameFirst,
            ["{x}.length"],
            [Halving(@"Math\.(?:floor|trunc)\(\s*(?<e>.+)\s*/\s*2\s*\)"), Halving(@"(?<e>.*[^>\s])\s*>>>?\s*1"), Halving(@"\(\s*(?<e>.+)\s*/\s*2\s*\)\s*\|\s*0"), Halving(@"parseInt\(\s*(?<e>.+)\s*/\s*2\s*\)")],
            ["{x}.slice( {m} )", "{x}.slice( {m} , {len} )"], Adds: ["{n}.push( ...{u} );", "{n}.push( ...{u} )"], Bare: Bare.Always),
        new("logic-c-halving-never-stops", CodePattern.Files(".c"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?!(?:return|else|if|while|for|switch)\b)(?:(?:static|inline|extern|const|unsigned|signed|long|short)\s+)*(?:struct\s+)?[A-Za-z_]\w*[\s*]+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Parameters.TypeFirst,
            [], Slashed, [], SlashFloors: true),
        new("logic-cpp-halving-never-stops", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?!(?:return|else|if|while|for|switch)\b)(?:(?:static|inline|extern|const|unsigned|signed|long|short)\s+)*(?:struct\s+)?[A-Za-z_][\w:]*(?:\s*<[^;(){}]*>)?[\s*&]+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*(?:const\s*)?\{?\s*$"), Parameters.TypeFirst,
            ["{x}.size()"], Slashed,
            ["std::vector< {t} >( {x}.begin() + {m} , {x}.end() )", "vector< {t} >( {x}.begin() + {m} , {x}.end() )"],
            Splits: ["std::vector< {t} > {n}( {x}.begin() + {m} , {x}.end() );", "vector< {t} > {n}( {x}.begin() + {m} , {x}.end() );"],
            Empties: ["{x}.empty()"], SlashFloors: true),
        new("logic-go-halving-never-stops", CodePattern.Files(".go"), Syntax.CLike,
            new(@"^(?<indent>\s*)func\s+(?:\([^)]*\)\s*)?(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Parameters.GoGroups,
            ["len( {x} )"], Slashed, ["{x}[ {m} : ]", "{x}[ {m} : {len} ]", "make( []{t} , {len} - {m} )"], SlashFloors: true),
        new("logic-rust-halving-never-stops", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:pub(?:\([^)]*\))?\s+)?fn\s+(?<function>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\((?<parameters>[^)]*)\)"), Parameters.NameFirst,
            ["{x}.len()"], Slashed,
            ["{x}[ {m} .. ]", "&{x}[ {m} .. ]", "&mut {x}[ {m} .. ]", "{x}[ {m} .. ].to_vec()", "&{x}[ {m} .. ].to_vec()", "{x}[ {m} .. {len} ]", "&{x}[ {m} .. {len} ]", "{x}[ {m} .. {len} ].to_vec()"],
            Splits: ["let ( {a} , {n} ) = {x}.split_at( {m} );", "let ( {a} , {n} ) = {x}.split_at_mut( {m} );"], Empties: ["{x}.is_empty()"], SlashFloors: true),
        new("logic-ruby-halving-never-stops", CodePattern.Files(".rb"), Syntax.Python,
            new(@"^(?<indent>\s*)def\s+(?:self\.)?(?<function>[a-z_]\w*[?!]?)\s*(?:\((?<parameters>[^)]*)\)|\s+(?<parameters>[a-z_][\w,\s]*))\s*$"), Parameters.NameFirst,
            ["{x}.length", "{x}.size", "{x}.count"], Slashed,
            ["{x}[ {m} .. ]", "{x}[ {m} .. -1 ]", "{x}[ {m} ... {len} ]", "{x}[ {m} .. {len} - 1 ]", "{x}[ {m} , {len} ]", "{x}[ {m} , {len} - {m} ]", "{x}.drop( {m} )", "{x}.slice( {m} .. -1 )", "{x}.slice( {m} , {len} )"],
            Empties: ["{x}.empty?"], Fills: ["{x}.any?"], SlashFloors: true, Modifiers: true, Bare: Bare.Always),
        new("logic-lua-halving-never-stops", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"^(?<indent>\s*)(?:local\s+)?function\s+(?:[A-Za-z_]\w*[.:])?(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Parameters.NameFirst,
            ["#{x}"], [Halving(@"math\.floor\(\s*(?<e>.+)\s*/\s*2\s*\)"), Halving(@"(?<e>.+)\s*//\s*2"), Halving(@"(?<e>.*[^>\s])\s*>>\s*1")],
            ["{ table.unpack( {x} , {m} + 1 ) }", "{ table.unpack( {x} , {m} + 1 , {len} ) }", "{ unpack( {x} , {m} + 1 ) }", "{ unpack( {x} , {m} + 1 , {len} ) }", "table.move( {x} , {m} + 1 , {len} , 1 , { } )"],
            Empties: ["next( {x} ) == nil"], Fills: ["next( {x} ) ~= nil"], Bare: Bare.Always),
        new("logic-php-halving-never-stops", CodePattern.Files(".php"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|final)\s+)*function\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Parameters.Dollar,
            ["count( {x} )", "sizeof( {x} )"],
            [Halving(@"intdiv\(\s*(?<e>.+)\s*,\s*2\s*\)"), Halving(@"(?:\(int\)\s*)?floor\(\s*(?<e>.+)\s*/\s*2\s*\)"), Halving(@"\(int\)\s*\(\s*(?<e>.+)\s*/\s*2\s*\)"), Halving(@"(?<e>.*[^>\s])\s*>>\s*1")],
            ["array_slice( {x} , {m} )", "array_slice( {x} , {m} , {len} )", "array_slice( {x} , {m} , {len} - {m} )"],
            Empties: ["empty( {x} )", "{x} == [ ]", "{x} === [ ]"], Fills: ["!empty( {x} )"], Bare: Bare.HasItems),
        new("logic-perl-halving-never-stops", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"^(?<indent>\s*)sub\s+(?<function>[A-Za-z_]\w*)\s*(?<parameters>)\{?\s*$"), Parameters.PerlCopy,
            ["scalar( {x} )", "scalar {x}", "{x}"], [Halving(@"int\(\s*(?<e>.+)\s*/\s*2\s*\)"), Halving(@"(?<e>.*[^>\s])\s*>>\s*1")],
            ["@{xb}[ {m} .. $#{xb} ]", "@{xb}[ {m} .. {len} - 1 ]"], Modifiers: true),
        new("logic-powershell-halving-never-stops", CodePattern.Files(".ps1"), Syntax.Python,
            new(@"^(?<indent>\s*)function\s+(?<function>[A-Za-z_][\w-]*)\s*(?:\((?<parameters>[^)]*)\))?", RegexOptions.IgnoreCase), Parameters.Dollar,
            ["{x}.Count", "{x}.Length"], [Halving(@"(?:\[int\]\s*)?\[math\]::(?:Floor|Truncate)\(\s*(?<e>.+)\s*/\s*2\s*\)", RegexOptions.IgnoreCase)],
            ["{x}[ {m} .. ( {len} - 1 ) ]", "@( {x}[ {m} .. ( {len} - 1 ) ] )"], Words: true, Commands: true, IgnoreCase: true),
        new("logic-dart-halving-never-stops", CodePattern.Files(".dart"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?!(?:return|new|else|throw)\b)(?:static\s+)?[\w.]+(?:\s*<[^()]*>)?\??\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Parameters.TypeFirst,
            ["{x}.length"], [Halving(@"(?<e>.+)\s*~/\s*2"), Halving(@"\(\s*(?<e>.+)\s*/\s*2\s*\)\.floor\(\)"), Halving(@"(?<e>.*[^>\s])\s*>>\s*1")],
            ["{x}.sublist( {m} )", "{x}.sublist( {m} , {len} )", "{x}.skip( {m} ).toList()", "{x}.getRange( {m} , {len} ).toList()"],
            Empties: ["{x}.isEmpty"], Fills: ["{x}.isNotEmpty"]),
        new("logic-scala-halving-never-stops", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            new(@"^(?<indent>\s*)(?:(?:private|protected|override|final)\s+)*def\s+(?<function>[A-Za-z_]\w*)\s*(?:\[[^\]]*\])?\s*\((?<parameters>[^)]*)\)"), Parameters.NameFirst,
            ["{x}.length", "{x}.size"], Slashed, ["{x}.drop( {m} )", "{x}.slice( {m} , {len} )", "{x}.takeRight( {len} - {m} )"],
            Splits: ["val ( {a} , {n} ) = {x}.splitAt( {m} )"], Empties: ["{x}.isEmpty", "{x} == Nil", "{x} == List( )"], Fills: ["{x}.nonEmpty"], SlashFloors: true),
        new("logic-fortran-halving-never-stops", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran,
            new(@"^(?<indent>\s*)(?:(?:recursive|pure|elemental)\s+)*(?:subroutine|(?:integer\s+|real\s+)?function)\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)", RegexOptions.IgnoreCase), Parameters.NameFirst,
            ["size( {x} )"], Slashed, ["{x}( {m} + 1 : )", "{x}( {m} + 1 : {len} )"], SlashFloors: true, IgnoreCase: true),
        new("logic-elixir-halving-never-stops", CodePattern.Files(".ex", ".exs"), Syntax.Python,
            new(@"^(?<indent>\s*)(?<def>defp?)\s+(?<function>[a-z_]\w*[?!]?)\s*\((?<parameters>[^)]*)\)(?:\s+when\s+(?<guard>.+?))?\s*(?:,\s*do:.*|\bdo\s*)$"), Parameters.NameFirst,
            ["length( {x} )", "Enum.count( {x} )"], [Halving(@"div\(\s*(?<e>.+)\s*,\s*2\s*\)"), Halving(@"Integer\.floor_div\(\s*(?<e>.+)\s*,\s*2\s*\)")],
            ["Enum.drop( {x} , {m} )", "Enum.slice( {x} , {m} , {len} )"], Splits: ["{ {a} , {n} } = Enum.split( {x} , {m} )"],
            Empties: ["{x} == [ ]", "Enum.empty?( {x} )"], Clauses: true, OneItem: "{indent}{def} {name}([item]), do: [item]"),
        new("logic-erlang-halving-never-stops", CodePattern.Files(".erl"), Syntax.Erlang,
            new(@"^(?<indent>\s*)(?<function>[a-z]\w*)\s*\((?<parameters>[^)]*)\)\s*(?:when\s+(?<guard>.+?))?\s*->"), Parameters.NameFirst,
            ["length( {x} )"], [Halving(@"(?<e>.+)\s+div\s+2")],
            ["lists:nthtail( {m} , {x} )"], Splits: ["{ {a} , {n} } = lists:split( {m} , {x} )"],
            Empties: ["{x} == [ ]", "{x} =:= [ ]"], Clauses: true, OneItem: "{indent}{name}([Item]) -> [Item];"),
        new("logic-fsharp-halving-never-stops", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp,
            new(@"^(?<indent>\s*)let\s+(?:rec\s+)?(?<function>[A-Za-z_]\w*)\s+(?<parameters>(?:\(\s*[A-Za-z_]\w*\s*(?::[^()=]+)?\)\s*|[A-Za-z_]\w*\s+)+)=\s*$"), Parameters.Spaced,
            ["List.length {x}", "{x}.Length", "Array.length {x}"], Slashed,
            ["List.skip {m} {x}", "{x}.[ {m} .. ]"], Splits: ["let {a} , {n} = List.splitAt {m} {x}", "let ( {a} , {n} ) = List.splitAt {m} {x}", "let {a} , {n} = Array.splitAt {m} {x}", "let ( {a} , {n} ) = Array.splitAt {m} {x}"],
            Empties: ["List.isEmpty {x}", "{x}.IsEmpty", "Array.isEmpty {x}", "{x} = [ ]"], SlashFloors: true, Arms: true, Juxtaposed: true, EqualsCompares: true,
            OneItem: "{indent}| [item] -> [item]"),

        // Read for its quicksorts alone: OCaml's own library has no split at a position, so a merge sort's halves are the file's own function's.
        new("logic-ocaml-halving-never-stops", CodePattern.Files(".ml"), Syntax.OCaml,
            new(@"^(?<indent>\s*)let\s+(?:rec\s+)?(?<function>[A-Za-z_]\w*)\s+(?<parameters>(?:\(\s*[A-Za-z_]\w*\s*(?::[^()=]+)?\)\s*|[A-Za-z_]\w*\s+)+)=\s*$"), Parameters.Spaced,
            ["List.length {x}"], [], [], Juxtaposed: true, EqualsCompares: true),
    ];

    /// <summary>Both checks in every language that writes them - made when first asked for, once the tables below are made too.</summary>
    public static IReadOnlyList<ILogicPattern> All => Every.Value;

    private static readonly Lazy<IReadOnlyList<ILogicPattern>> Every = new(() =>
    [
        .. Dialects.Where(dialect => dialect.Halves.Length > 0).Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => Halvings(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
        .. Partitions.Select(partition => (Partition: partition, Dialect: Dialects.Single(dialect => dialect.Id == $"logic-{partition.Language}-halving-never-stops")))
            .Select(each => new CodePattern($"logic-{each.Partition.Language}-partition-drops-equal", each.Dialect.Files, each.Dialect.Syntax,
                (id, source, masked) => PartsLeavingOutEqual(id, source, masked, each.Dialect, each.Partition), Severity.Error, Confidence.Likely)),
    ]);

    /// <summary>A function the file writes: its header's line, its name, its parameters - each name with how it is declared - its body's lines, and the lines of any function written inside it.</summary>
    private sealed record Function(int Header, string Name, List<(string Name, string Declared)> Parameters, List<int> Body, HashSet<int> Inner);

    /// <summary>What is halved: a list's length, a count, or the two ends of a range - with both in it, or the end past it.</summary>
    private enum Shape { List, Count, Inclusive, HalfOpen }

    /// <summary>
    /// The middle, on the line it is worked out on: its name, how it is halved, and what is halved - the list and how its
    /// length was written, the count, or the range's two ends.
    /// </summary>
    private sealed record Middle(int Line, string Name, Shape Shape, string List = "", string Length = "", string Low = "", string High = "");

    /// <summary>What every way of halving in the table has in it: / 2, &gt;&gt; 1, div, intdiv, floor_div, ~/ 2 - a line without one halves nothing.</summary>
    [GeneratedRegex(@"/\s*2|>>>?\s*1|\b(?:div|intdiv|floor_div)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HalvingWritten();

    private static IEnumerable<LogicFinding> Halvings(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (!masked.Any(line => HalvingWritten().IsMatch(line))) yield break;

        foreach (var function in Functions(masked, dialect))
        {
            foreach (var middle in Middles(masked, dialect, function))
            {
                if (Read(id, source, masked, dialect, function, middle) is { } finding)
                {
                    yield return finding;
                    break;
                }
            }
        }
    }

    // ---- The functions of the file ----------------------------------------------------------------------------------

    /// <param name="namedParameters">Whether only a function whose parameters are all names is kept - not a clause whose
    /// parameter is a pattern, quick_sort([pivot | rest]), whose parameters are then given as none.</param>
    private static List<Function> Functions(IReadOnlyList<string> masked, Dialect dialect, bool namedParameters = true)
    {
        // Each line heading a function, read once: the functions written inside others are among them.
        var headers = new Dictionary<int, Match>();
        for (var line = 0; line < masked.Count; line++)
        {
            if (dialect.Header.Match(masked[line]) is { Success: true } header) headers[line] = header;
        }

        var functions = new List<Function>();
        foreach (var (line, header) in headers.OrderBy(pair => pair.Key))
        {
            var indent = header.Groups["indent"].Value.Length;
            var body = new List<int>();
            for (var at = line + 1; at < masked.Count; at++)
            {
                var text = masked[at];
                if (text.Trim().Length == 0) continue;
                var width = CodeText.Indentation(text).Length;

                // A { on a line of its own, under the header, opens the body - as C# writes it.
                if (width == indent && body.Count == 0 && text.Trim() == "{") continue;
                if (width <= indent) break;
                body.Add(at);
            }

            if (body.Count == 0) continue;

            var parameters = ParametersOf(dialect, header.Groups["parameters"].Value, masked, body);
            if (parameters.Count == 0 && namedParameters) continue;

            // The lines of a function written inside this one are its own.
            var inner = new HashSet<int>();
            foreach (var at in body)
            {
                if (inner.Contains(at) || !headers.TryGetValue(at, out var nested)) continue;
                var width = nested.Groups["indent"].Value.Length;
                inner.Add(at);
                foreach (var below in body.Where(below => below > at))
                {
                    if (CodeText.Indentation(masked[below]).Length <= width && masked[below].Trim() is not ("}" or "end")) break;
                    inner.Add(below);
                    if (CodeText.Indentation(masked[below]).Length <= width) break;
                }
            }

            functions.Add(new Function(line, header.Groups["function"].Value, parameters, body, inner));
        }

        return functions;
    }

    /// <summary>Each parameter's name, with the whole of how it is declared - Perl's from the line that copies them out of @_.</summary>
    private static List<(string Name, string Declared)> ParametersOf(Dialect dialect, string written, IReadOnlyList<string> masked, List<int> body)
    {
        if (dialect.Parameters == Parameters.PerlCopy)
        {
            var copied = Regex.Match(masked[body[0]], @"^\s*my\s*(?:\((?<names>[^)]*)\)|(?<names>[@$][A-Za-z_]\w*))\s*=\s*@_\s*;");
            return copied.Success
                ? [.. Regex.Matches(copied.Groups["names"].Value, @"[@$][A-Za-z_]\w*").Select(copiedName => (copiedName.Value, copiedName.Value))]
                : [];
        }

        if (dialect.Parameters == Parameters.Spaced)
        {
            // F#'s names one after another, each on its own or in brackets with its type: items, (items: int list).
            return [.. Regex.Matches(written, @"\(\s*(?<name>[A-Za-z_]\w*)\s*(?::[^()]+)?\)|(?<name>[A-Za-z_]\w*)")
                .Select(parameter => (parameter.Groups["name"].Value, parameter.Value))];
        }

        if (dialect.Commands && written.Trim().Length == 0 && body.Count > 0 &&
            Regex.Match(masked[body[0]], @"^\s*param\s*\((?<names>.*)\)\s*$", RegexOptions.IgnoreCase) is { Success: true } block)
        {
            written = block.Groups["names"].Value;
        }

        var pieces = TopLevel(written);
        var parameters = new List<(string, string)>();
        for (var at = 0; at < pieces.Count; at++)
        {
            var piece = Regex.Replace(pieces[at].Trim(), @"\s*=.*$", "");
            if (piece.Length == 0) continue;

            var name = dialect.Parameters switch
            {
                Parameters.Dollar => Regex.Match(piece, @"\$[A-Za-z_]\w*").Value,
                Parameters.TypeFirst => Regex.Match(Regex.Replace(piece, @"(?:\s*\[[^\]]*\])+\s*$", ""), @"[A-Za-z_]\w*(?=\s*$)").Value,
                _ => Regex.Match(piece, @"^(?:(?:mut|ref|out|in|const|final|var|val)\s+|[*&]+\s*|&\s*mut\s+|\.\.\.)*(?<name>[A-Za-z_$][\w$]*)").Groups["name"].Value,
            };

            // A method's own object comes first and is not given in its calls - self.merge_sort(items[mid:]); a parameter not read leaves the function alone.
            if (at == 0 && name is "self" or "this" or "cls") continue;
            if (name.Length == 0) return [];

            // Go's names share the type written after the last of them: lo, hi int.
            var declared = piece;
            if (dialect.Parameters == Parameters.GoGroups && !piece.Contains(' '))
                declared = pieces.Skip(at + 1).Select(next => next.Trim()).FirstOrDefault(next => next.Contains(' ')) ?? piece;

            parameters.Add((name, declared));
        }

        return parameters;
    }

    /// <summary>The pieces of a list between its commas at the top level.</summary>
    private static List<string> TopLevel(string text)
    {
        var pieces = new List<string>();
        var depth = 0;
        var from = 0;
        for (var at = 0; at < text.Length; at++)
        {
            var c = text[at];
            if (c is '(' or '[' or '{' || c == '<' && at + 1 < text.Length && char.IsLetter(text[at + 1])) depth++;
            else if (c is ')' or ']' or '}' || c == '>' && depth > 0 && !(at > 0 && text[at - 1] is '=' or '-')) depth = Math.Max(0, depth - 1);
            else if (c == ',' && depth == 0)
            {
                pieces.Add(text[from..at]);
                from = at + 1;
            }
        }

        if (text.Trim().Length > 0) pieces.Add(text[from..]);
        return pieces;
    }

    // ---- The middle ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A name given a value: mid = len(arr) // 2, int mid = (lo + hi) / 2;, ArrayList&lt;Integer&gt; right = new ArrayList&lt;Integer&gt;(100);.
    /// The value ends on a letter, not a space, so a long line - a string masked out - is read once, not again for each
    /// space; F#'s and OCaml's in after it is taken off by <see cref="ValueOf"/>.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:(?:let|var|const|final|val|local|my|our|auto|mut|static|integer|int|long|short|unsigned|size_t|usize|isize|i32|i64|u32|u64|Int|Integer|Long|uint)\s+)*(?:(?!(?:return|else|throw|yield|print|echo)\b)[A-Za-z_][\w.:]*(?:\s*<[^=;()]*>)?(?:\s*\[\s*\])*\s+)?(?<mid>[$@]?[A-Za-z_]\w*)\s*(?::\s*[\w<>\[\]]+\s*)?(?::=|=(?![=>~]))\s*(?<right>.*[^;,\s])\s*(?:[;,]\s*)?$")]
    private static partial Regex Assigned();

    /// <summary>The value an assignment gives, without the in that ends F#'s and OCaml's: let mid = (lo + hi) / 2 in.</summary>
    private static string ValueOf(Match assigned) => Regex.Replace(assigned.Groups["right"].Value.Trim(), @"\s+in$", "");

    /// <summary>A new empty list: new ArrayList&lt;Integer&gt;(100), [], list(), new List&lt;int&gt;().</summary>
    [GeneratedRegex(@"^(?:new\s+[\w.]+(?:\s*<[^>]*>)?\s*\(\s*\d*\s*\)|\[\s*\]|list\(\s*\)|array\(\s*\))$")]
    private static partial Regex MadeEmpty();

    private static IEnumerable<Middle> Middles(IReadOnlyList<string> masked, Dialect dialect, Function function)
    {
        var options = Options(dialect);
        var names = function.Parameters.Select(parameter => parameter.Name).ToList();
        var aliases = new Dictionary<string, (string List, string Length)>(dialect.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var line in function.Body)
        {
            if (function.Inner.Contains(line) || Assigned().Match(masked[line]) is not { Success: true } assigned) continue;
            var mid = assigned.Groups["mid"].Value;
            var right = ValueOf(assigned);

            // n = len(arr): a name for the list's length.
            if (LengthOf(right, names, dialect, options) is { } length)
            {
                aliases[mid] = (length, right);
                continue;
            }

            if (Halved(right, dialect, options) is not { } halved) continue;

            if (LengthOf(halved, names, dialect, options) is { } list)
            {
                yield return new Middle(line, mid, Shape.List, list, halved);
            }
            else if (aliases.TryGetValue(halved, out var alias))
            {
                yield return new Middle(line, mid, Shape.List, alias.List, halved);
            }
            else if (names.FirstOrDefault(name => Same(name, halved, dialect)) is { } count && WholeNumbers(dialect, function, masked, [count]))
            {
                yield return new Middle(line, mid, Shape.Count, Low: count);
            }
            else if (Ends(halved, names, dialect) is { } ends && WholeNumbers(dialect, function, masked, [ends.Low, ends.High]))
            {
                yield return new Middle(line, mid, Shape.Inclusive, Low: ends.Low, High: ends.High);
            }
            else if (Offset(right, names, dialect, options) is { } offset && WholeNumbers(dialect, function, masked, [offset.Low, offset.High]))
            {
                yield return new Middle(line, mid, Shape.Inclusive, Low: offset.Low, High: offset.High);
            }
        }

        foreach (var inline in InlineMiddles(masked, dialect, function, names, options)) yield return inline;
    }

    /// <summary>
    /// A middle written where the list is split, with no name of its own - xs.splitAt(xs.length / 2), arr[len(arr) // 2:] -
    /// named by how it is written.
    /// </summary>
    private static IEnumerable<Middle> InlineMiddles(IReadOnlyList<string> masked, Dialect dialect, Function function, List<string> names, RegexOptions options)
    {
        var seen = new HashSet<(string List, string Middle)>();
        foreach (var line in function.Body.Where(line => !function.Inner.Contains(line) && HalvingWritten().IsMatch(masked[line])))
        {
            foreach (var list in names)
            {
                var written = new List<string>();
                foreach (var split in dialect.Splits ?? [])
                {
                    if (SplitLine(split, list, dialect, options).Match(masked[line].TrimEnd()) is { Success: true } splitLine) written.Add(splitLine.Groups["m"].Value);
                }

                foreach (var upper in dialect.Uppers)
                {
                    foreach (Match part in Form(upper, list, dialect, options).Matches(masked[line])) written.Add(part.Groups["m"].Value);
                }

                foreach (var middleText in written.Select(text => Unbracketed(text.Trim())))
                {
                    if (Halved(middleText, dialect, options) is not { } halved || LengthOf(halved, [list], dialect, options) is null) continue;
                    if (seen.Add((list, Spaceless(middleText)))) yield return new Middle(line, middleText, Shape.List, list, halved);
                }
            }
        }
    }

    /// <summary>What is halved, rounded down, by the right side - its own outer brackets taken off - or null.</summary>
    private static string? Halved(string right, Dialect dialect, RegexOptions options)
    {
        foreach (var halving in dialect.Halves)
        {
            if (halving.Match(right) is not { Success: true } halved) continue;
            var halvedText = Unbracketed(halved.Groups["e"].Value.Trim());
            if (Regex.IsMatch(halvedText, @"\+\s*1\s*$")) return null;
            return halvedText;
        }

        return null;
    }

    private static string Unbracketed(string text)
    {
        while (text.StartsWith('(') && Brackets.ClosingParenthesis(text, 0) == text.Length - 1) text = text[1..^1].Trim();
        return text;
    }

    /// <summary>The parameter whose length the text is - len(arr), arr.length - or null.</summary>
    private static string? LengthOf(string text, List<string> names, Dialect dialect, RegexOptions options)
    {
        foreach (var name in names)
        {
            foreach (var form in dialect.Lengths)
            {
                if (Form(form, name, dialect, options, "^", "$").IsMatch(text.Trim())) return name;
            }
        }

        return null;
    }

    /// <summary>The two parameters added together - lo + hi - or null.</summary>
    private static (string Low, string High)? Ends(string text, List<string> names, Dialect dialect)
    {
        if (Regex.Match(text, @"^(?<a>[$@]?[A-Za-z_]\w*)\s*\+\s*(?<b>[$@]?[A-Za-z_]\w*)$") is not { Success: true } sum) return null;
        var first = names.FindIndex(name => Same(name, sum.Groups["a"].Value, dialect));
        var second = names.FindIndex(name => Same(name, sum.Groups["b"].Value, dialect));
        if (first < 0 || second < 0 || first == second) return null;
        return first < second ? (names[first], names[second]) : (names[second], names[first]);
    }

    /// <summary>lo + (hi - lo) / 2, as the two ends - or null.</summary>
    private static (string Low, string High)? Offset(string right, List<string> names, Dialect dialect, RegexOptions options)
    {
        if (Regex.Match(right, @"^(?<low>[$@]?[A-Za-z_]\w*)\s*\+\s*(?<half>.+)$") is not { Success: true } offset) return null;
        if (Halved(Unbracketed(offset.Groups["half"].Value.Trim()), dialect, options) is not { } halved) return null;
        if (Regex.Match(halved, @"^(?<high>[$@]?[A-Za-z_]\w*)\s*-\s*(?<low>[$@]?[A-Za-z_]\w*)$") is not { Success: true } apart) return null;

        var low = offset.Groups["low"].Value;
        if (!Same(low, apart.Groups["low"].Value, dialect)) return null;
        var lowAt = names.FindIndex(name => Same(name, low, dialect));
        var highAt = names.FindIndex(name => Same(name, apart.Groups["high"].Value, dialect));
        return lowAt >= 0 && highAt > lowAt ? (names[lowAt], names[highAt]) : null;
    }

    /// <summary>
    /// Whether the names are whole numbers, so halving them with / rounds down: declared with an integer type where the
    /// language declares one - Fortran's on a line of their own - and taken as given in a language whose / never rounds.
    /// </summary>
    private static bool WholeNumbers(Dialect dialect, Function function, IReadOnlyList<string> masked, IEnumerable<string> names)
    {
        if (!dialect.SlashFloors) return true;
        foreach (var name in names)
        {
            var declared = function.Parameters.First(parameter => Same(parameter.Name, name, dialect)).Declared;
            if (Regex.IsMatch(declared, Integer, Options(dialect))) continue;
            if (dialect.Syntax == Syntax.Fortran &&
                function.Body.Any(line => Regex.IsMatch(masked[line], $@"^\s*integer\b[^:]*::.*(?<![\w%]){Regex.Escape(name)}\b", RegexOptions.IgnoreCase))) continue;
            if (dialect.Id == "logic-ruby-halving-never-stops") continue;
            return false;
        }

        return true;
    }

    // ---- The calls -------------------------------------------------------------------------------------------------

    /// <summary>One of the function's calls to itself: its line, where it is, how it was written, and its arguments.</summary>
    private sealed record SelfCall(int Line, int Start, string Written, List<string> Arguments);

    private static List<SelfCall> SelfCalls(IReadOnlyList<string> masked, Dialect dialect, Function function)
    {
        var options = Options(dialect);
        var calls = new List<SelfCall>();
        var name = Regex.Escape(function.Name);
        foreach (var line in function.Body)
        {
            if (function.Inner.Contains(line)) continue;
            var text = masked[line];
            // merge_sort(...), spread out - ...quickSort(lower) - or a method's call on its own object: self.merge_sort(...), $this->mergeSort(...).
            foreach (Match call in Regex.Matches(text, $@"(?:(?<![\w$.:-])|(?<=(?<![\w$.])\.\.\.)|(?<=(?<![\w$.])(?:self|this)\.)|(?<=\$this->)|(?<=\b(?:self|static)::)){name}\s*\(", options))
            {
                var open = call.Index + call.Length - 1;
                if (Brackets.ClosingParenthesis(text, open) is not { } close) continue;
                var arguments = TopLevel(text[(open + 1)..close]).Select(argument => argument.Trim()).ToList();

                // PowerShell's Merge-Sort($right) gives the one argument in brackets.
                calls.Add(new SelfCall(line, call.Index, text[call.Index..(close + 1)], arguments));
            }

            if (dialect.Juxtaposed)
            {
                // merge_sort left, merge_sort (List.skip mid items): its arguments one after another.
                foreach (Match call in Regex.Matches(text, $@"(?<![\w.']){name}(?=\s+[A-Za-z_(\[\d])", options))
                {
                    var arguments = SpacedArguments(text, call.Index + call.Length, out var end);
                    if (arguments.Count > 0) calls.Add(new SelfCall(line, call.Index, text[call.Index..end], arguments));
                }
            }

            if (!dialect.Commands) continue;

            foreach (Match call in Regex.Matches(text, $@"(?<![\w$.:-]){name}(?=\s+[$(@\d-])", options))
            {
                var arguments = CommandArguments(text, call.Index + call.Length, out var end);
                if (arguments.Count == 0 || arguments.Any(argument => argument.StartsWith('-') && !Regex.IsMatch(argument, @"^-\d"))) continue;
                calls.Add(new SelfCall(line, call.Index, text[call.Index..end], arguments));
            }
        }

        return calls;
    }

    [GeneratedRegex(@"^(?:in|then|else|with|do|done|of|and|or|not|when|match|let|if|elif|fun|function|mod)$")]
    private static partial Regex NotAnArgument();

    /// <summary>
    /// The arguments given one after another - names, numbers, bracketed pieces - up to anything else: an operator, a
    /// bracket the call did not open, or a word such as in or then.
    /// </summary>
    private static List<string> SpacedArguments(string text, int from, out int end)
    {
        var arguments = new List<string>();
        var at = from;
        while (true)
        {
            var start = at;
            while (start < text.Length && text[start] == ' ') start++;
            if (start >= text.Length) break;

            if (text[start] is '(' or '[')
            {
                var close = text[start] == '(' ? Brackets.ClosingParenthesis(text, start) : Brackets.Closing(text, start);
                if (close is not (> 0 and var closing)) break;
                arguments.Add(text[start..(closing + 1)]);
                at = closing + 1;
                continue;
            }

            var word = Regex.Match(text[start..], @"^(?:[A-Za-z_][\w.']*|\d+)");
            if (!word.Success || NotAnArgument().IsMatch(word.Value)) break;
            arguments.Add(word.Value);
            at = start + word.Length;
        }

        end = at;
        return arguments;
    }

    /// <summary>A command's arguments, each a word or a bracketed piece, up to a |, a ; or a bracket it did not open.</summary>
    private static List<string> CommandArguments(string text, int from, out int end)
    {
        var arguments = new List<string>();
        var at = from;
        while (true)
        {
            while (at < text.Length && text[at] == ' ') at++;
            if (at >= text.Length || text[at] is '|' or ';' or ')' or '}' or ']') break;

            var start = at;
            var depth = 0;
            for (; at < text.Length; at++)
            {
                var c = text[at];
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}')
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (depth == 0 && c is ' ' or '|' or ';') break;
            }

            arguments.Add(text[start..at]);
        }

        end = at;
        return arguments;
    }

    // ---- Reading one function --------------------------------------------------------------------------------------

    private static LogicFinding? Read(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, Function function, Middle middle)
    {
        var calls = SelfCalls(masked, dialect, function);
        if (calls.Count == 0) return null;

        var names = function.Parameters.Select(parameter => parameter.Name).ToList();
        var (shape, culprit, given) = middle.Shape switch
        {
            Shape.List => ListCall(masked, dialect, function, middle, calls, names),
            Shape.Count => CountCall(dialect, middle, calls, names),
            _ => RangeCall(dialect, middle, calls, names),
        };
        if (culprit is null) return null;

        // The call must be made each time: not in a loop, not after a ? or an and on its line.
        if (Reaching.InALoop(masked, function.Header, culprit.Line)) return null;
        var before = masked[culprit.Line][..culprit.Start];
        if (Regex.IsMatch(before, @"\?|&&|\|\||\b(?:and|or|if|unless)\b|-(?:and|or)\b", Options(dialect))) return null;

        var tests = Reaching.Tests(masked, function.Body[0], culprit.Line, function.Inner, dialect.ColonEnds, dialect.Modifiers, dialect.IgnoreCase);
        if (shape == Shape.List && (dialect.Clauses || dialect.Arms))
        {
            if (PatternStops(masked, dialect, function, middle, culprit.Line) is not { } patternStops) return null;
            tests.InsertRange(0, patternStops);
        }

        var cases = Cases(shape);
        Reaching.Test? stop = null;
        foreach (var test in tests)
        {
            var related = Put(test.Condition, dialect, middle, shape, cases.Reached[0]) is not null;
            if (!related)
            {
                if (test.Role is Reaching.Role.Inside or Reaching.Role.InElse) return null;
                continue;
            }

            if (test.Role == Reaching.Role.Unread) return null;

            foreach (var reached in cases.Reached)
            {
                if (Passes(test, dialect, middle, shape, reached) is not true) return null;
            }

            if (stop is null && cases.Stopped.All(stopped => Passes(test, dialect, middle, shape, stopped) is false)) stop = test;
        }

        if (stop is null) return null;

        // A clause or an arm is named as it is written; a test by its condition.
        var stopTest = stop.Start < 0 ? stop.Then : source.Lines[stop.Line].Substring(stop.Start, stop.Condition.Length);
        var stopKind = stop.Start >= 0 ? "test" : dialect.Arms ? "arm" : "clause";
        var mid = middle.Name;
        var call = source.Lines[culprit.Line].Substring(culprit.Start, culprit.Written.Length);
        var told = shape switch
        {
            Shape.List => $"The {stopKind} on line {stop.Line + 1}, `{stopTest}`, stops the calls only when `{middle.List}` is empty. Given one item, `{mid}` is 0 " +
                          $"(line {middle.Line + 1}), so `{given}` on line {culprit.Line + 1} is all of it - the same one item - and {function.Name} calls itself " +
                          "with it again, and that call does the same: the calls never stop.",
            Shape.Count => $"The test on line {stop.Line + 1}, `{stopTest}`, stops the calls only when `{middle.Low}` is 0. With `{middle.Low}` 1, `{mid}` is 0 " +
                           $"(line {middle.Line + 1}), so `{call}` on line {culprit.Line + 1} is given 1 again, and that call does the same: the calls never stop.",
            Shape.Inclusive => $"The test on line {stop.Line + 1}, `{stopTest}`, stops the calls only when nothing is left from `{middle.Low}` to `{middle.High}`. " +
                               $"With one place left, `{middle.Low}` and `{middle.High}` the same, `{mid}` is `{middle.Low}` too (line {middle.Line + 1}), so " +
                               $"`{call}` on line {culprit.Line + 1} is the very call it was made from: the calls never stop.",
            _ => $"The test on line {stop.Line + 1}, `{stopTest}`, stops the calls only when nothing is left from `{middle.Low}` up to `{middle.High}`. With one " +
                 $"place left, `{middle.High}` one more than `{middle.Low}`, `{mid}` is `{middle.Low}` (line {middle.Line + 1}), so `{call}` on line " +
                 $"{culprit.Line + 1} is the very call it was made from: the calls never stop.",
        };

        return new LogicFinding(id, stop.Line + 1, told, Change(id, source, masked, dialect, function, middle, shape, stop))
        {
            Severity = Severity.Error,
            Confidence = Confidence.Likely,
        };
    }

    /// <summary>
    /// The clauses before the one making the calls - def merge_sort([]), do: [] - or the arms of a match before the one making
    /// them - | [] -> [] - each as a stop whose condition is what its pattern matches, read as the list's length: [] as none,
    /// [x] as one, [h | t] as one or more. The calling clause's own when is a test the calls are inside. Null when a pattern
    /// says anything else, as what it matches is then not read here.
    /// </summary>
    private static List<Reaching.Test>? PatternStops(IReadOnlyList<string> masked, Dialect dialect, Function function, Middle middle, int callLine)
    {
        var stops = new List<Reaching.Test>();
        var at = function.Parameters.FindIndex(parameter => Same(parameter.Name, middle.List, dialect));
        var length = dialect.Lengths[0].Replace("{x}", middle.List, StringComparison.Ordinal);

        if (dialect.Clauses)
        {
            for (var line = 0; line < function.Header; line++)
            {
                if (dialect.Header.Match(masked[line]) is not { Success: true } clause || !Same(clause.Groups["function"].Value, function.Name, dialect)) continue;
                var patterns = TopLevel(clause.Groups["parameters"].Value).Select(pattern => pattern.Trim()).ToList();
                if (patterns.Count != function.Parameters.Count) continue;
                if (patterns.Where((_, index) => index != at).Any(pattern => !Regex.IsMatch(pattern, @"^[A-Za-z_]\w*$"))) return null;
                if (Matches(patterns[at], length) is not { } condition) return null;

                if (clause.Groups["guard"].Success)
                {
                    // The guard names the list as this clause does: msort(L) when length(L) =< 1.
                    var guard = clause.Groups["guard"].Value;
                    if (Regex.IsMatch(patterns[at], @"^[A-Za-z_]\w*$")) guard = Regex.Replace(guard, $@"(?<![\w$@.]){Regex.Escape(patterns[at])}(?!\w)", middle.List);
                    condition = $"({condition}) and ({guard})";
                }

                stops.Add(new Reaching.Test(line, -1, condition, false, Reaching.Role.Leaves, ClauseText(masked[line])));
            }

            if (dialect.Header.Match(masked[function.Header]) is { Success: true } own && own.Groups["guard"].Success)
                stops.Add(new Reaching.Test(function.Header, -1, own.Groups["guard"].Value, false, Reaching.Role.Inside, ClauseText(masked[function.Header])));
        }

        if (dialect.Arms)
        {
            // match items with, then the arms below it, each starting with |.
            var matching = function.Body.LastOrDefault(line => line < callLine && !function.Inner.Contains(line) &&
                                                               Regex.IsMatch(masked[line], $@"^\s*match\s+{Regex.Escape(middle.List)}\s+with\s*$"), -1);
            if (matching < 0) return stops;

            var arms = function.Body.Where(line => line > matching && line <= callLine && masked[line].TrimStart().StartsWith('|')).ToList();
            if (arms.Count == 0) return stops;
            var armIndent = CodeText.Indentation(masked[arms[0]]).Length;
            arms = arms.Where(line => CodeText.Indentation(masked[line]).Length == armIndent).ToList();

            // Every arm before the one the calls are in.
            foreach (var arm in arms.Take(arms.Count - 1))
            {
                if (Regex.Match(masked[arm], @"^\s*\|\s*(?<pattern>.+?)\s*(?:\bwhen\b(?<guard>.+?))?->") is not { Success: true } written) return null;
                if (written.Groups["guard"].Success || Matches(written.Groups["pattern"].Value, length) is not { } condition) return null;
                stops.Add(new Reaching.Test(arm, -1, condition, false, Reaching.Role.Leaves, masked[arm].Trim()));
            }
        }

        return stops;
    }

    /// <summary>A clause's header as written, without the body it gives on the same line: def merge_sort([]).</summary>
    private static string ClauseText(string line) => Regex.Replace(line.Trim(), @"\s*(?:,\s*do:.*|\bdo\s*|->.*)$", "");

    /// <summary>
    /// What a list pattern matches, as a condition on the list's length: [] none, [a, b] two, [a | []] and a :: [] one,
    /// [h | t] and h :: t one or more, a name or _ anything - or null for any other pattern.
    /// </summary>
    private static string? Matches(string pattern, string length)
    {
        var written = pattern.Trim();

        // [h | t] = list: the list's pattern, beside the name it is given.
        if (Regex.Match(written, @"^(?<list>\[.*\])\s*=\s*[A-Za-z_]\w*$|^[A-Za-z_]\w*\s*=\s*(?<list>\[.*\])$") is { Success: true } named) written = named.Groups["list"].Value;

        const string Item = @"(?:_|[A-Za-z_]\w*)";
        if (Regex.IsMatch(written, $"^{Item}$")) return "true";
        if (Regex.IsMatch(written, @"^\[\s*\]$")) return $"{length} == 0";
        if (Regex.Match(written, $@"^\[\s*(?<items>{Item}(?:\s*[,;]\s*{Item})*)\s*\]$") is { Success: true } fixedItems)
            return $"{length} == {Regex.Split(fixedItems.Groups["items"].Value, @"\s*[,;]\s*").Length}";
        if (Regex.Match(written, $@"^\[\s*(?<items>{Item}(?:\s*,\s*{Item})*)\s*\|\s*(?<tail>\[\s*\]|{Item})\s*\]$") is { Success: true } headAndTail)
        {
            var count = Regex.Split(headAndTail.Groups["items"].Value, @"\s*,\s*").Length;
            return headAndTail.Groups["tail"].Value.StartsWith('[') ? $"{length} == {count}" : $"{length} >= {count}";
        }

        if (Regex.Match(written, $@"^(?<items>{Item}(?:\s*::\s*{Item})*)\s*::\s*(?<tail>\[\s*\]|{Item})$") is { Success: true } consed)
        {
            var count = Regex.Split(consed.Groups["items"].Value, @"\s*::\s*").Length;
            return consed.Groups["tail"].Value.StartsWith('[') ? $"{length} == {count}" : $"{length} >= {count}";
        }

        return null;
    }

    /// <summary>The call given the list's part from the middle on, and that part as written - or no call.</summary>
    private static (Shape, SelfCall?, string) ListCall(IReadOnlyList<string> masked, Dialect dialect, Function function, Middle middle, List<SelfCall> calls, List<string> names)
    {
        var options = Options(dialect);
        var at = names.FindIndex(name => Same(name, middle.List, dialect));
        foreach (var call in calls)
        {
            if (call.Arguments.Count != names.Count) continue;
            if (!names.Select((name, index) => index == at || Same(Spaceless(call.Arguments[index]), name, dialect)).All(same => same)) continue;

            var argument = Unbracketed(call.Arguments[at]);
            if (IsUpper(argument, masked, dialect, function, middle, options)) return (Shape.List, call, call.Arguments[at]);

            // A name given the part from the middle on before the call: right = arr[mid:], or a split's second half.
            if (!Regex.IsMatch(argument, @"^[$@]?[A-Za-z_]\w*$")) continue;
            if (FilledWithUpper(masked, dialect, function, middle, argument, call.Line, options)) return (Shape.List, call, argument);

            foreach (var line in function.Body.Where(line => line < call.Line && !function.Inner.Contains(line)))
            {
                if (Assigned().Match(masked[line]) is { Success: true } assigned && Same(assigned.Groups["mid"].Value, argument, dialect) &&
                    IsUpper(ValueOf(assigned), masked, dialect, function, middle, options))
                {
                    return (Shape.List, call, argument);
                }

                foreach (var split in dialect.Splits ?? [])
                {
                    if (SplitLine(split, middle.List, dialect, options).Match(masked[line].TrimEnd()) is { Success: true } splitLine &&
                        Same(splitLine.Groups["n"].Value, argument, dialect) && IsMiddle(splitLine.Groups["m"].Value, dialect, middle, options))
                    {
                        return (Shape.List, call, argument);
                    }
                }
            }
        }

        return (Shape.List, null, "");
    }

    /// <summary>
    /// Whether the name is made a new, empty list before the call, and then given the part from the middle on and nothing
    /// else - right = new ArrayList&lt;Integer&gt;(100); right.addAll(arr.subList(middle, arr.size())); - so it holds that part.
    /// </summary>
    private static bool FilledWithUpper(IReadOnlyList<string> masked, Dialect dialect, Function function, Middle middle, string name, int callLine, RegexOptions options)
    {
        if (dialect.Adds is not { } adds) return false;
        var lines = function.Body.Where(line => line < callLine && !function.Inner.Contains(line)).ToList();
        var made = lines.Where(line => Assigned().Match(masked[line]) is { Success: true } assigned && Same(assigned.Groups["mid"].Value, name, dialect)).ToList();
        if (made is not [var madeLine] || !MadeEmpty().IsMatch(ValueOf(Assigned().Match(masked[madeLine])))) return false;

        var named = new Regex($@"(?<![\w$@.]){Regex.Escape(name)}(?!\w)", options);
        var given = lines.Where(line => line > madeLine && named.IsMatch(masked[line])).ToList();
        if (given is not [var givenLine]) return false;

        return adds.Any(add => Form(add, middle.List, dialect, options, @"^\s*", @"\s*$").Match(masked[givenLine].TrimEnd()) is { Success: true } adding &&
                               Same(adding.Groups["n"].Value, name, dialect) && IsUpper(adding.Groups["u"].Value.Trim(), masked, dialect, function, middle, options));
    }

    /// <summary>Whether the text is the list's part from the middle on, in one of the language's forms.</summary>
    private static bool IsUpper(string text, IReadOnlyList<string> masked, Dialect dialect, Function function, Middle middle, RegexOptions options)
    {
        foreach (var upper in dialect.Uppers)
        {
            if (Form(upper, middle.List, dialect, options, "^", "$").Match(text) is { Success: true } part && IsMiddle(part.Groups["m"].Value, dialect, middle, options))
                return true;
        }

        return false;
    }

    /// <summary>Whether the text is the middle: its name, or the list's length halved there and then.</summary>
    private static bool IsMiddle(string text, Dialect dialect, Middle middle, RegexOptions options) =>
        Same(Spaceless(Unbracketed(text.Trim())), Spaceless(middle.Name), dialect) ||
        Halved(Unbracketed(text.Trim()), dialect, options) is { } halved && Spaceless(halved) == Spaceless(middle.Length);

    /// <summary>A whole line splitting the list in two, as the form writes it, ending with what may end a line there: , or ;.</summary>
    private static Regex SplitLine(string split, string list, Dialect dialect, RegexOptions options) =>
        Form(split, list, dialect, options, @"^\s*", @"\s*[,;]?\s*$");

    /// <summary>The call given the count less the middle - sort(a + mid, n - mid) - or no call.</summary>
    private static (Shape, SelfCall?, string) CountCall(Dialect dialect, Middle middle, List<SelfCall> calls, List<string> names)
    {
        var at = names.FindIndex(name => Same(name, middle.Low, dialect));
        var less = Spaceless($"{middle.Low}-{middle.Name}");
        foreach (var call in calls)
        {
            if (call.Arguments.Count != names.Count || !SameText(Spaceless(call.Arguments[at]), less, dialect)) continue;
            return (Shape.Count, call, call.Arguments[at]);
        }

        return (Shape.Count, null, "");
    }

    /// <summary>
    /// The call that is the one it was made from once one place is left: sort(a, lo, mid) beside sort(a, mid + 1, hi), or
    /// sort(a, mid, hi) beside sort(a, lo, mid) - or no call.
    /// </summary>
    private static (Shape, SelfCall?, string) RangeCall(Dialect dialect, Middle middle, List<SelfCall> calls, List<string> names)
    {
        var low = names.FindIndex(name => Same(name, middle.Low, dialect));
        var high = names.FindIndex(name => Same(name, middle.High, dialect));
        SelfCall? lower = null, upperFromNext = null, upperFromMid = null;

        foreach (var call in calls)
        {
            if (call.Arguments.Count != names.Count) continue;
            var others = names.Select((name, index) => index == low || index == high || Same(Spaceless(call.Arguments[index]), name, dialect)).All(same => same);
            if (!others) continue;

            var (from, to) = (Spaceless(Unbracketed(call.Arguments[low])), Spaceless(Unbracketed(call.Arguments[high])));
            var lowName = Spaceless(middle.Low);
            var highName = Spaceless(middle.High);
            var mid = Spaceless(middle.Name);
            if (SameText(from, lowName, dialect) && SameText(to, mid, dialect)) lower ??= call;
            else if (SameText(from, $"{mid}+1", dialect) && SameText(to, highName, dialect)) upperFromNext ??= call;
            else if (SameText(from, mid, dialect) && SameText(to, highName, dialect)) upperFromMid ??= call;
            else return (Shape.Inclusive, null, "");
        }

        if (lower is not null && upperFromNext is not null && upperFromMid is null) return (Shape.Inclusive, lower, lower.Written);
        if (lower is not null && upperFromMid is not null && upperFromNext is null) return (Shape.HalfOpen, upperFromMid, upperFromMid.Written);
        return (Shape.Inclusive, null, "");
    }

    // ---- The cases the tests are read in ---------------------------------------------------------------------------

    /// <summary>Values for what is halved: a length, or a range's two ends.</summary>
    private sealed record Values(long Length, long Low = 0, long High = 0);

    /// <summary>The cases where the call is reached, once one item or one place is left - and where the calls should stop, with none.</summary>
    private static (Values[] Reached, Values[] Stopped) Cases(Shape shape) => shape switch
    {
        Shape.List or Shape.Count => ([new Values(1)], [new Values(0)]),
        Shape.Inclusive => ([new Values(0, 0, 0), new Values(0, 5, 5)], [new Values(0, 1, 0), new Values(0, 6, 5)]),
        _ => ([new Values(0, 0, 1), new Values(0, 5, 6)], [new Values(0, 0, 0), new Values(0, 5, 5)]),
    };

    /// <summary>Whether the test lets the call through in the case: true, false, or null when it cannot be read.</summary>
    private static bool? Passes(Reaching.Test test, Dialect dialect, Middle middle, Shape shape, Values values)
    {
        if (Put(test.Condition, dialect, middle, shape, values) is not { } put) return null;
        if (Reaching.Holds(put, dialect.EqualsCompares) is not { } holds) return null;
        if (test.Unless) holds = !holds;
        return test.Role == Reaching.Role.Inside ? holds : !holds;
    }

    /// <summary>
    /// The condition with numbers put in place of what it tests - the list's length, its emptiness, the count, the range's
    /// ends; null when it names none of them; and the condition as it was, so it cannot be read, when one is left in it.
    /// </summary>
    private static string? Put(string condition, Dialect dialect, Middle middle, Shape shape, Values values)
    {
        var options = Options(dialect);
        string Whole(string name) => $@"(?<![\w$@.]){Regex.Escape(name)}(?![\w])";

        if (shape is Shape.Count or Shape.Inclusive or Shape.HalfOpen)
        {
            var named = new[] { middle.Low, middle.High }.Where(name => name.Length > 0).ToList();
            if (!named.Any(name => Regex.IsMatch(condition, Whole(name), options))) return null;
            var put = condition;
            if (shape == Shape.Count) put = Regex.Replace(put, Whole(middle.Low), values.Length.ToString(), options);
            else
            {
                put = Regex.Replace(put, Whole(middle.Low), values.Low.ToString(), options);
                put = Regex.Replace(put, Whole(middle.High), values.High.ToString(), options);
            }

            return put;
        }

        var list = middle.List;
        var bare = list.TrimStart('$', '@');
        var mentions = $@"(?<![\w]){Regex.Escape(bare)}(?![\w])";
        var alias = middle.Length;
        var aliasNamed = Regex.IsMatch(alias, @"^[$@]?[A-Za-z_]\w*$") && !Same(alias, list, dialect);
        if (!Regex.IsMatch(condition, mentions, options) && !(aliasNamed && Regex.IsMatch(condition, Whole(alias), options))) return null;

        var length = values.Length.ToString();
        var text = condition;

        // Not null: a list with an item in it is there.
        text = Regex.Replace(text, $@"{Whole(list)}\s*(?:===?|is)\s*(?:null|nil|None|undefined)\b|\b(?:null|nil|None)\s*===?\s*{Whole(list)}|{Whole(list)}\.nil\?|\bis_null\(\s*{Regex.Escape(list)}\s*\)", "false", options);
        text = Regex.Replace(text, $@"{Whole(list)}\s*(?:!==?|~=|is\s+not)\s*(?:null|nil|None|undefined)\b|\b(?:null|nil|None)\s*!==?\s*{Whole(list)}", "true", options);

        foreach (var empty in dialect.Empties ?? []) text = Form(empty, list, dialect, options).Replace(text, $"({length} == 0)");
        foreach (var filled in dialect.Fills ?? []) text = Form(filled, list, dialect, options).Replace(text, $"({length} != 0)");
        foreach (var form in dialect.Lengths.OrderByDescending(form => form.Length)) text = Form(form, list, dialect, options).Replace(text, length);
        if (aliasNamed) text = Regex.Replace(text, Whole(alias), length, options);

        text = dialect.Bare switch
        {
            Bare.HasItems => Regex.Replace(text, $@"{Whole(list)}(?!\s*[\[.(])", values.Length != 0 ? "true" : "false", options),
            Bare.Always => Regex.Replace(text, $@"{Whole(list)}(?!\s*[\[.(])", "true", options),
            _ => text,
        };

        return text;
    }

    // ---- The change ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The stop's test changed to stop at one item, or one place, as well - offered when what the stop gives back is nothing,
    /// or the list or a parameter as it came, which one item, or one place, needs too.
    /// </summary>
    private static LocalFix? Change(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, Function function, Middle middle, Shape shape, Reaching.Test stop)
    {
        if (stop.Start < 0) return OneItemClause(id, source, masked, dialect, function, middle, stop);

        var givesBack = Regex.Replace(stop.Then, @"\b(?:return|then|end|do)\b|[;{}:,]", " ", Options(dialect)).Trim();
        var parameters = function.Parameters.Select(parameter => parameter.Name).ToList();
        if (givesBack.Length > 0 && !parameters.Any(name => Same(name, givesBack, dialect) || Same(name.TrimStart('@', '$'), givesBack.TrimStart('@', '$'), dialect))) return null;

        if (stop.Role == Reaching.Role.Inside)
        {
            // A test the calls are inside: what the function does without them must give the list back too.
            var after = function.Body.FirstOrDefault(line => line > stop.Line && !function.Inner.Contains(line) &&
                                                             CodeText.Indentation(masked[line]).Length <= CodeText.Indentation(masked[stop.Line]).Length &&
                                                             !Regex.IsMatch(masked[line], @"^\s*(?:\}|end\b)", Options(dialect)), -1);
            if (after >= 0)
            {
                var then = Regex.Replace(masked[after], @"\breturn\b|[;{}]", " ").Trim();
                if (then.Length > 0 && !parameters.Any(name => Same(name, then, dialect))) return null;
            }
        }

        // Whether the condition, as it is to be written, stops the calls when it holds.
        var stopsWhenItHolds = stop.Role != Reaching.Role.Inside;
        if (stop.Unless) stopsWhenItHolds = !stopsWhenItHolds;

        var (le, gt, ge, lt) = dialect.Words ? ("-le", "-gt", "-ge", "-lt") : ("<=", ">", ">=", "<");
        var condition = shape switch
        {
            Shape.List => stopsWhenItHolds ? $"{middle.Length} {le} 1" : $"{middle.Length} {gt} 1",
            Shape.Count => stopsWhenItHolds ? $"{middle.Low} {le} 1" : $"{middle.Low} {gt} 1",
            Shape.Inclusive => stopsWhenItHolds ? $"{middle.Low} {ge} {middle.High}" : $"{middle.Low} {lt} {middle.High}",
            _ => stopsWhenItHolds ? $"{middle.High} - {middle.Low} {le} 1" : $"{middle.High} - {middle.Low} {gt} 1",
        };

        var text = source.Lines[stop.Line];
        var changed = text[..stop.Start] + condition + text[(stop.Start + stop.Condition.Length)..];
        var (title, why) = shape switch
        {
            Shape.List => ($"Stop at one item too: {condition}",
                "One item cannot be halved into two shorter lists, so the calls stop there - and each half of a longer list is shorter than the list it came from."),
            Shape.Count => ($"Stop at one too: {condition}",
                "A count of one cannot be halved into two smaller ones, so the calls stop there - and each half of a larger count is smaller than the count it came from."),
            _ => ($"Stop at one place too: {condition}",
                "A range of one place cannot be halved into two shorter ranges, so the calls stop there - and each half of a longer range is shorter than the range it came from."),
        };

        return LocalFix.ReplaceLine(id, title, why, source.Path, stop.Line + 1, changed);
    }

    /// <summary>
    /// A clause, or an arm, for one item, put after the one for none - offered when that one is written on a line of its own
    /// and matches only an empty list: def merge_sort([]), do: [], merge_sort([]) -> [];, | [] -> [].
    /// </summary>
    private static LocalFix? OneItemClause(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, Function function, Middle middle, Reaching.Test stop)
    {
        if (dialect.OneItem is not { } oneItem || stop.Role != Reaching.Role.Leaves) return null;
        var length = dialect.Lengths[0].Replace("{x}", middle.List, StringComparison.Ordinal);
        if (stop.Condition != $"{length} == 0") return null;

        var text = masked[stop.Line];
        var onOneLine = dialect.Arms ? Regex.IsMatch(text, @"->\s*\S") : dialect.Syntax == Syntax.Erlang ? Regex.IsMatch(text, @"->.*;\s*$") : Regex.IsMatch(text, @",\s*do:\s*\S");
        if (!onOneLine) return null;

        var definedWith = dialect.Header.Match(text) is { Success: true } clause && clause.Groups["def"].Success ? clause.Groups["def"].Value : "";
        var added = oneItem.Replace("{indent}", CodeText.Indentation(source.Lines[stop.Line]), StringComparison.Ordinal)
            .Replace("{def}", definedWith, StringComparison.Ordinal)
            .Replace("{name}", function.Name, StringComparison.Ordinal);
        var kind = dialect.Arms ? "an arm" : "a clause";
        return LocalFix.Insert(id, $"Add {kind} for one item: {added.Trim()}",
            $"With {kind} giving one item back as it is, the calls stop at one item - and each half of a longer list is shorter than the list it came from.",
            source.Path, stop.Line + 2, [added]);
    }

    // ---- A quicksort's parts ---------------------------------------------------------------------------------------

    /// <summary>How one language writes a quicksort's parts, beside its dialect above, named by its id's language.</summary>
    /// <param name="Keeps">The items of the list {s}, each named {v}, for which {c} holds: [x for x in s if c], s.filter(x => c) -
    /// a form with no {v} names each item in {c} itself, as $_, _ or &amp;1, or is the list itself, as Fortran's pack does.</param>
    /// <param name="Walks">A loop's first line going through the list {s}, each item named {v} - or through the positions of the
    /// list {x}, {i} from the number {k}.</param>
    /// <param name="Puts">A line putting the item {v} into the list named {n}.</param>
    /// <param name="Firsts">The list {x}'s first item, beside the list of its other items - x[0] and x[1:] - so that a part
    /// drawn from those leaves the pivot out.</param>
    /// <param name="Counts">Whether a list's positions start at 1, as Lua's and Fortran's do.</param>
    private sealed record Partition(string Language, string[] Keeps, string[] Walks, string[] Puts, (string First, string Others)[] Firsts, bool CountsFromOne = false);

    private const string CountedLoop = "for ( int {i} = {k} ; {i} < {any} ; {i}++ ) {";

    private static readonly Partition[] Partitions =
    [
        new("python", ["[ {v} for {v} in {s} if {c} ]", "filter( lambda {v} : {c} , {s} )"], ["for {v} in {s} :"], ["{n}.append( {v} )", "{n} += [ {v} ]"],
            [("{x}[ 0 ]", "{x}[ 1 : ]"), ("{x}[ -1 ]", "{x}[ : -1 ]")]),
        new("java", ["{s}.stream().filter( {v} -> {c} )"], ["for ( {t} {v} : {s} ) {", CountedLoop], ["{n}.add( {v} );", "{n}[ {any} ] = {v};"],
            [("{x}.get( 0 )", "{x}.subList( 1 , {len} )"), ("{x}[ 0 ]", "Arrays.copyOfRange( {x} , 1 , {len} )")]),
        new("csharp", ["{s}.Where( {v} => {c} )"], ["foreach ( {t} {v} in {s} )", CountedLoop], ["{n}.Add( {v} );", "{n}[ {any} ] = {v};"],
            [("{x}[ 0 ]", "{x}.Skip( 1 ).ToList()"), ("{x}[ 0 ]", "{x}.Skip( 1 ).ToArray()"), ("{x}[ 0 ]", "{x}.Skip( 1 )"), ("{x}[ 0 ]", "{x}[ 1 .. ]"), ("{x}.First()", "{x}.Skip( 1 )")]),
        new("js", ["{s}.filter( ( {v} ) => {c} )", "{s}.filter( {v} => {c} )", "{s}.filter( function ( {v} ) { return {c}; } )"],
            ["for ( const {v} of {s} ) {", "for ( let {v} of {s} ) {", "for ( let {i} = {k} ; {i} < {any} ; {i}++ ) {"], ["{n}.push( {v} );", "{n}.push( {v} )"],
            [("{x}[ 0 ]", "{x}.slice( 1 )")]),
        new("c", [], [CountedLoop, "for ( {i} = {k} ; {i} < {any} ; {i}++ ) {"], ["{n}[ {any} ] = {v};"], [("{x}[ 0 ]", "")]),
        new("cpp", [], ["for ( {t} {v} : {s} ) {", "for ( const {t}& {v} : {s} ) {", CountedLoop], ["{n}.push_back( {v} );", "{n}[ {any} ] = {v};"],
            [("{x}[ 0 ]", "std::vector< {t} >( {x}.begin() + 1 , {x}.end() )")]),
        new("go", [], ["for _ , {v} := range {s} {"], ["{n} = append( {n} , {v} )"], [("{x}[ 0 ]", "{x}[ 1 : ]")]),
        new("rust", ["{s}.iter().filter( |&&{v}| {c} )", "{s}.iter().filter( |&{v}| {c} )", "{s}.iter().filter( |{v}| {c} )", "{s}.into_iter().filter( |&{v}| {c} )", "{s}.into_iter().filter( |{v}| {c} )"],
            ["for {v} in {s} {", "for &{v} in {s}.iter() {"], ["{n}.push( {v} );", "{n}.push( *{v} );"],
            [("{x}[ 0 ]", "&{x}[ 1 .. ]"), ("{x}[ 0 ]", "{x}[ 1 .. ].to_vec()"), ("{x}[ 0 ]", "{x}[ 1 .. ]")]),
        new("ruby", ["{s}.select { | {v} | {c} }", "{s}.filter { | {v} | {c} }"], ["{s}.each do | {v} |", "for {v} in {s}"], ["{n} << {v}", "{n}.push( {v} )"],
            [("{x}[ 0 ]", "{x}[ 1 .. ]"), ("{x}[ 0 ]", "{x}[ 1 .. -1 ]"), ("{x}[ 0 ]", "{x}.drop( 1 )"), ("{x}.first", "{x}.drop( 1 )")]),
        new("lua", [], ["for _ , {v} in ipairs( {s} ) do", "for {i} = {k} , #{x} do"], ["table.insert( {n} , {v} )", "{n}[ #{n} + 1 ] = {v}"],
            [("{x}[ 1 ]", "{ table.unpack( {x} , 2 ) }")], CountsFromOne: true),
        new("php", ["array_filter( {s} , fn( {v} ) => {c} )", "array_filter( {s} , function ( {v} ) use ( {any} ) { return {c}; } )"],
            ["foreach ( {s} as {v} ) {", "for ( {i} = {k} ; {i} < {any} ; {i}++ ) {"], ["{n}[] = {v};", "array_push( {n} , {v} );"],
            [("{x}[ 0 ]", "array_slice( {x} , 1 )")]),
        new("perl", ["grep { {c} } {s}"], ["for my {v} ( {s} ) {", "foreach my {v} ( {s} ) {"], ["push {n} , {v};", "push( {n} , {v} );"], []),
        new("powershell", ["{s} | Where-Object { {c} }", "{s} | ? { {c} }", "{s}.Where( { {c} } )"], ["foreach ( {v} in {s} ) {"], ["{n} += {v}", "{n}.Add( {v} )", "[void]{n}.Add( {v} )"],
            [("{x}[ 0 ]", "{x}[ 1 .. ( {len} - 1 ) ]")]),
        new("dart", ["{s}.where( ( {v} ) => {c} )", "{s}.where( {v} => {c} )"], ["for ( final {v} in {s} ) {", "for ( var {v} in {s} ) {"], ["{n}.add( {v} );"],
            [("{x}[ 0 ]", "{x}.sublist( 1 )"), ("{x}.first", "{x}.skip( 1 )")]),
        new("scala", ["{s}.filter( {c} )", "{s}.filter( {v} => {c} )"], ["for ( {v} <- {s} ) {"], ["{n} += {v}"], [("{x}.head", "{x}.tail"), ("{x}( 0 )", "{x}.drop( 1 )")]),
        new("fortran", ["pack( {s} , {c} )"], [], [], [("{x}( 1 )", "{x}( 2 : )")], CountsFromOne: true),
        new("elixir", ["for {v} <- {s} , {c} , do: {v}", "Enum.filter( {s} , fn {v} -> {c} end )", "Enum.filter( {s} , &( {c} ) )"], [], [], []),
        new("erlang", ["[ {v} || {v} <- {s} , {c} ]"], [], [], []),
        new("fsharp", ["List.filter ( fun {v} -> {c} ) {s}", "[ for {v} in {s} do if {c} then yield {v} ]"], [], [], []),
        new("ocaml", ["List.filter ( fun {v} -> {c} ) {s}"], [], [], []),
    ];

    /// <summary>
    /// One part of a quicksort: the values it keeps, below the pivot or above it - on its line, where its text is there, what
    /// it is drawn from, each item's name and the pivot's, where the comparison's sign is and how it is written, and the list
    /// it is put into, when it is.
    /// </summary>
    private sealed record Part(int Line, int Start, int Length, string Source, string Item, bool Below, string Pivot, int SignAt, string Sign, string? Into, bool Strict, bool Walked = false);

    /// <summary>
    /// The pivot: its name, the list it is an item of, the name a pattern gives the list's other items, and - where it is an
    /// item the table knows, x[0] - how those other items are written: x[1:].
    /// </summary>
    private sealed record Pivot(string Name, string List, string? Others, IReadOnlyList<string> OthersWritten);

    /// <summary>A comparison of two plain sides: x &lt; pivot, $_ -gt $pivot, items[i] &gt;= pivot, items(2:) &lt; pivot, x == pivot.</summary>
    [GeneratedRegex(@"^\s*(?<a>[$@*&]*(?:[A-Za-z_]\w*(?:\[[^\[\]]*\]|\([^()]*\))?|_|&1))\s*(?<sign>===|==|=:=|=<|<=|>=|<(?![<=\-])|>(?![>=])|-(?:lt|gt|le|ge|eq)\b|\.(?:lt|gt|le|ge|eq)\.|=(?![=>]))\s*(?<b>[$@*&]*(?:[A-Za-z_]\w*(?:\[[^\[\]]*\]|\([^()]*\))?|_|&1))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Compared();

    /// <summary>The pivot taken as the list's items are named: pivot, *rest = items; const [pivot, ...rest] = items; my ($pivot, @rest) = @_;.</summary>
    [GeneratedRegex(@"^\s*(?:(?:const|let|var|my|local)\s*)?[\[(]?\s*(?<p>[$]?[A-Za-z_]\w*)\s*,\s*(?:\*|\.\.\.|(?=@))(?<r>[@$]?[A-Za-z_]\w*)\s*[\])]?\s*=\s*(?<x>@_|[A-Za-z_]\w*)\s*;?\s*$")]
    private static partial Regex NamedFirst();

    /// <summary>The pivot as a pattern names it: [pivot | rest], pivot :: rest.</summary>
    [GeneratedRegex(@"\[\s*(?<p>[A-Za-z_]\w*)\s*\|\s*(?<r>[A-Za-z_]\w*)\s*\]|^\s*(?:\||case)\s*(?<p>[A-Za-z_]\w*)\s*::\s*(?<r>[A-Za-z_]\w*)\s*(?:->|=>)")]
    private static partial Regex PatternFirst();

    /// <summary>An item of a list: items[0], items.get(0), items(1), items.head, items.First().</summary>
    [GeneratedRegex(@"^(?<x>[$@]?[A-Za-z_]\w*)\s*(?:\[\s*[^\[\]]+\]|\(\s*[^()]+\)|\.(?:get|at|First|Last|ElementAt)\s*\([^()]*\)|\.(?:first|last|head|First|Last))$")]
    private static partial Regex ItemOf();

    /// <summary>A type's arguments, List&lt;int&gt; and Dictionary&lt;string, int&gt; - not a comparison.</summary>
    [GeneratedRegex(@"<\s*[\w.?]+(?:\s*\[\s*\])?(?:\s*,\s*[\w.?]+(?:\s*\[\s*\])?)*\s*>")]
    private static partial Regex TypeArguments();

    /// <summary>
    /// Whether some name is compared both ways in the lines - something below it, and something above it - as a quicksort's
    /// pivot is: x &lt; pivot and x &gt; pivot, $_ -lt $pivot and $_ -gt $pivot. Read by looking at each sign and the names beside it.
    /// </summary>
    private static bool ComparedBothWays(IEnumerable<string> lines)
    {
        var below = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var above = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var written in lines)
        {
            var line = written.Contains('<') && written.Contains('>') ? TypeArguments().Replace(written, " ") : written;
            for (var at = 0; at < line.Length; at++)
            {
                var length = StrictSignAt(line, at);
                if (length == 0) continue;

                var less = line[at] == '<' || line[at] is '-' or '.' && char.ToLowerInvariant(line[at + 1]) == 'l';
                // x < pivot keeps x below pivot - and pivot above x.
                if (NameAfter(line, at + length) is { } after) (less ? below : above).Add(after);
                if (NameBefore(line, at) is { } before) (less ? above : below).Add(before);
                at += length - 1;
            }
        }

        return below.Overlaps(above);
    }

    /// <summary>How long the strict comparison's sign at the place is - &lt;, &gt;, -lt, -gt, .lt., .gt. - or 0 where there is none.</summary>
    private static int StrictSignAt(string line, int at)
    {
        var c = line[at];
        var before = at > 0 ? line[at - 1] : ' ';
        var after = at + 1 < line.Length ? line[at + 1] : ' ';
        if (c == '<') return before is '<' or '=' or '>' or '-' || after is '<' or '=' or '>' or '-' ? 0 : 1;
        if (c == '>') return before is '=' or '>' or '-' || after is '>' or '=' ? 0 : 1;
        if (c is '-' or '.' && at + 2 < line.Length && char.ToLowerInvariant(line[at + 1]) is 'l' or 'g' && char.ToLowerInvariant(line[at + 2]) == 't')
        {
            if (c == '.') return at + 3 < line.Length && line[at + 3] == '.' ? 4 : 0;
            return at + 3 >= line.Length || !char.IsLetterOrDigit(line[at + 3]) ? 3 : 0;
        }

        return 0;
    }

    /// <summary>The name just after the place, past spaces - or null.</summary>
    private static string? NameAfter(string line, int at)
    {
        while (at < line.Length && line[at] == ' ') at++;
        var start = at;
        while (at < line.Length && (char.IsLetterOrDigit(line[at]) || line[at] is '_' or '$' or '@')) at++;
        return at > start ? line[start..at] : null;
    }

    /// <summary>The name just before the place, past spaces and a closing bracket - items[i] &lt;, items(2:) &lt; - or null.</summary>
    private static string? NameBefore(string line, int at)
    {
        var end = at;
        while (end > 0 && line[end - 1] is ' ' or ')' or ']' or ':') end--;
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] is '_' or '$' or '@')) start--;
        return end > start ? line[start..end] : null;
    }

    private static IEnumerable<LogicFinding> PartsLeavingOutEqual(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, Partition partition)
    {
        if (!ComparedBothWays(masked)) yield break;
        var options = Options(dialect);
        foreach (var function in Functions(masked, dialect, namedParameters: false))
        {
            if (!ComparedBothWays(function.Body.Where(line => !function.Inner.Contains(line)).Select(line => masked[line]))) continue;
            var pivots = Pivots(masked, dialect, partition, function, options);
            if (pivots.Count == 0) continue;

            var parts = KeptParts(masked, dialect, partition, function, pivots, options).Concat(WalkedParts(masked, dialect, partition, function, pivots, options)).ToList();
            foreach (var below in parts.Where(part => part.Below && part.Strict))
            {
                var above = parts.FirstOrDefault(part => !part.Below && part.Strict && SameText(part.Source, below.Source, dialect) && Same(part.Pivot, below.Pivot, dialect));
                if (above is null) continue;

                // Nothing else drawn from the same list against the same pivot: no part for what equals it, nor an else.
                if (parts.Count(part => SameText(part.Source, below.Source, dialect) && Same(part.Pivot, below.Pivot, dialect)) != 2) continue;
                // Both parts sorted on every pass, in the same branch: a quickselect goes on into one of them, chosen by a test.
                if (SortedAgain(masked, dialect, function, below) is not { } belowSorted || SortedAgain(masked, dialect, function, above) is not { } aboveSorted) continue;
                if (!SameBranch(masked, function, belowSorted, aboveSorted)) continue;
                if (!PutBack(masked, dialect, function, below, above, options)) continue;

                yield return Dropped(id, source, masked, dialect, partition, pivots.First(pivot => Same(pivot.Name, below.Pivot, dialect)), below, above, options);
                break;
            }
        }
    }

    /// <summary>Each name the function gives an item of its list - pivot = items[0], pivot, *rest = items, [pivot | rest].</summary>
    private static List<Pivot> Pivots(IReadOnlyList<string> masked, Dialect dialect, Partition partition, Function function, RegexOptions options)
    {
        var pivots = new List<Pivot>();
        var lists = function.Parameters.Select(parameter => parameter.Name).ToList();
        foreach (var line in new[] { function.Header }.Concat(function.Body.Where(line => !function.Inner.Contains(line))))
        {
            var text = masked[line];
            if ((text.Contains('|') || text.Contains("::", StringComparison.Ordinal)) && PatternFirst().Match(text) is { Success: true } pattern)
                pivots.Add(new Pivot(pattern.Groups["p"].Value, "", pattern.Groups["r"].Value, []));
            else if (line != function.Header && text.Contains(',') && text.Contains('=') && NamedFirst().Match(text) is { Success: true } named && (named.Groups["x"].Value == "@_" || lists.Any(list => Same(list, named.Groups["x"].Value, dialect))))
                pivots.Add(new Pivot(named.Groups["p"].Value, named.Groups["x"].Value, named.Groups["r"].Value, []));
            else if (line != function.Header && text.Contains('=') && Assigned().Match(text) is { Success: true } assigned &&
                     ItemOf().Match(ValueOf(assigned)) is { Success: true } item && lists.Any(list => Same(list, item.Groups["x"].Value, dialect)))
            {
                var list = item.Groups["x"].Value;
                var othersWritten = partition.Firsts.Where(pair => Form(pair.First, list, dialect, options, "^", "$").IsMatch(ValueOf(assigned)))
                    .Select(pair => pair.Others).ToList();
                pivots.Add(new Pivot(assigned.Groups["mid"].Value, list, null, othersWritten));
            }
        }

        return pivots;
    }

    /// <summary>The parts a filter keeps: [x for x in rest if x &lt; pivot], rest.filter((x) =&gt; x &gt; pivot).</summary>
    private static IEnumerable<Part> KeptParts(IReadOnlyList<string> masked, Dialect dialect, Partition partition, Function function, List<Pivot> pivots, RegexOptions options)
    {
        foreach (var line in function.Body.Where(line => !function.Inner.Contains(line)))
        {
            var text = masked[line];
            foreach (var keep in partition.Keeps)
            {
                foreach (Match kept in Form(keep, "", dialect, options).Matches(text))
                {
                    var items = kept.Groups["v"].Captures.Select(capture => capture.Value).Distinct().ToList();
                    if (items.Count > 1) continue;
                    var source = kept.Groups["s"].Value.Trim();
                    var named = items.Count == 1 ? items[0] : null;
                    if (Comparison(kept.Groups["c"], named, source, pivots, dialect) is not { } compared) continue;

                    // Given a name on its line: lower = [x for x in rest if x < pivot], with whatever wraps it.
                    var into = Assigned().Match(text) is { Success: true } assigned && assigned.Groups["right"].Index <= kept.Index ? assigned.Groups["mid"].Value : null;
                    yield return new Part(line, kept.Index, kept.Length, source, compared.Item, compared.Below, compared.Pivot, compared.SignAt, compared.Sign, into, compared.Strict);
                }
            }
        }
    }

    /// <summary>A comparison of an item with a pivot, which way it keeps the item, and where its sign is.</summary>
    private sealed record Comparing(string Item, string Pivot, bool Below, bool Strict, int SignAt, string Sign);

    /// <summary>
    /// The condition read as a comparison of the item with a pivot - x &lt; pivot, pivot &gt; x, $_ -gt $pivot - or null. With no
    /// item named, the item is $_, _ or &amp;1, or the list itself, as pack(items(2:), items(2:) &lt; pivot) compares it.
    /// </summary>
    private static Comparing? Comparison(Group condition, string? named, string source, List<Pivot> pivots, Dialect dialect)
    {
        if (Compared().Match(condition.Value) is not { Success: true } compared) return null;
        var (first, second) = (compared.Groups["a"].Value, compared.Groups["b"].Value);
        bool IsPivot(string side) => pivots.Any(pivot => Same(pivot.Name, side, dialect));
        bool IsItem(string side) => named is not null
            ? Same(side.TrimStart('*', '&'), named.TrimStart('*', '&'), dialect)
            : side is "$_" or "_" or "&1" || Same(Spaceless(side), Spaceless(source), dialect);

        var sign = compared.Groups["sign"].Value.ToLowerInvariant();
        var less = sign is "<" or "<=" or "=<" or "-lt" or "-le" or ".lt." or ".le.";
        var strict = sign is "<" or ">" or "-lt" or "-gt" or ".lt." or ".gt.";
        var at = condition.Index + compared.Groups["sign"].Index;

        if (IsItem(first) && IsPivot(second)) return new Comparing(first, second, less, strict, at, compared.Groups["sign"].Value);
        if (IsPivot(first) && IsItem(second)) return new Comparing(second, first, !less, strict, at, compared.Groups["sign"].Value);
        return null;
    }

    /// <summary>
    /// The parts a loop puts items into: for x in rest: if x &lt; pivot: lower.append(x) elif x &gt; pivot: higher.append(x) - the
    /// two branches of one test, each putting the item into a list; an else after them is a part of its own, for what equals the pivot.
    /// </summary>
    private static IEnumerable<Part> WalkedParts(IReadOnlyList<string> masked, Dialect dialect, Partition partition, Function function, List<Pivot> pivots, RegexOptions options)
    {
        foreach (var header in function.Body.Where(line => !function.Inner.Contains(line)))
        {
            foreach (var walk in partition.Walks)
            {
                if (Form(walk, "", dialect, options, @"^\s*", @"\s*$").Match(masked[header].TrimEnd()) is not { Success: true } walked) continue;

                var lines = BlockBelow(masked, header, function.Body);
                if (lines.Count == 0) continue;

                // Through positions: the item is items[i] where it is used - or the name the loop's first line gives it, local x = items[i].
                string item, source;
                var bindingLines = 0;
                if (walked.Groups["i"].Success)
                {
                    var position = walked.Groups["i"].Value;
                    var list = walked.Groups["x"].Success ? walked.Groups["x"].Value : null;
                    item = $"[{position}]";
                    if (Assigned().Match(masked[lines[0]]) is { Success: true } named &&
                        Regex.Match(ValueOf(named), $@"^(?<x>[A-Za-z_]\w*)\s*\[\s*{Regex.Escape(position)}\s*\]$") is { Success: true } read)
                    {
                        item = named.Groups["mid"].Value;
                        list ??= read.Groups["x"].Value;
                        bindingLines = 1;
                    }

                    source = $"{list ?? "?"} from {walked.Groups["k"].Value}";
                }
                else
                {
                    item = walked.Groups["v"].Value;
                    source = walked.Groups["s"].Value.Trim();
                }

                foreach (var part in Branches(masked, dialect, lines.Skip(bindingLines).ToList(), item, source, pivots, partition, options)) yield return part;
            }
        }
    }

    /// <summary>The lines of the block a line heads, by indentation - after a { on a line of its own, up to the } closing it.</summary>
    private static List<int> BlockBelow(IReadOnlyList<string> masked, int header, List<int> body)
    {
        var indent = CodeText.Indentation(masked[header]).Length;
        var lines = new List<int>();
        foreach (var line in body.Where(line => line > header))
        {
            var width = CodeText.Indentation(masked[line]).Length;
            if (lines.Count == 0 && width == indent && masked[line].Trim() == "{") continue;
            if (width <= indent) break;
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>The parts the loop's one test makes: its if and else if branches, each putting the item into a list - and an else, as a part for the rest.</summary>
    private static List<Part> Branches(IReadOnlyList<string> masked, Dialect dialect, List<int> lines, string item, string source, List<Pivot> pivots, Partition partition, RegexOptions options)
    {
        var parts = new List<Part>();
        if (lines.Count == 0) return parts;

        // A branch not read as one of the two - an else, or a test of anything else - keeps what they leave: a part of its own.
        var others = new List<(int Line, int Start, int Length)>();
        var top = CodeText.Indentation(masked[lines[0]]).Length;
        foreach (var line in lines.Where(line => CodeText.Indentation(masked[line]).Length == top))
        {
            var text = masked[line];
            var branch = Regex.Match(text, @"^\s*(?:\}\s*)?(?<word>else\s+if|elseif|elsif|elif|if|else)\b", options);
            if (!branch.Success) continue;

            if (branch.Groups["word"].Value.Equals("else", StringComparison.OrdinalIgnoreCase) ||
                ReadBranch(masked, dialect, lines, line, top, branch, item, source, pivots, partition, options) is not { } part)
            {
                others.Add((line, branch.Index, branch.Length));
                continue;
            }

            parts.Add(part);
        }

        // Each other branch draws from the same list as the two do, for every pivot.
        var drawnFrom = parts.Count > 0 ? parts[0].Source : source;
        foreach (var (line, start, length) in others)
            parts.AddRange(pivots.Select(pivot => new Part(line, start, length, drawnFrom, item, false, pivot.Name, -1, "", null, false, Walked: true)));
        return parts;
    }

    /// <summary>One branch of the loop's test as a part: a comparison of the item with a pivot, and one line putting the item into a list - or null.</summary>
    private static Part? ReadBranch(IReadOnlyList<string> masked, Dialect dialect, List<int> lines, int line, int top, Match branch, string item, string source, List<Pivot> pivots, Partition partition, RegexOptions options)
    {
        var text = masked[line];
        if (Reaching.Written(text, branch.Index + branch.Length, dialect.ColonEnds) is not { } wording) return null;
        var condition = new Regex(Regex.Escape(wording.Condition)).Match(text, wording.Start);
        if (!condition.Success) return null;

        // The item as the comparison reads it: x, or items[i] for a loop through positions - whose list is then the one named there.
        var named = item.StartsWith('[') ? Regex.Match(wording.Condition, $@"[A-Za-z_]\w*\s*\[\s*{Regex.Escape(item[1..^1])}\s*\]").Value : item;
        if (named.Length == 0) return null;
        var drawnFrom = source.StartsWith("? from ", StringComparison.Ordinal) ? named[..named.IndexOf('[')].Trim() + source[1..] : source;
        if (Comparison(condition, named, drawnFrom, pivots, dialect) is not { } compared) return null;

        // What the branch does: one line putting the item, as written, into a list - on the test's own line or the one under it.
        var does = wording.Rest.Length > 0 ? wording.Rest : lines.SkipWhile(each => each <= line).TakeWhile(each => CodeText.Indentation(masked[each]).Length > top)
            .Select(each => masked[each].Trim()).FirstOrDefault(each => each is not ("{" or "}")) ?? "";
        var into = partition.Puts.Select(put => Form(put.Replace("{v}", named, StringComparison.Ordinal), "", dialect, options, @"^\s*", @"\s*(?:\}\s*)?$").Match(does.Trim()))
            .FirstOrDefault(put => put.Success);
        if (into is null) return null;

        return new Part(line, condition.Index, condition.Length, drawnFrom, named, compared.Below, compared.Pivot, compared.SignAt, compared.Sign,
            into.Groups["n"].Captures[0].Value, compared.Strict, Walked: true);
    }

    /// <summary>The line of the function's call to itself with the part - in the call's brackets, or by the name it is given - or null.</summary>
    private static int? SortedAgain(IReadOnlyList<string> masked, Dialect dialect, Function function, Part part)
    {
        foreach (var call in SelfCalls(masked, dialect, function))
        {
            if (call.Line == part.Line && part.Start >= call.Start && part.Start + part.Length <= call.Start + call.Written.Length) return call.Line;
            if (part.Into is { } into && call.Arguments.Any(argument => Same(Spaceless(Unbracketed(argument)), Spaceless(into), dialect))) return call.Line;
        }

        return null;
    }

    /// <summary>Whether two lines of the function are in the same blocks - each line heading one, by indentation, the same for both.</summary>
    private static bool SameBranch(IReadOnlyList<string> masked, Function function, int first, int second)
    {
        List<int> Heads(int line)
        {
            var heads = new List<int>();
            var indent = CodeText.Indentation(masked[line]).Length;
            foreach (var above in function.Body.Where(above => above < line).Reverse())
            {
                if (masked[above].Trim().Length == 0 || CodeText.Indentation(masked[above]).Length >= indent) continue;
                indent = CodeText.Indentation(masked[above]).Length;
                heads.Add(above);
            }

            return heads;
        }

        return first == second || Heads(first).SequenceEqual(Heads(second));
    }

    /// <summary>Whether the pivot is named again outside the two parts - lesser + [pivot] + greater - so it is put back between them.</summary>
    private static bool PutBack(IReadOnlyList<string> masked, Dialect dialect, Function function, Part below, Part above, RegexOptions options)
    {
        var pivot = new Regex($@"(?<![\w$@.]){Regex.Escape(below.Pivot)}(?!\w)", options);
        var firstLine = Math.Min(below.Line, above.Line);
        var lastLine = Math.Max(below.Line, above.Line);
        foreach (var line in function.Body.Where(line => line >= firstLine && !function.Inner.Contains(line)))
        {
            var text = masked[line];

            // A loop's two tests, and the lines putting the items into their lists, are the parts themselves.
            if (below.Walked && line <= lastLine + 1) continue;
            foreach (var part in new[] { below, above }.Where(part => part.Line == line).OrderByDescending(part => part.Start))
                text = text[..part.Start] + new string(' ', part.Length) + text[(part.Start + part.Length)..];
            if (pivot.IsMatch(text)) return true;
        }

        return false;
    }

    /// <summary>
    /// The finding, said of the part above the pivot - with the change that keeps what equals it there, x &gt;= pivot, when the
    /// part is drawn from the list's other items, so the pivot itself is not among them.
    /// </summary>
    private static LogicFinding Dropped(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, Partition partition, Pivot pivot, Part below, Part above, RegexOptions options)
    {
        var shown = pivot.Name;
        string Kept(Part part) => $"`{source.Lines[part.Line].Substring(part.Start, part.Length).Trim()}`";

        var kept = below.Walked
            ? $"Line {below.Line + 1} puts the values below `{shown}` into `{below.Into}` - {Kept(below)} - and line {above.Line + 1} those above it into `{above.Into}` - {Kept(above)}"
            : $"Line {below.Line + 1} keeps the values below `{shown}` - {Kept(below)} - and line {above.Line + 1} the values above it - {Kept(above)}";
        var told = $"{kept} - so a value equal to `{shown}` goes into neither. Only `{shown}` itself is put back between them, so any other value equal to it is " +
                   "not put back at all.";

        var others = OthersOf(masked, dialect, partition, pivot, above.Source, options);
        LocalFix? fix = null;
        if (others)
        {
            var text = source.Lines[above.Line];
            var sign = above.Sign;
            var wider = sign switch { ">" => ">=", "<" => "<=", "-gt" or "-GT" => "-ge", "-lt" or "-LT" => "-le", ".gt." => ".ge.", ".lt." => ".le.", _ => sign };
            var changed = text[..above.SignAt] + wider + text[(above.SignAt + sign.Length)..];
            fix = LocalFix.ReplaceLine(id, $"Keep what equals {shown} with the values above it: {wider}",
                $"With {wider}, each value equal to `{shown}` goes in with the values above it, and is sorted and put back with them - and as the part is drawn " +
                $"from the other items, `{shown}` itself is not among them.", source.Path, above.Line + 1, changed);
        }

        return new LogicFinding(id, above.Line + 1, told, fix) { Severity = Severity.Error, Confidence = Confidence.Likely };
    }

    /// <summary>
    /// Whether the parts are drawn from the list's items other than the pivot: the name a pattern gives them - [pivot | rest] -
    /// or, for the list's first item as the pivot, its other items written out, or a loop from its second position.
    /// </summary>
    private static bool OthersOf(IReadOnlyList<string> masked, Dialect dialect, Partition partition, Pivot pivot, string source, RegexOptions options)
    {
        if (pivot.Others is { } others && Same(Spaceless(source), Spaceless(others), dialect)) return true;
        if (pivot.OthersWritten.Count == 0 || pivot.List.Length == 0) return false;

        // A loop through positions from the second: items from 1, or from 2 where they count from 1.
        if (Regex.Match(source, @"^(?<x>\S+) from (?<k>\d+)$") is { Success: true } walked)
            return Same(walked.Groups["x"].Value, pivot.List, dialect) && walked.Groups["k"].Value == (partition.CountsFromOne ? "2" : "1");

        bool IsOthers(string text) => pivot.OthersWritten.Any(written => written.Length > 0 && Form(written, pivot.List, dialect, options, "^", "$").IsMatch(text.Trim()));
        if (IsOthers(source)) return true;

        // A name given them: rest = items[1:], or C++'s std::vector<int> rest(items.begin() + 1, items.end());.
        foreach (var text in masked)
        {
            if (Assigned().Match(text) is { Success: true } assigned && Same(assigned.Groups["mid"].Value, source, dialect) && IsOthers(ValueOf(assigned))) return true;
            if (Regex.Match(text, $@"^\s*(?<type>(?:std::)?vector\s*<[^>]*>)\s+{Regex.Escape(source)}\s*\((?<from>.*)\)\s*;\s*$") is { Success: true } declared &&
                IsOthers($"{declared.Groups["type"].Value}({declared.Groups["from"].Value})")) return true;
        }

        return false;
    }

    // ---- Forms -----------------------------------------------------------------------------------------------------

    private static RegexOptions Options(Dialect dialect) => dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;

    private static readonly Dictionary<(string, string, string, bool, string, string), Regex> Made = [];

    /// <summary>
    /// A form as a pattern: {x} the list, {xb} its name without a sigil, {m} the middle as "m", {len} its length, {n} and {a}
    /// names, {u} a part of the list, {t} a type; a space may be any space or none. <paramref name="before"/> and
    /// <paramref name="after"/> are patterns put round it as they are - ^ and $ for the whole of a text.
    /// </summary>
    private static Regex Form(string form, string list, Dialect dialect, RegexOptions options, string before = "", string after = "")
    {
        var key = (form, list, dialect.Id, options.HasFlag(RegexOptions.IgnoreCase), before, after);
        lock (Made)
        {
            if (Made.TryGetValue(key, out var made)) return made;
        }

        var lengths = string.Join("|", dialect.Lengths.Where(length => length != "{x}" || dialect.Syntax == Syntax.Perl)
            .Select(length => Pattern(length, list, "")));
        var regex = new Regex(before + Pattern(form, list, lengths) + after, options);
        lock (Made) Made[key] = regex;
        return regex;
    }

    private static string Pattern(string form, string list, string lengths)
    {
        var bare = list.TrimStart('$', '@');
        var built = new StringBuilder();
        for (var at = 0; at < form.Length; at++)
        {
            if (form[at] == '{' && form.IndexOf('}', at) is var close and > 0 && close - at <= 4)
            {
                var placeholder = form[(at + 1)..close];
                var piece = placeholder switch
                {
                    // With no list given, {x} is any list's name: for {i} = {k}, #{x} do.
                    "x" when list.Length == 0 => @"(?<x>[$@]?[A-Za-z_]\w*)",
                    "x" => $@"(?<![\w$@.]){Regex.Escape(list)}(?!\w)",
                    "s" => @"(?<![\w$@.])(?<s>[$@]?[A-Za-z_]\w*(?:\((?:[^()]|\([^()]*\))*\)|\[(?:[^\[\]]|\[[^\[\]]*\])*\]|\.[A-Za-z_]\w*)*)",
                    "v" => @"(?<v>[$@]?[A-Za-z_]\w*)",
                    "c" => @"(?<c>(?:[^(){};]|\((?:[^()]|\([^()]*\))*\))+?)",
                    "i" => @"(?<i>[$]?[A-Za-z_]\w*)",
                    "k" => @"(?<k>\d+)",
                    "any" => @"(?:[^()\[\]{};]|\([^()]*\)|\[[^\[\]]*\])+?",
                    "xb" => $@"{Regex.Escape(bare)}(?!\w)",
                    "m" => @"(?<m>(?:[^()\[\]{},;]|\((?:[^()]|\([^()]*\))*\)|\[[^\[\]]*\])+?)",
                    "len" => $"(?:{lengths})",
                    "n" => @"(?<n>[$@]?[A-Za-z_]\w*)",
                    "u" => @"(?<u>.+)",
                    "a" => @"[$@]?[A-Za-z_]\w*",
                    "t" => @"[\w.:<>\[\]?]+",
                    _ => null,
                };

                if (piece is not null)
                {
                    built.Append(piece);
                    at = close;
                    continue;
                }
            }

            built.Append(form[at] == ' ' ? @"\s*" : Regex.Escape(form[at].ToString()));
        }

        return built.ToString();
    }

    private static string Spaceless(string text) => Regex.Replace(text, @"\s+", "");

    private static bool Same(string first, string second, Dialect dialect) =>
        string.Equals(first, second, dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool SameText(string first, string second, Dialect dialect) => Same(first, second, dialect);
}
