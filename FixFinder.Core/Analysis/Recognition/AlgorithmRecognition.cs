using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Recognition;

/// <summary>
/// An algorithm a function's code has the shape of: which function, where, what it looks like, and the parts of the
/// code that make it look so - the names it keeps, the item it looks at, the swap it makes.
/// </summary>
/// <param name="Function">The function as the program names it - or "the top-level code".</param>
/// <param name="Algorithm">What it looks like: "a binary search".</param>
/// <param name="HowItWorks">What in the code makes it look so, in the code's own names.</param>
/// <param name="WhatToKnow">What that algorithm needs, or costs, whoever writes it.</param>
/// <summary>What a search gives back when it finds what it looks for.</summary>
public enum SearchGives
{
    /// <summary>Where it is: return mid, return index.</summary>
    Position,

    /// <summary>The item itself: return item.</summary>
    Item,

    /// <summary>That it is there: return True.</summary>
    Truth,
}

public sealed record AlgorithmSeen(string File, int Line, string Function, string Algorithm, string HowItWorks, string WhatToKnow)
{
    /// <summary>The name the code gives the list the algorithm works on - items - when it is a plain name.</summary>
    public string? List { get; init; }

    /// <summary>
    /// The name the code gives what a search looks for - target - when it is a plain name; null for a sort.
    /// </summary>
    public string? Sought { get; init; }

    /// <summary>
    /// What a search gives back when it finds the value, read from the code; null for a sort, or when it cannot be
    /// told.
    /// </summary>
    public SearchGives? Gives { get; init; }

    /// <summary>The name the code gives the first of two numbers - a in gcd(a, b) - whose second is Sought.</summary>
    public string? FirstNumber { get; init; }

    /// <summary>The name the code gives the second of a merge's two lists, whose first is List.</summary>
    public string? SecondList { get; init; }

    /// <summary>
    /// What calling the function on lists of its own showed, when it was called: every one right, or the first that was
    /// not.
    /// </summary>
    public string? Tried { get; init; }

    /// <summary>Whether it is a sort, whose result is the same items in order, rather than a search.</summary>
    public bool IsSort => Algorithm.EndsWith(" sort", StringComparison.Ordinal);

    /// <summary>Whether it is a search for the largest or the smallest item, whose result is that item.</summary>
    public bool IsExtreme => Algorithm.StartsWith("a search for the ", StringComparison.Ordinal);

    /// <summary>Whether it is a prime test, whose answer for each number is whether that number is prime.</summary>
    public bool IsPrimeTest => Algorithm == AlgorithmRecognition.PrimeTest;

    /// <summary>Whether it is a leap-year test, whose answer for each year is whether that year is a leap year.</summary>
    public bool IsLeapYearTest => Algorithm == AlgorithmRecognition.LeapYearTest;

    /// <summary>Whether it is given a number on its own, with no list, and answers yes or no of it: a prime or a leap-year test.</summary>
    public bool IsNumberTest => IsPrimeTest || IsLeapYearTest;

    /// <summary>Whether it is a factorial, whose answer for each number n is 1 times 2 times each number up to n.</summary>
    public bool IsFactorial => Algorithm == AlgorithmRecognition.Factorial;

    /// <summary>Whether it is the Fibonacci numbers, whose answer for each number is the two before it added.</summary>
    public bool IsFibonacci => Algorithm == AlgorithmRecognition.Fibonacci;

    /// <summary>Whether it is a merge of two lists in order, whose result is every item of both, in order.</summary>
    public bool IsMerge => Algorithm == AlgorithmRecognition.Merge;

    /// <summary>Whether it is given a number on its own, with no list: a prime test, a leap-year test, a factorial or the Fibonacci numbers.</summary>
    public bool IsGivenANumber => IsNumberTest || IsFactorial || IsFibonacci;

    /// <summary>Whether it is a reversal, whose result is the same items in the opposite order.</summary>
    public bool IsReversal => Algorithm == AlgorithmRecognition.Reversal;

    /// <summary>Whether it is Euclid's greatest common divisor, whose answer for two numbers is the greatest dividing both.</summary>
    public bool IsGreatestCommonDivisor => Algorithm == AlgorithmRecognition.GreatestCommonDivisor;

    /// <summary>For a search for the largest or smallest item: whether it is the largest.</summary>
    public bool IsLargest => Algorithm == AlgorithmRecognition.LargestSearch;

    /// <summary>The whole of it as one sentence: "`find` looks like a binary search: it keeps ...".</summary>
    public string Said =>
        $"{(Function == AlgorithmRecognition.TopLevelCode ? "The top-level code" : $"`{Function}`")} looks like {Algorithm}: {HowItWorks}. {WhatToKnow}";
}

/// <summary>
/// Recognising the algorithm a function is written as, from the shape of its code: a binary search keeps a range, looks
/// at its middle and moves one end past the middle; a bubble sort swaps neighbours that are out of order inside two
/// loops. Each is read from the program's IR, so it is the same in every language FixFinder follows values in - Python,
/// Java, JavaScript, C#, C, C++ and Go - and it only says what the code's own shape shows, naming the code that shows
/// it.
/// </summary>
/// <remarks>
/// Recognising is not checking: a function that looks like a binary search may still be a wrong one, which is what the
/// logic checks look for. What is recognised is shown to say what the code seems meant to do, and what that kind of
/// code needs.
/// </remarks>
public static class AlgorithmRecognition
{
    public const string TopLevelCode = "the top-level code";

    public const string LargestSearch = "a search for the largest item";

    public const string SmallestSearch = "a search for the smallest item";

    public const string PrimeTest = "a prime test";

    public const string LeapYearTest = "a leap-year test";

    public const string Factorial = "a factorial";

    public const string Reversal = "a reversal";

    public const string GreatestCommonDivisor = "Euclid's greatest common divisor";

    public const string Fibonacci = "the Fibonacci numbers";

    public const string Merge = "a merge of two lists in order";

    /// <summary>
    /// The code's own words for an expression read as something else - items.get(j) read as items[j] - while a program
    /// is being recognised; what is said of the code says it as it is written.
    /// </summary>
    [ThreadStatic]
    private static Dictionary<Expr, string>? _writtenAs;

    public static IReadOnlyList<AlgorithmSeen> Of(IrProgram program)
    {
        _writtenAs = new Dictionary<Expr, string>(ReferenceEqualityComparer.Instance);
        try
        {
            return Recognised(program);
        }
        finally
        {
            _writtenAs = null;
        }
    }

    private static IReadOnlyList<AlgorithmSeen> Recognised(IrProgram program)
    {
        var seen = new List<AlgorithmSeen>();
        foreach (var written in program.AllFunctions)
        {
            var function = program.Language == SourceLanguage.Java ? JavaListsReadAsIndexed(written) : written;
            // A function declared inside the top-level code - C#'s local functions - is named as the program names it.
            var name = function.Name == IrFunction.ModuleBody ? TopLevelCode
                : function.FullName.StartsWith(IrFunction.ModuleBody + ".", StringComparison.Ordinal) ? function.FullName[(IrFunction.ModuleBody.Length + 1)..]
                : function.FullName;
            var statements = IrWalk.Statements(function.Body).ToList();

            foreach (var recognise in Recognisers)
            {
                if (recognise(function, statements) is not { } found) continue;
                seen.Add(new AlgorithmSeen(function.Span.File, found.Line > 0 ? found.Line : function.Span.Line, name, found.Algorithm, found.HowItWorks, found.WhatToKnow)
                {
                    List = found.List,
                    Sought = found.Sought,
                    Gives = found.Gives,
                    FirstNumber = found.FirstNumber,
                    SecondList = found.SecondList,
                });
            }
        }

        return seen;
    }

    private sealed record Found(int Line, string Algorithm, string HowItWorks, string WhatToKnow, string? List = null, string? Sought = null)
    {
        public SearchGives? Gives { get; init; }

        /// <summary>The first of a function's two numbers - a in gcd(a, b) - whose second is Sought.</summary>
        public string? FirstNumber { get; init; }

        /// <summary>The second of a merge's two lists, whose first is List.</summary>
        public string? SecondList { get; init; }
    }

    /// <summary>
    /// What a branch that found the value gives back: the position it was found at, the item, or true - null when it
    /// returns anything else, or leaves without returning.
    /// </summary>
    private static SearchGives? GivenBack(IReadOnlyList<Stmt> found, string? position, string item) =>
        found.OfType<Return>().FirstOrDefault()?.Value switch
        {
            Name { Identifier: var name } when name == position => SearchGives.Position,
            Name { Identifier: var name } when name == item => SearchGives.Item,
            { } value when IrText.Of(value) == item => SearchGives.Item,
            Literal { Kind: LiteralKind.Boolean, Value: true } => SearchGives.Truth,
            _ => null,
        };

    private static string? NameOf(Expr expression) => expression is Name { Identifier: var name } ? name : null;

    /// <summary>An expression as the code writes it: items.get(j), where it was read as items[j].</summary>
    private static string Shown(Expr expression) => _writtenAs?.GetValueOrDefault(expression) ?? Unbracketed(expression);

    /// <summary>
    /// An expression as it is usually written: a sum or difference taken further left to right with no brackets round
    /// its first part - len(items) - 1 - i, not (len(items) - 1) - i - inside an item's position too.
    /// </summary>
    private static string Unbracketed(Expr expression) => expression switch
    {
        Binary { Operator: BinaryOperator.Add or BinaryOperator.Subtract, Left: Binary { Operator: BinaryOperator.Add or BinaryOperator.Subtract } first, Right: var then } chain =>
            $"{Unbracketed(first)} {(chain.Operator == BinaryOperator.Add ? "+" : "-")} {(then is Binary ? $"({IrText.Of(then)})" : IrText.Of(then))}",
        ElementAccess { Target: var list, Key: var position } => $"{IrText.Of(list)}[{Unbracketed(position)}]",
        // Sums, products and remainders are worked out before a comparison in every language read: a % b != 0.
        Binary { Operator: var compared, Left: var left, Right: var right } when ComparisonWords.TryGetValue(compared, out var word) =>
            $"{Compared(left)} {word} {Compared(right)}",
        _ => IrText.Of(expression),
    };

    private static readonly Dictionary<BinaryOperator, string> ComparisonWords = new()
    {
        [BinaryOperator.Equal] = "==", [BinaryOperator.NotEqual] = "!=", [BinaryOperator.Less] = "<",
        [BinaryOperator.LessOrEqual] = "<=", [BinaryOperator.Greater] = ">", [BinaryOperator.GreaterOrEqual] = ">=",
    };

    /// <summary>One side of a comparison: a sum, product or remainder with no brackets, anything else as it reads.</summary>
    private static string Compared(Expr side) => side switch
    {
        Binary { Operator: BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.FloorDivide or BinaryOperator.Modulo } =>
            Unbracketed(side),
        Binary => $"({IrText.Of(side)})",
        _ => IrText.Of(side),
    };

    /// <summary>
    /// A Java method with its lists read as an array is: items.get(j) as items[j], items.set(j, v) as items[j] = v,
    /// a.equals(b) and Objects.equals(a, b) as a == b - each remembered as it is written, to be said so.
    /// </summary>
    private static IrFunction JavaListsReadAsIndexed(IrFunction function) => new IrRewrite(expression =>
    {
        Expr? read = expression switch
        {
            Call { Callee: Member { Target: var list, MemberName: "get" }, Arguments: [var at] } => new ElementAccess(expression.Span, list, at.Value),
            Call { Callee: Member { Target: var list, MemberName: "set" }, Arguments: [var at, var value] } =>
                new AssignValue(expression.Span, new ElementAccess(expression.Span, list, at.Value), value.Value),
            Call { Callee: Member { Target: var one, MemberName: "equals" }, Arguments: [var other] } => new Binary(expression.Span, BinaryOperator.Equal, one, other.Value),
            Call { Callee: Member { Target: Name { Identifier: "Objects" }, MemberName: "equals" }, Arguments: [var one, var other] } =>
                new Binary(expression.Span, BinaryOperator.Equal, one.Value, other.Value),
            _ => null,
        };
        if (read is null) return expression;

        // Said from its parts as they are written, as a part read already - the get inside a set - is read as an array's.
        var written = expression switch
        {
            Call { Callee: Member { Target: Name { Identifier: "Objects" } }, Arguments: [var one, var other] } => $"Objects.equals({Shown(one.Value)}, {Shown(other.Value)})",
            Call { Callee: Member { Target: var owner, MemberName: var method }, Arguments: var given } =>
                $"{Shown(owner)}.{method}({string.Join(", ", given.Select(argument => Shown(argument.Value)))})",
            _ => IrText.Of(expression),
        };
        _writtenAs?.TryAdd(read, written);
        return read;
    }).Function(function);

    private static readonly Func<IrFunction, IReadOnlyList<Stmt>, Found?>[] Recognisers =
    [
        BinarySearch,
        LinearSearch,
        BubbleSort,
        SelectionSort,
        InsertionSort,
        LargestOrSmallest,
        PrimeTestOf,
        LeapYearTestOf,
        FactorialOf,
        ReversalOf,
        GreatestCommonDivisorOf,
        FibonacciOf,
        MergeOf,
    ];

    /// <summary>
    /// A loop that runs while one name is below another - low &lt;= high - in which a third is set to their middle, the
    /// item at that middle is compared, and both ends are moved by the middle: low = mid + 1, high = mid - 1.
    /// </summary>
    private static Found? BinarySearch(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        foreach (var loop in statements.Where(statement => statement is While or For))
        {
            if (Condition(loop) is not Binary { Left: Name left, Right: Name right } test || left.Identifier == right.Identifier) continue;
            var (low, high) = test.Operator switch
            {
                BinaryOperator.Less or BinaryOperator.LessOrEqual => (left.Identifier, right.Identifier),
                BinaryOperator.Greater or BinaryOperator.GreaterOrEqual => (right.Identifier, left.Identifier),
                _ => ((string?)null, (string?)null),
            };
            if (low is null || high is null) continue;

            var inside = IrWalk.Statements(Body(loop)).ToList();
            var writes = Writes(inside).ToList();
            foreach (var middle in writes.Where(write => write.Name is { } named && named != low && named != high &&
                                                         Mentions(write.Value, low) && Mentions(write.Value, high) && Halves(write.Value)))
            {
                var mid = middle.Name!;
                if (ComparedItemAt(inside, mid) is not { } probed) continue;
                if (!writes.Any(write => write.Name == low && Mentions(write.Value, mid)) || !writes.Any(write => write.Name == high && Mentions(write.Value, mid))) continue;

                return new Found(loop.Span.Line, "a binary search",
                    $"it keeps a range from `{low}` to `{high}`, looks at its middle, `{mid}`, compares `{Shown(probed)}` with what it looks for, " +
                    $"and moves `{low}` or `{high}` past the middle, halving the range each time round",
                    "A binary search looks at no more than about log2(n) items of n, but finds what it looks for only in a list that is in order.",
                    NameOf(probed.Target), SoughtBeside(inside, probed))
                {
                    Gives = inside.OfType<If>().Where(branch => branch.Condition is Binary { Operator: BinaryOperator.Equal } equal &&
                                                                (IrText.Of(equal.Left) == IrText.Of(probed) || IrText.Of(equal.Right) == IrText.Of(probed)))
                        .Select(branch => GivenBack(branch.Then, mid, IrText.Of(probed))).FirstOrDefault(given => given is not null),
                };
            }
        }

        return null;
    }

    /// <summary>
    /// A loop over a list - for each item, or by a position it moves one at a time - with an if that compares the item,
    /// or the item at the position, for equality with something the loop does not change, and leaves the loop or the
    /// function when it is equal.
    /// </summary>
    private static Found? LinearSearch(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        foreach (var loop in statements.Where(IsLoop))
        {
            var changedInLoop = Writes(IrWalk.Statements(Body(loop))).Select(write => write.Name).OfType<string>()
                .Concat(loop is ForEach { Target: Name each } ? [each.Identifier] : []).ToHashSet(StringComparer.Ordinal);
            var stepped = SteppedByOne(loop);

            foreach (var check in IrWalk.Statements(Body(loop)).OfType<If>())
            {
                if (check.Condition is not Binary { Operator: BinaryOperator.Equal } equal) continue;
                if (!check.Then.Any(statement => statement is Return or Break)) continue;

                foreach (var (looked, sought) in new[] { (equal.Left, equal.Right), (equal.Right, equal.Left) })
                {
                    var isTheItem = loop is ForEach { Target: Name item } && looked is Name { Identifier: var name } && name == item.Identifier ||
                                    looked is ElementAccess { Key: Name key } && stepped.Contains(key.Identifier);
                    if (!isTheItem || IrWalk.Names(sought).Any(changedInLoop.Contains)) continue;

                    // Read by position, the list is the one the position is taken into, not the range of positions gone through.
                    var over = looked is ElementAccess { Target: var list } ? $"`{IrText.Of(list)}`" : loop is ForEach { Items: var items } ? $"`{IrText.Of(items)}`" : "the list";
                    return new Found(loop.Span.Line, "a linear search",
                        $"it goes through {over} one item at a time and stops at the first, `{Shown(looked)}`, that equals `{Shown(sought)}`",
                        "A linear search needs no order, and looks at every item when what it looks for is not there - n looks for n items.",
                        looked is ElementAccess { Target: var indexed } ? NameOf(indexed) : loop is ForEach { Items: var walked } ? NameOf(walked) : null, NameOf(sought))
                    {
                        Gives = GivenBack(check.Then, looked is ElementAccess { Key: Name position } ? position.Identifier : null, IrText.Of(looked)),
                    };
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A loop inside a loop, where two neighbours - items[j] and items[j + 1] - are compared, and swapped when out of
    /// order.
    /// </summary>
    private static Found? BubbleSort(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        foreach (var outer in statements.Where(IsLoop))
        {
            foreach (var inner in IrWalk.Statements(Body(outer)).Where(IsLoop))
            {
                foreach (var check in IrWalk.Statements(Body(inner)).OfType<If>())
                {
                    if (check.Condition is not Binary { Operator: BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual } order ||
                        order.Left is not ElementAccess first || order.Right is not ElementAccess second || !Neighbours(first, second)) continue;

                    if (!Swaps(IrWalk.Statements(check.Then).ToList()).Any(swap => SamePair(swap, first, second))) continue;

                    return new Found(outer.Span.Line, "a bubble sort",
                        $"it goes over `{IrText.Of(first.Target)}` again and again, and wherever two neighbours, `{Shown(first)}` and `{Shown(second)}`, " +
                        "are out of order, swaps them",
                        "A bubble sort compares about half of n squared pairs of n items, so it slows down fast as the list grows.",
                        NameOf(first.Target));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A loop that sets a name to the position it steps through - smallest = i - then, in a loop inside it, moves that
    /// name to any later position whose item is in order before it, and afterwards swaps the item at the position with
    /// the item it found.
    /// </summary>
    private static Found? SelectionSort(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        foreach (var outer in statements.Where(IsLoop))
        {
            var outerBody = IrWalk.Statements(Body(outer)).ToList();
            var position = SteppedByOne(outer);
            foreach (var start in Writes(outerBody).Where(write => write.Name is not null && write.Value is Name { Identifier: var from } && position.Contains(from)))
            {
                var chosen = start.Name!;
                var at = ((Name)start.Value).Identifier;

                foreach (var inner in outerBody.Where(IsLoop))
                {
                    var moved = IrWalk.Statements(Body(inner)).OfType<If>().FirstOrDefault(check =>
                        check.Condition is Binary { Operator: BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual } order &&
                        new[] { order.Left, order.Right }.Any(side => side is ElementAccess { Key: Name key } && key.Identifier == chosen) &&
                        Writes(IrWalk.Statements(check.Then)).Any(write => write.Name == chosen));
                    if (moved is null) continue;

                    var afterInner = outerBody.Except<Stmt>(IrWalk.Statements(Body(inner)), ReferenceEqualityComparer.Instance).ToList();
                    var swap = Swaps(afterInner).FirstOrDefault(pair =>
                        new[] { pair.First, pair.Second }.All(side => side is ElementAccess { Key: Name key } && (key.Identifier == chosen || key.Identifier == at)) &&
                        pair.First is ElementAccess { Key: Name one } && pair.Second is ElementAccess { Key: Name other } && one.Identifier != other.Identifier);
                    if (swap.First is null) continue;

                    return new Found(outer.Span.Line, "a selection sort",
                        $"for each position `{at}` it looks through the rest for the item that should come first, keeping its position in `{chosen}`, " +
                        $"then swaps `{Shown(swap.First)}` and `{Shown(swap.Second)}`",
                        "A selection sort compares about half of n squared pairs of n items, however ordered they already are, but swaps at most n - 1 times.",
                        swap.First is ElementAccess { Target: var swapped } ? NameOf(swapped) : null);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A loop that takes out the item at the position it steps through - key = items[i] - then, while the items before
    /// it are in order after it, moves each one along - items[j + 1] = items[j] - and puts the item back in the gap
    /// left: items[j + 1] = key.
    /// </summary>
    private static Found? InsertionSort(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        foreach (var outer in statements.Where(IsLoop))
        {
            var outerBody = IrWalk.Statements(Body(outer)).ToList();
            var stepped = SteppedByOne(outer);
            foreach (var taken in Writes(outerBody).Where(write => write.Name is not null && write.Value is ElementAccess { Key: Name { Identifier: var from } } && stepped.Contains(from)))
            {
                var key = taken.Name!;
                var list = IrText.Of(((ElementAccess)taken.Value).Target);

                foreach (var inner in outerBody.Where(statement => IsLoop(statement) && Condition(statement) is { } test && Mentions(test, key)))
                {
                    var innerWrites = Writes(IrWalk.Statements(Body(inner))).ToList();
                    var shift = innerWrites.FirstOrDefault(write => write.Target is ElementAccess { Target: var to } && IrText.Of(to) == list &&
                                                                     write.Value is ElementAccess { Target: var from } && IrText.Of(from) == list);
                    if (shift.Target is null) continue;

                    var afterInner = Writes(outerBody.Except<Stmt>(IrWalk.Statements(Body(inner)), ReferenceEqualityComparer.Instance)).ToList();
                    if (!afterInner.Any(write => write.Target is ElementAccess { Target: var into } && IrText.Of(into) == list && write.Value is Name { Identifier: var back } && back == key)) continue;

                    return new Found(outer.Span.Line, "an insertion sort",
                        $"it takes each item out into `{key}`, moves the items before it that should come after it one place along, " +
                        $"`{(shift.Assigned is { } assigned && _writtenAs?.GetValueOrDefault(assigned) is { } call ? call : $"{Shown(shift.Target)} = {Shown(shift.Value)}")}`, and puts `{key}` back in the gap",
                        "An insertion sort is quick on a list that is nearly in order, and compares about half of n squared pairs of n items on one in reverse order.",
                        NameOf(((ElementAccess)taken.Value).Target));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A loop over a list - by item, or by a position it steps one at a time - in which the item is compared with a
    /// name the loop keeps, and put there when it is larger, or smaller: if item > best: best = item. When the function
    /// gives back that name, the recognition says so, for it to be called and checked.
    /// </summary>
    private static Found? LargestOrSmallest(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        foreach (var loop in statements.Where(IsLoop))
        {
            var stepped = SteppedByOne(loop);
            foreach (var check in IrWalk.Statements(Body(loop)).OfType<If>())
            {
                if (check.Condition is not Binary { Operator: BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual } compare)
                    continue;

                foreach (var (item, kept, itemFirst) in new[] { (compare.Left, compare.Right, true), (compare.Right, compare.Left, false) })
                {
                    var isTheItem = loop is ForEach { Target: Name each } && item is Name { Identifier: var named } && named == each.Identifier ||
                                    item is ElementAccess { Key: Name key } && stepped.Contains(key.Identifier);
                    if (!isTheItem || kept is not Name { Identifier: var best } || stepped.Contains(best)) continue;
                    if (!Writes(IrWalk.Statements(check.Then)).Any(write => write.Name == best && IrText.Of(write.Value) == IrText.Of(item))) continue;

                    var largest = (compare.Operator is BinaryOperator.Greater or BinaryOperator.GreaterOrEqual) == itemFirst;
                    var list = item is ElementAccess { Target: var indexed } ? indexed : loop is ForEach { Items: var walked } ? walked : null;
                    if (list is null) continue;

                    return new Found(loop.Span.Line, largest ? LargestSearch : SmallestSearch,
                        $"it goes through `{Shown(list)}` and keeps the {(largest ? "largest" : "smallest")} item it has seen in `{best}`, putting each " +
                        $"{(largest ? "larger" : "smaller")} one, `{Shown(item)}`, there",
                        $"It looks at each item once, n looks for n items. What `{best}` starts at has to be an item of the list - a start of 0 " +
                        $"stays the {(largest ? "largest of a list all below zero" : "smallest of a list all above zero")}.",
                        NameOf(list))
                    {
                        Gives = statements.OfType<Return>().Any(leave => leave.Value is Name { Identifier: var returned } && returned == best) ? SearchGives.Item : null,
                    };
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A loop that steps a name one at a time and, when the function's parameter divides by it exactly - n % i == 0 -
    /// gives back false: the parameter is not prime. When the function gives back true otherwise, the recognition says
    /// so, for it to be called on numbers and checked.
    /// </summary>
    private static Found? PrimeTestOf(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        var parameters = function.Parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var loop in statements.Where(IsLoop))
        {
            var stepped = SteppedByOne(loop);
            foreach (var check in IrWalk.Statements(Body(loop)).OfType<If>())
            {
                if (check.Condition is not Binary { Operator: BinaryOperator.Equal } equal) continue;
                var remainder = (equal.Left, equal.Right) switch
                {
                    (Binary { Operator: BinaryOperator.Modulo } left, Literal { Kind: LiteralKind.Integer, Value: 0L }) => left,
                    (Literal { Kind: LiteralKind.Integer, Value: 0L }, Binary { Operator: BinaryOperator.Modulo } right) => right,
                    _ => null,
                };
                if (remainder is not { Left: Name { Identifier: var number }, Right: Name { Identifier: var divisor } } ||
                    !parameters.Contains(number) || !stepped.Contains(divisor)) continue;
                if (!check.Then.Any(statement => statement is Return { Value: Literal { Kind: LiteralKind.Boolean, Value: false } })) continue;

                return new Found(loop.Span.Line, PrimeTest,
                    $"it tries each `{divisor}` as a divisor of `{number}`, and says `{number}` is not prime at the first that divides it exactly, " +
                    $"`{Shown(remainder)} == 0`",
                    "Trying divisors up to the square root of n is enough - a number with a factor above that has one below it too - and " +
                    "0 and 1 are not prime, which a prime test has to say for itself.",
                    Sought: number)
                {
                    Gives = statements.OfType<Return>().Any(leave => leave.Value is Literal { Kind: LiteralKind.Boolean, Value: true }) ? SearchGives.Truth : null,
                };
            }
        }

        return null;
    }

    /// <summary>The divisors a leap-year test asks about, in the order the rule takes them.</summary>
    private static readonly long[] LeapYearDivisors = [4, 100, 400];

    /// <summary>
    /// A function that asks whether 4 divides one of its parameters - year % 4 - and whether 100 or 400 does too, every
    /// answer it gives back a yes or a no; or one named for leap years that asks of 4 at least, whatever it does with the
    /// answer. A function asking the same of a year for some other end - the days in February - gives back a number, and
    /// is not one. When every answer is a yes or a no, the recognition says so, for it to be called on years and checked.
    /// </summary>
    private static Found? LeapYearTestOf(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        var returns = statements.OfType<Return>().ToList();
        var answersYesOrNo = returns.Count > 0 && returns.All(leave => leave.Value is { } value && IsYesOrNo(value));
        var namedForLeapYears = function.Name.Contains("leap", StringComparison.OrdinalIgnoreCase);
        if (!answersYesOrNo && !namedForLeapYears) return null;

        var remainders = statements.SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within).OfType<Binary>()
            .Where(remainder => remainder is { Operator: BinaryOperator.Modulo, Left: Name, Right: Literal { Kind: LiteralKind.Integer, Value: long } })
            .ToList();
        foreach (var parameter in function.Parameters)
        {
            var asked = remainders.Where(remainder => ((Name)remainder.Left).Identifier == parameter.Name).ToList();
            var firstOf = LeapYearDivisors.ToDictionary(divisor => divisor, divisor => asked.FirstOrDefault(remainder => (long)((Literal)remainder.Right).Value! == divisor));
            if (firstOf[4] is not { } byFour || firstOf[100] is null && firstOf[400] is null && !namedForLeapYears) continue;

            var divisors = LeapYearDivisors.Where(divisor => firstOf[divisor] is not null).ToList();
            var whichDivide = divisors.Count == 1 ? "4 divides" : $"{string.Join(", ", divisors.SkipLast(1))} and {divisors[^1]} divide";
            var written = divisors.Select(divisor => $"`{Shown(firstOf[divisor]!)}`").ToList();
            var asWritten = written.Count == 1 ? written[0] : $"{string.Join(", ", written.SkipLast(1))} and {written[^1]}";

            return new Found(byFour.Span.Line, LeapYearTest,
                $"it asks whether {whichDivide} `{parameter.Name}` - {asWritten}",
                "A year is a leap year when 4 divides it - but not when 100 does as well, unless 400 does too: 2024 and 2000 were " +
                "leap years, 2023 and 1900 were not.",
                Sought: parameter.Name)
            {
                Gives = answersYesOrNo ? SearchGives.Truth : null,
            };
        }

        return null;
    }

    /// <summary>
    /// A function of one number that multiplies a product by each number up to it in turn - a loop stepping a name one at
    /// a time to the parameter, with result *= i in it, and result given back - or that gives back the number times
    /// itself called with one less: n * factorial(n - 1). A product of a list's items, a power multiplying by the same
    /// number each time, and a loop stepping by 2 are none of them.
    /// </summary>
    private static Found? FactorialOf(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        if (function.Parameters is not [{ Name: var number }]) return null;

        foreach (var leave in statements.OfType<Return>())
        {
            if (leave.Value is not Binary { Operator: BinaryOperator.Multiply } product) continue;
            var (factor, call) = (product.Left, product.Right) switch
            {
                (Name left, Call right) => (left, right),
                (Call left, Name right) => (right, left),
                _ => ((Name?)null, (Call?)null),
            };
            if (factor?.Identifier != number || call is not { Callee: Name { Identifier: var called }, Arguments: [{ Value: var given }] } || called != function.Name ||
                given is not Binary { Operator: BinaryOperator.Subtract, Left: Name { Identifier: var lessened }, Right: Literal { Kind: LiteralKind.Integer, Value: 1L } } ||
                lessened != number)
                continue;

            return new Found(leave.Span.Line, Factorial,
                $"it gives back `{number}` times itself called with one less, `{Shown(product)}`",
                FactorialAdvice, Sought: number);
        }

        var returned = statements.OfType<Return>().Select(leave => leave.Value is Name { Identifier: var kept } ? kept : null)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var loop in statements.Where(IsLoop))
        {
            if (!RunsUpTo(loop, number)) continue;
            var stepped = SteppedByOne(loop);
            foreach (var (_, name, value, _) in Writes(IrWalk.Statements(Body(loop))))
            {
                if (name is null || !returned.Contains(name) || value is not Binary { Operator: BinaryOperator.Multiply } product) continue;
                var multiplier = (product.Left, product.Right) switch
                {
                    (Name { Identifier: var kept }, Name { Identifier: var other }) when kept == name => other,
                    (Name { Identifier: var other }, Name { Identifier: var kept }) when kept == name => other,
                    _ => null,
                };
                if (multiplier is null || !stepped.Contains(multiplier)) continue;
                if (multiplier == number && !CountedDown(loop, number)) continue;

                // while n > 1: product *= n; n -= 1 - the number itself is counted down to the end.
                var howItRuns = multiplier == number
                    ? $"it multiplies `{name}` by `{number}` each time round as `{number}` is counted down one at a time"
                    : $"it multiplies `{name}` by each `{multiplier}` in turn as `{multiplier}` runs to `{number}`";
                return new Found(loop.Span.Line, Factorial, $"{howItRuns}, and gives back `{name}`", FactorialAdvice, Sought: number);
            }
        }

        return null;
    }

    /// <summary>
    /// A list put in the opposite order. In place, by a loop that swaps the item at one end with the item at the other and
    /// moves both ends in - items[left] with items[right], or items[i] with items[n - 1 - i] - in a loop holding no if, no
    /// loop and no return, as a sort's or a partition's swap has. Or into a new list that is given back: by a loop going
    /// back from the last position adding the
    /// item at each, by one going forward putting each item first, or by one copying items[n - 1 - i] into result[i].
    /// </summary>
    private static Found? ReversalOf(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        var returned = statements.OfType<Return>().Select(leave => leave.Value is Name { Identifier: var kept } ? kept : null)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);

        foreach (var loop in statements.Where(IsLoop))
        {
            var stepped = SteppedByOne(loop);
            var body = Body(loop);

            // A partition swaps from both ends too, but its loop holds loops, ifs and a return; a reversal's holds none.
            var plainLoop = !IrWalk.Statements(body).Any(statement => statement is If or While or For or ForEach or Return);
            foreach (var (first, second) in plainLoop ? Swaps(body) : [])
            {
                if (first is not ElementAccess { Target: Name { Identifier: var list }, Key: var one } ||
                    second is not ElementAccess { Target: Name { Identifier: var other }, Key: var two } || list != other ||
                    !FromBothEnds(loop, stepped, one, two) && !FromBothEnds(loop, stepped, two, one))
                    continue;

                return new Found(loop.Span.Line, Reversal,
                    $"it swaps `{Shown(first)}` and `{Shown(second)}`, from the two ends of `{list}` in towards its middle",
                    ReversalAdvice, List: list);
            }

            foreach (var statement in body)
            {
                // result.append(items[i]) as i goes back from the last position.
                if (statement is Evaluate { Value: Call { Callee: Member { Target: Name { Identifier: var built }, MemberName: "append" or "push" or "add" or "Add" or "push_back" }, Arguments: [{ Value: ElementAccess { Target: Name { Identifier: var list }, Key: Name { Identifier: var position } } taken }] } } &&
                    built != list && returned.Contains(built) && GoesBack(loop, position))
                {
                    return new Found(loop.Span.Line, Reversal,
                        $"it goes back through the positions of `{list}` from the last, adding each `{Shown(taken)}` to `{built}`, and gives back `{built}`",
                        ReversalAdvice, List: list);
                }

                // result.insert(0, item) - or unshift(item) - for each item of the list.
                if (loop is ForEach { Target: Name { Identifier: var item }, Items: Name { Identifier: var walked } } &&
                    statement is Evaluate { Value: Call { Callee: Member { Target: Name { Identifier: var front }, MemberName: var method }, Arguments: var given } } &&
                    front != walked && returned.Contains(front) &&
                    (method is "insert" or "Insert" or "add" && given is [{ Value: Literal { Kind: LiteralKind.Integer, Value: 0L } }, { Value: Name { Identifier: var first } }] && first == item ||
                     method == "unshift" && given is [{ Value: Name { Identifier: var only } }] && only == item))
                {
                    return new Found(loop.Span.Line, Reversal,
                        $"it puts each `{item}` of `{walked}` first in `{front}`, and gives back `{front}`",
                        ReversalAdvice, List: walked);
                }
            }

            // result[i] = items[n - 1 - i] for each i.
            foreach (var (target, _, value, _) in Writes(body))
            {
                if (target is ElementAccess { Target: Name { Identifier: var built }, Key: Name { Identifier: var position } } placed &&
                    value is ElementAccess { Target: Name { Identifier: var list }, Key: var mirrored } copied &&
                    built != list && returned.Contains(built) && stepped.Contains(position) && !CountedDown(loop, position) && Mirrors(mirrored, position))
                {
                    return new Found(loop.Span.Line, Reversal,
                        $"it copies `{Shown(copied)}` into `{Shown(placed)}` for each `{position}`, and gives back `{built}`",
                        ReversalAdvice, List: list);
                }
            }
        }

        return null;
    }

    private const string ReversalAdvice =
        "Reversing a list in place takes a swap for each pair, half as many as there are items: swapping all the way along " +
        "swaps each pair twice and leaves the list as it was. A new list built from the end has to take in the item at " +
        "position 0 too.";

    /// <summary>
    /// Whether one position moves up from the start while the other comes down from the end: left and right, one counted
    /// up a step at a time and the other down; or i and n - 1 - i, i counted up.
    /// </summary>
    private static bool FromBothEnds(Stmt loop, HashSet<string> stepped, Expr rising, Expr falling) => (rising, falling) switch
    {
        (Name { Identifier: var up }, Name { Identifier: var down }) => up != down && stepped.Contains(up) && stepped.Contains(down) &&
            !CountedDown(loop, up) && CountedDown(loop, down),
        (Name { Identifier: var up }, _) => stepped.Contains(up) && !CountedDown(loop, up) && Mirrors(falling, up),
        _ => false,
    };

    /// <summary>Whether a position is counted from the end by another: n - 1 - i, len(items) - i - 1, items.length - 1 - i.</summary>
    private static bool Mirrors(Expr position, string counted) =>
        position is Binary { Operator: BinaryOperator.Subtract } &&
        IrWalk.Within(position).OfType<Binary>().Any(part => part is { Operator: BinaryOperator.Subtract, Right: Name { Identifier: var taken } } && taken == counted);

    /// <summary>
    /// Whether a loop goes back one position at a time: range(len(items) - 1, -1, -1), or i counted down - i--, i -= 1.
    /// </summary>
    private static bool GoesBack(Stmt loop, string position) => loop switch
    {
        ForEach { Target: Name { Identifier: var each }, Items: Call { Callee: Name { Identifier: "range" }, Arguments: [_, _, { Value: Unary { Operator: UnaryOperator.Negate, Operand: Literal { Kind: LiteralKind.Integer, Value: 1L } } or Literal { Kind: LiteralKind.Integer, Value: -1L } }] } } =>
            each == position,
        ForEach => false,
        _ => CountedDown(loop, position),
    };

    /// <summary>Whether a loop takes 1 from a name each time round: n -= 1, n = n - 1, n--.</summary>
    private static bool CountedDown(Stmt loop, string name) =>
        WritesEachToItsOwn(IrWalk.Statements(loop is For forLoop ? forLoop.Step.Concat(forLoop.Body) : Body(loop)))
            .Any(write => write.Name == name &&
                          write.Value is Binary { Operator: BinaryOperator.Subtract, Left: Name { Identifier: var from }, Right: Literal { Kind: LiteralKind.Integer, Value: 1L } } &&
                          from == name);

    /// <summary>
    /// Euclid's greatest common divisor, of a function's two numbers: one that gives back itself called with the second
    /// and the remainder of the first divided by it - gcd(b, a % b) - or one with a loop, run while it tests one of them,
    /// that takes the remainder of one divided by the other - a % b - each time round, and gives back one of the two. A
    /// function giving the pair back the other way round, gcd(a % b, b), never stops, and is left to the logic checks.
    /// </summary>
    private static Found? GreatestCommonDivisorOf(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        if (function.Parameters is not [{ Name: var first }, { Name: var second }]) return null;

        foreach (var leave in statements.OfType<Return>())
        {
            if (leave.Value is Call { Callee: Name { Identifier: var called }, Arguments: [{ Value: Name { Identifier: var divisor } }, { Value: Binary { Operator: BinaryOperator.Modulo, Left: Name { Identifier: var divided }, Right: Name { Identifier: var by } } remainder }] } &&
                called == function.Name && divisor == second && divided == first && by == second)
            {
                return new Found(leave.Span.Line, GreatestCommonDivisor,
                    $"it gives back itself called with `{second}` and the remainder of `{first}` divided by it, `{Shown(remainder)}`",
                    GreatestCommonDivisorAdvice, Sought: second) { FirstNumber = first };
            }
        }

        var returned = statements.OfType<Return>().Select(leave => leave.Value is Name { Identifier: var kept } ? kept : null)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var loop in statements.Where(statement => statement is While or For))
        {
            if (Condition(loop) is not { } test) continue;
            var writes = WritesEachToItsOwn(IrWalk.Statements(Body(loop))).ToList();
            var written = writes.Select(write => write.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
            foreach (var (_, _, value, _) in writes)
            {
                // Both numbers move on each time round: a remainder taken of one number alone - n = n % base - is not Euclid's.
                if (value is not Binary { Operator: BinaryOperator.Modulo, Left: Name { Identifier: var divided }, Right: Name { Identifier: var divisor } } remainder ||
                    divided == divisor || !Mentions(test, divisor) || !written.Contains(divided) || !written.Contains(divisor) ||
                    !returned.Contains(divided) && !returned.Contains(divisor))
                    continue;

                return new Found(loop.Span.Line, GreatestCommonDivisor,
                    $"while `{Shown(test)}`, it takes the remainder of `{divided}` divided by `{divisor}`, `{Shown(remainder)}`, each time round, " +
                    $"and gives back `{string.Join("` or `", new[] { divided, divisor }.Where(returned.Contains))}`",
                    GreatestCommonDivisorAdvice, Sought: second) { FirstNumber = first };
            }
        }

        return null;
    }

    /// <summary>
    /// The Fibonacci numbers, worked out for a function's one number: one that gives back itself called with one less and
    /// with two less, added - fib(n - 1) + fib(n - 2); one with a loop, run as far as the number, that keeps two names
    /// and works out their sum each time round - a, b = b, a + b, or by way of a third name - giving back one of the two;
    /// or one adding to a list the sum of its two items before - fib[i - 1] + fib[i - 2] - and giving back an item of it.
    /// </summary>
    private static Found? FibonacciOf(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        if (function.Parameters is not [{ Name: var number }]) return null;

        foreach (var leave in statements.OfType<Return>())
        {
            if (leave.Value is Binary { Operator: BinaryOperator.Add, Left: Call first, Right: Call second } sum &&
                CallsItselfWithLess(first) is { } one && CallsItselfWithLess(second) is { } other && one + other == 3)
            {
                return new Found(leave.Span.Line, Fibonacci,
                    $"it gives back itself called with one less and with two less, added: `{Shown(sum)}`", FibonacciAdvice, Sought: number);
            }
        }

        var returned = statements.OfType<Return>().Select(leave => leave.Value).OfType<Expr>().ToList();
        var returnedNames = returned.Select(value => value is Name { Identifier: var kept } ? kept : null).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var loop in statements.Where(IsLoop))
        {
            if (!RunsUpTo(loop, number) && (Condition(loop) is not { } test || !Mentions(test, number))) continue;
            var body = IrWalk.Statements(Body(loop)).ToList();
            var writes = WritesEachToItsOwn(body).ToList();
            var written = writes.Select(write => write.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var stepped = SteppedByOne(loop);

            // One of the pair moved on to the other - a = b - as a running total and its counter never are.
            bool MovedOn(string one, string other) => writes.Any(write =>
                write.Name == one && write.Value is Name { Identifier: var from } && from == other ||
                write.Name == other && write.Value is Name { Identifier: var back } && back == one);

            // a, b = b, a + b - or the sum kept in a third name - both of the two names written, one of them given back.
            foreach (var (_, _, value, _) in writes)
            {
                if (value is Binary { Operator: BinaryOperator.Add, Left: Name { Identifier: var left }, Right: Name { Identifier: var right } } pair &&
                    left != right && left != number && right != number && written.Contains(left) && written.Contains(right) &&
                    !stepped.Contains(left) && !stepped.Contains(right) && MovedOn(left, right) &&
                    (returnedNames.Contains(left) || returnedNames.Contains(right)))
                {
                    return new Found(loop.Span.Line, Fibonacci,
                        $"it keeps two numbers, `{left}` and `{right}`, and works out the next as their sum, `{Shown(pair)}`, each time round, " +
                        $"giving back `{string.Join("` or `", new[] { left, right }.Where(returnedNames.Contains))}`",
                        FibonacciAdvice, Sought: number);
                }
            }

            // fib.append(fib[i - 1] + fib[i - 2]), with an item of fib given back.
            foreach (var statement in body)
            {
                if (statement is Evaluate { Value: Call { Callee: Member { Target: Name { Identifier: var kept }, MemberName: "append" or "push" or "add" or "Add" or "push_back" }, Arguments: [{ Value: Binary { Operator: BinaryOperator.Add, Left: ElementAccess { Target: Name { Identifier: var one } }, Right: ElementAccess { Target: Name { Identifier: var other } } } pair }] } } &&
                    one == kept && other == kept && returned.Any(value => value is ElementAccess { Target: Name { Identifier: var given } } && given == kept))
                {
                    return new Found(loop.Span.Line, Fibonacci,
                        $"it adds to `{kept}` the sum of its two items before, `{Shown(pair)}`, each time round, and gives back an item of `{kept}`",
                        FibonacciAdvice, Sought: number);
                }
            }
        }

        return null;

        // How much less than its own number a call of itself is given - fib(n - 1) is 1, fib(n - 2) is 2 - or null for any other call.
        long? CallsItselfWithLess(Call call) =>
            call is { Callee: Name { Identifier: var called }, Arguments: [{ Value: Binary { Operator: BinaryOperator.Subtract, Left: Name { Identifier: var lessened }, Right: Literal { Kind: LiteralKind.Integer, Value: long less } } }] } &&
            called == function.Name && lessened == number && less is 1 or 2
                ? less
                : null;
    }

    /// <summary>
    /// Two lists merged into one: a loop run while both have items left - i &lt; len(first) and j &lt; len(second) - that
    /// compares the item it is at in each, first[i] with second[j], and puts one of them in a result each way the comparison
    /// goes - result.append(first[i]), or result[k] = second[j] - with the result given back. Whether what is left of either
    /// list is taken in after the loop is what the trial tries, not what the recognition asks.
    /// </summary>
    private static Found? MergeOf(IrFunction function, IReadOnlyList<Stmt> statements)
    {
        var parameters = function.Parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        // What each return gives back, by the list it is made from: result, result.concat(rest), result + first[i:].
        var givenBack = statements.OfType<Return>().Where(leave => MadeFrom(leave.Value) is not null)
            .GroupBy(leave => MadeFrom(leave.Value)!, StringComparer.Ordinal).ToDictionary(made => made.Key, made => made.First().Value!, StringComparer.Ordinal);

        foreach (var loop in statements.Where(statement => statement is While or For))
        {
            if (Condition(loop) is not Binary { Operator: BinaryOperator.And } both) continue;

            foreach (var choice in Body(loop).OfType<If>())
            {
                // first[i] compared with second[j], each a list of the function's own, and the two not the same list.
                var compared = choice.Condition is Binary { Operator: BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual,
                    Left: ElementAccess { Target: Name { Identifier: var one } } oneItem, Right: ElementAccess { Target: Name { Identifier: var other } } otherItem }
                    ? (one, other, oneItem, otherItem) : default;
                // The loop runs while both positions are inside their lists: i < len(first), or C's i < first_count.
                if (compared.one is null || compared.one == compared.other || !parameters.Contains(compared.one) || !parameters.Contains(compared.other) ||
                    !IrWalk.Names(compared.oneItem.Key).Any(position => Mentions(both, position)) ||
                    !IrWalk.Names(compared.otherItem.Key).Any(position => Mentions(both, position)))
                    continue;

                var intoWhenTrue = PutInto(choice.Then, compared.oneItem, compared.otherItem);
                var intoWhenFalse = PutInto(choice.Else, compared.oneItem, compared.otherItem);
                // The merged list is given back, or is one the function was given to fill: C's void merge(..., int result[]).
                if (intoWhenTrue is null || intoWhenTrue != intoWhenFalse) continue;
                var filled = parameters.Contains(intoWhenTrue) && intoWhenTrue != compared.one && intoWhenTrue != compared.other;
                if (!givenBack.TryGetValue(intoWhenTrue, out var given) && !filled) continue;
                var ending = given is not null ? $"gives back `{Shown(given)}`" : $"leaves them in `{intoWhenTrue}`, which it was given";

                var (first, second) = function.Parameters.Select(parameter => parameter.Name).ToList().IndexOf(compared.one) <
                                      function.Parameters.Select(parameter => parameter.Name).ToList().IndexOf(compared.other)
                    ? (compared.one, compared.other) : (compared.other, compared.one);
                return new Found(loop.Span.Line, Merge,
                    $"while both have items left, it compares `{Shown(compared.oneItem)}` with `{Shown(compared.otherItem)}` and puts one of them in " +
                    $"`{intoWhenTrue}` each time round, and {ending}",
                    "Merging two lists in order takes the smaller front item each time, until one list runs out - and then the rest of " +
                    "the other, which the loop stopped before: without it, those items are lost.",
                    List: first)
                {
                    SecondList = second,
                };
            }
        }

        return null;

        // The list a value is made from: result itself, result.concat(...), or result + first[i:] - or null.
        static string? MadeFrom(Expr? value) => value switch
        {
            Name { Identifier: var kept } => kept,
            Call { Callee: Member { Target: var on } } => MadeFrom(on),
            Binary { Operator: BinaryOperator.Add, Left: var left } => MadeFrom(left),
            _ => null,
        };

        // The list a branch puts the item it is at in - result.append(first[i]), result.push(...), result[k] = first[i] - or null.
        static string? PutInto(IReadOnlyList<Stmt> branch, ElementAccess oneItem, ElementAccess otherItem)
        {
            // An item of either list, at whatever its position is written as there - first[i], or Java's first[i++].
            bool IsAnItem(Expr value) => value is ElementAccess { Target: Name { Identifier: var from } } &&
                                         (from == IrText.Of(oneItem.Target) || from == IrText.Of(otherItem.Target));
            foreach (var statement in IrWalk.Statements(branch))
            {
                if (statement is Evaluate { Value: Call { Callee: Member { Target: Name { Identifier: var into }, MemberName: "append" or "push" or "add" or "Add" or "push_back" }, Arguments: [{ Value: var added }] } } &&
                    IsAnItem(added))
                    return into;
                foreach (var (target, name, value, _) in Writes([statement]))
                {
                    if (target is ElementAccess { Target: Name { Identifier: var placedIn } } && IsAnItem(value)) return placedIn;
                    // Go's result = append(result, first[i]), read as result = result + [first[i]].
                    if (name is not null && value is Binary { Operator: BinaryOperator.Add, Left: Name { Identifier: var grown }, Right: CollectionLiteral { Items: [var appended] } } &&
                        grown == name && IsAnItem(appended))
                        return name;
                }
            }

            return null;
        }
    }

    private const string FibonacciAdvice =
        "Each Fibonacci number is the two before it added - 0, 1, 1, 2, 3, 5, 8 - so both have to be kept until the next is " +
        "worked out: a = b and then b = a + b adds the new b to itself. A function calling itself twice for each number takes " +
        "time growing as fast as the numbers do; a loop takes one step for each.";

    private const string GreatestCommonDivisorAdvice =
        "Euclid's algorithm: the greatest number dividing both a and b is the greatest dividing both b and a % b, so the " +
        "pair can be replaced by that one until the second is 0 - every number divides 0 - when the first is the answer.";

    private const string FactorialAdvice =
        "n factorial is 1 times 2 times each number up to n, so the product has to start at 1 and take in n itself; 0 factorial " +
        "is 1, the product of no numbers at all. It grows so fast that 13 factorial is already too big for a 32-bit int.";

    /// <summary>
    /// Whether a loop runs as far as a number: a for or while loop whose test reads it - i &lt;= n, n &gt; 1 - or a for-each
    /// over a range of numbers that reads it, stepping by one: range(1, n + 1), but not range(n, 0, -2).
    /// </summary>
    private static bool RunsUpTo(Stmt loop, string number) => loop switch
    {
        ForEach { Items: Call { Callee: Name { Identifier: "range" } or Member { MemberName: "Range" }, Arguments: var bounds } } =>
            bounds.Any(bound => Mentions(bound.Value, number)) &&
            (bounds.Count is 1 or 2 ||
             bounds is [_, _, { Value: Literal { Kind: LiteralKind.Integer, Value: 1L } or Unary { Operator: UnaryOperator.Negate, Operand: Literal { Kind: LiteralKind.Integer, Value: 1L } } }]),
        ForEach => false,
        _ => Condition(loop) is { } test && Mentions(test, number),
    };

    /// <summary>Whether an expression gives a yes or a no: true or false written out, a comparison, and, or, or not of them.</summary>
    private static bool IsYesOrNo(Expr value) => value switch
    {
        Literal { Kind: LiteralKind.Boolean } => true,
        Binary { Operator: BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual } => true,
        Binary { Operator: BinaryOperator.And or BinaryOperator.Or } both => IsYesOrNo(both.Left) && IsYesOrNo(both.Right),
        Unary { Operator: UnaryOperator.Not, Operand: var operand } => IsYesOrNo(operand),
        _ => false,
    };

    private static bool IsLoop(Stmt statement) => statement is While or For or ForEach;

    /// <summary>
    /// The names a loop moves one at a time: what a for-each loop gives - for i in range(n) - and a name the loop, or
    /// its step, adds or takes 1 from: i++, i += 1, i = i - 1.
    /// </summary>
    private static HashSet<string> SteppedByOne(Stmt loop)
    {
        var stepped = new HashSet<string>(StringComparer.Ordinal);
        if (loop is ForEach { Target: Name each }) stepped.Add(each.Identifier);

        foreach (var (_, name, value, _) in WritesEachToItsOwn(IrWalk.Statements(loop is For forLoop ? forLoop.Step.Concat(forLoop.Body) : Body(loop))))
        {
            if (name is not null && value is Binary { Operator: BinaryOperator.Add or BinaryOperator.Subtract, Left: Name { Identifier: var from }, Right: Literal { Kind: LiteralKind.Integer, Value: 1L } } &&
                from == name)
                stepped.Add(name);
        }

        return stepped;
    }

    private static Expr? Condition(Stmt loop) => loop switch
    {
        While whileLoop => whileLoop.Condition,
        For forLoop => forLoop.Condition,
        _ => null,
    };

    private static IReadOnlyList<Stmt> Body(Stmt loop) => loop switch
    {
        While whileLoop => whileLoop.Body,
        For forLoop => forLoop.Body,
        ForEach eachLoop => eachLoop.Body,
        _ => [],
    };

    /// <summary>
    /// Every value given to something, whichever way the language writes it: x = v, var x = v, x += v, and an
    /// assignment inside an expression.
    /// </summary>
    private static IEnumerable<(Expr Target, string? Name, Expr Value, Expr? Assigned)> Writes(IEnumerable<Stmt> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case Assign { Target: var target, Value: var value, Compound: var compound }:
                    yield return (target, (target as Name)?.Identifier, compound is { } op ? new Binary(statement.Span, op, target, value) : value, null);
                    break;
                case Declare { Initial: { } initial } declared:
                    yield return (new Name(declared.Span, declared.Variable), declared.Variable, initial, null);
                    break;
                case Evaluate { Value: AssignValue { Target: var target, Value: var value } assigned }:
                    yield return (target, (target as Name)?.Identifier, value, assigned);
                    break;
            }
        }
    }

    /// <summary>
    /// Every value given to something, as Writes reads them, and each part of one given to several at once on its own:
    /// left, right = left + 1, right - 1 is left = left + 1 and right = right - 1.
    /// </summary>
    private static IEnumerable<(Expr Target, string? Name, Expr Value, Expr? Assigned)> WritesEachToItsOwn(IEnumerable<Stmt> statements)
    {
        foreach (var statement in statements)
        {
            foreach (var write in Writes([statement])) yield return write;

            // left, right = ... as a statement, or JavaScript's [a, b] = [b, a + b] as an assignment made in an expression.
            var (targets, values) = statement switch
            {
                Assign { Target: CollectionLiteral { Items: var named }, Value: CollectionLiteral { Items: var given }, Compound: null } => (named, given),
                Evaluate { Value: AssignValue { Target: CollectionLiteral { Items: var named }, Value: CollectionLiteral { Items: var given } } } => (named, given),
                _ => ((IReadOnlyList<Expr>?)null, (IReadOnlyList<Expr>?)null),
            };
            if (targets is not null && values is not null && targets.Count == values.Count)
            {
                for (var index = 0; index < targets.Count; index++)
                    yield return (targets[index], (targets[index] as Name)?.Identifier, values[index], null);
            }
        }
    }

    private static bool Mentions(Expr expression, string name) => IrWalk.Names(expression).Contains(name, StringComparer.Ordinal);

    /// <summary>Whether a value halves something: / 2, // 2, or >> 1, anywhere in it.</summary>
    private static bool Halves(Expr expression) => IrWalk.Within(expression).Any(part => part is
        Binary { Operator: BinaryOperator.Divide or BinaryOperator.FloorDivide, Right: Literal { Kind: LiteralKind.Integer, Value: 2L } } or
        Binary { Operator: BinaryOperator.ShiftRight, Right: Literal { Kind: LiteralKind.Integer, Value: 1L } });

    /// <summary>
    /// What the item at the middle is tested equal to - target in items[mid] == target - when it is a plain name.
    /// </summary>
    private static string? SoughtBeside(IEnumerable<Stmt> statements, ElementAccess probed) =>
        statements.SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within).OfType<Binary>()
            .Where(compare => compare.Operator == BinaryOperator.Equal)
            .Select(compare => IrText.Of(compare.Left) == IrText.Of(probed) ? NameOf(compare.Right) : IrText.Of(compare.Right) == IrText.Of(probed) ? NameOf(compare.Left) : null)
            .FirstOrDefault(name => name is not null);

    /// <summary>The item at a position that some comparison in the statements reads: items[mid] == target.</summary>
    private static ElementAccess? ComparedItemAt(IEnumerable<Stmt> statements, string position) =>
        statements.SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within)
            .OfType<Binary>().Where(compare => compare.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Less or
                BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual)
            .SelectMany(compare => new[] { compare.Left, compare.Right })
            .OfType<ElementAccess>().FirstOrDefault(item => item.Key is Name { Identifier: var key } && key == position);

    /// <summary>
    /// Two items of the same list next to each other: items[j] and items[j + 1], or items[j - 1] and items[j].
    /// </summary>
    private static bool Neighbours(ElementAccess first, ElementAccess second)
    {
        if (IrText.Of(first.Target) != IrText.Of(second.Target)) return false;

        static bool OneAfter(Expr later, Expr earlier) =>
            later is Binary { Operator: BinaryOperator.Add, Left: var from, Right: Literal { Kind: LiteralKind.Integer, Value: 1L } } && IrText.Of(from) == IrText.Of(earlier) ||
            earlier is Binary { Operator: BinaryOperator.Subtract, Left: var back, Right: Literal { Kind: LiteralKind.Integer, Value: 1L } } && IrText.Of(back) == IrText.Of(later);

        return OneAfter(second.Key, first.Key) || OneAfter(first.Key, second.Key);
    }

    private static bool SamePair((Expr First, Expr Second) swap, Expr one, Expr other) =>
        IrText.Of(swap.First) == IrText.Of(one) && IrText.Of(swap.Second) == IrText.Of(other) ||
        IrText.Of(swap.First) == IrText.Of(other) && IrText.Of(swap.Second) == IrText.Of(one);

    /// <summary>
    /// Two things exchanged, whichever way the language writes it: (a, b) = (b, a); held = a, a = b, b = held;
    /// std::swap(a, b).
    /// </summary>
    private static IEnumerable<(Expr First, Expr Second)> Swaps(IReadOnlyList<Stmt> statements)
    {
        for (var index = 0; index < statements.Count; index++)
        {
            var statement = statements[index];

            var (target, value) = statement switch
            {
                Assign assign => (assign.Target, assign.Value),
                Evaluate { Value: AssignValue assigned } => (assigned.Target, assigned.Value),
                _ => ((Expr?)null, (Expr?)null),
            };
            if (target is CollectionLiteral { Items: [var a, var b] } && value is CollectionLiteral { Items: [var c, var d] } &&
                IrText.Of(a) == IrText.Of(d) && IrText.Of(b) == IrText.Of(c))
                yield return (a, b);

            if (statement is Evaluate { Value: Call { Callee: Name { Identifier: "swap" } or Member { MemberName: "swap" }, Arguments: [var x, var y] } })
                yield return (Exchanged(x.Value), Exchanged(y.Value));

            // Java's Collections.swap(items, i, j): the items at i and at j, said as the call writes them.
            if (statement is Evaluate { Value: Call { Callee: Member { Target: Name { Identifier: "Collections" }, MemberName: "swap" }, Arguments: [var list, var one, var other] } swapCall })
            {
                var atOne = new ElementAccess(swapCall.Span, list.Value, one.Value);
                var atOther = new ElementAccess(swapCall.Span, list.Value, other.Value);
                _writtenAs?.TryAdd(atOne, $"{IrText.Of(list.Value)}.get({IrText.Of(one.Value)})");
                _writtenAs?.TryAdd(atOther, $"{IrText.Of(list.Value)}.get({IrText.Of(other.Value)})");
                yield return (atOne, atOther);
            }

            if (index + 2 < statements.Count && Writes([statement]).ToList() is [{ Name: { } held, Value: var first }] &&
                Writes([statements[index + 1]]).ToList() is [{ Target: var overwritten, Value: var second }] &&
                Writes([statements[index + 2]]).ToList() is [{ Target: var last, Value: Name { Identifier: var restored } }] &&
                restored == held && IrText.Of(overwritten) == IrText.Of(first) && IrText.Of(last) == IrText.Of(second))
                yield return (first, second);
        }

        // What a call that swaps is given: C++'s std::swap(a, b) is read as a call that sets both.
        static Expr Exchanged(Expr argument) => argument is AssignValue { Target: var given } ? given : argument;
    }
}
