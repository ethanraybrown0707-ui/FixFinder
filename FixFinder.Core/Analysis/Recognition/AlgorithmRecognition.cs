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

    /// <summary>
    /// What calling the function on lists of its own showed, when it was called: every one right, or the first that was
    /// not.
    /// </summary>
    public string? Tried { get; init; }

    /// <summary>Whether it is a sort, whose result is the same items in order, rather than a search.</summary>
    public bool IsSort => Algorithm.EndsWith(" sort", StringComparison.Ordinal);

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

    public static IReadOnlyList<AlgorithmSeen> Of(IrProgram program)
    {
        var seen = new List<AlgorithmSeen>();
        foreach (var function in program.AllFunctions)
        {
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
                });
            }
        }

        return seen;
    }

    private sealed record Found(int Line, string Algorithm, string HowItWorks, string WhatToKnow, string? List = null, string? Sought = null)
    {
        public SearchGives? Gives { get; init; }
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

    private static readonly Func<IrFunction, IReadOnlyList<Stmt>, Found?>[] Recognisers =
    [
        BinarySearch,
        LinearSearch,
        BubbleSort,
        SelectionSort,
        InsertionSort,
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
                    $"it keeps a range from `{low}` to `{high}`, looks at its middle, `{mid}`, compares `{IrText.Of(probed)}` with what it looks for, " +
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
                        $"it goes through {over} one item at a time and stops at the first, `{IrText.Of(looked)}`, that equals `{IrText.Of(sought)}`",
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
                        $"it goes over `{IrText.Of(first.Target)}` again and again, and wherever two neighbours, `{IrText.Of(first)}` and `{IrText.Of(second)}`, " +
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
                        $"then swaps `{IrText.Of(swap.First)}` and `{IrText.Of(swap.Second)}`",
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
                        $"`{IrText.Of(shift.Target)} = {IrText.Of(shift.Value)}`, and puts `{key}` back in the gap",
                        "An insertion sort is quick on a list that is nearly in order, and compares about half of n squared pairs of n items on one in reverse order.",
                        NameOf(((ElementAccess)taken.Value).Target));
                }
            }
        }

        return null;
    }

    private static bool IsLoop(Stmt statement) => statement is While or For or ForEach;

    /// <summary>
    /// The names a loop moves one at a time: what a for-each loop gives - for i in range(n) - and a name the loop, or
    /// its step, adds or takes 1 from: i++, i += 1, i = i - 1.
    /// </summary>
    private static HashSet<string> SteppedByOne(Stmt loop)
    {
        var stepped = new HashSet<string>(StringComparer.Ordinal);
        if (loop is ForEach { Target: Name each }) stepped.Add(each.Identifier);

        foreach (var (_, name, value) in Writes(IrWalk.Statements(loop is For forLoop ? forLoop.Step.Concat(forLoop.Body) : Body(loop))))
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
    private static IEnumerable<(Expr Target, string? Name, Expr Value)> Writes(IEnumerable<Stmt> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case Assign { Target: var target, Value: var value, Compound: var compound }:
                    yield return (target, (target as Name)?.Identifier, compound is { } op ? new Binary(statement.Span, op, target, value) : value);
                    break;
                case Declare { Initial: { } initial } declared:
                    yield return (new Name(declared.Span, declared.Variable), declared.Variable, initial);
                    break;
                case Evaluate { Value: AssignValue { Target: var target, Value: var value } }:
                    yield return (target, (target as Name)?.Identifier, value);
                    break;
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
