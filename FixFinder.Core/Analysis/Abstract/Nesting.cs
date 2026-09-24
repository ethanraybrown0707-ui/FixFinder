using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Abstract;

/// <summary>
/// Which of a function's variables other code written inside it can see and change, for every function in one program.
/// </summary>
/// <remarks>
/// The answer for a single function depends on every other function in the program, so asking the question afresh each
/// time reads the whole program once per function - and the walk that finds which function encloses which rebuilt its
/// index of the program on every step, making it once per pair of functions. On a ten thousand line file that was
/// sixty-six of the sixty-seven seconds an entire check took, while the analyses it was preparing for took thirty-five
/// milliseconds between them. Both the index and each function's free names are worked out once here and kept.
/// </remarks>
public sealed class Nesting
{
    private readonly IrProgram _program;
    private readonly Dictionary<string, IrFunction> _byName;
    private readonly Dictionary<IrFunction, HashSet<string>> _freeNames = new(ReferenceEqualityComparer.Instance);

    public Nesting(IrProgram program)
    {
        _program = program;
        _byName = program.AllFunctions
            .GroupBy(function => function.FullName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Variables of <paramref name="function"/> that a function written inside it uses - or, for the module, that any
    /// function uses. Those can change during any call, so they are never treated as known.
    /// </summary>
    public HashSet<string> Volatile(IrFunction function)
    {
        var shared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var other in _program.AllFunctions)
        {
            if (ReferenceEquals(other, function) || !IsInside(other, function)) continue;
            shared.UnionWith(FreeNamesOf(other));
        }

        // A variable whose address was taken can be changed through that pointer by any call it was handed to.
        shared.UnionWith(function.AddressTaken);
        shared.IntersectWith(IrWalk.LocalNames(function));
        return shared;
    }

    private HashSet<string> FreeNamesOf(IrFunction function) =>
        _freeNames.TryGetValue(function, out var known)
            ? known
            : _freeNames[function] = new HashSet<string>(IrWalk.FreeNames(function), StringComparer.Ordinal);

    private bool IsInside(IrFunction inner, IrFunction outer)
    {
        if (outer.Name == IrFunction.ModuleBody) return inner.Name != IrFunction.ModuleBody && SameFile(inner, outer);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var parent = inner.EnclosedBy; parent is not null && parent != IrFunction.ModuleBody; parent = _byName.GetValueOrDefault(parent)?.EnclosedBy)
        {
            if (parent == outer.FullName) return true;

            // Two functions written on one line can each name the other as their parent, and following that is a walk
            // with no end.
            if (!seen.Add(parent)) break;
        }

        return false;
    }

    private static bool SameFile(IrFunction a, IrFunction b) =>
        string.Equals(a.Span.File, b.Span.File, StringComparison.OrdinalIgnoreCase);
}
