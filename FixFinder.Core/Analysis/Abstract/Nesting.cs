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
/// milliseconds between them. Both the index and each function's free names are worked out once here and kept - and so
/// is which functions are written inside which, as asking it of every pair of functions made a Go file of 50,000 lines
/// take five seconds here alone.
/// </remarks>
public sealed class Nesting
{
    private readonly IrProgram _program;
    private readonly Dictionary<string, IrFunction> _byName;
    private readonly Dictionary<IrFunction, HashSet<string>> _freeNames = new(ReferenceEqualityComparer.Instance);

    /// <summary>The functions written inside each function, by the name of the function they are inside.</summary>
    private readonly Dictionary<string, List<IrFunction>> _writtenInside = new(StringComparer.Ordinal);

    /// <summary>Every function but a module's own code, by the file it is in: what a module's variables can be changed by.</summary>
    private readonly Dictionary<string, List<IrFunction>> _inFile = new(StringComparer.OrdinalIgnoreCase);

    public Nesting(IrProgram program)
    {
        _program = program;
        _byName = program.AllFunctions
            .GroupBy(function => function.FullName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var inner in program.AllFunctions)
        {
            if (inner.Name != IrFunction.ModuleBody)
            {
                var file = inner.Span.File;
                if (!_inFile.TryGetValue(file, out var written)) _inFile[file] = written = [];
                written.Add(inner);
            }

            foreach (var outerName in EnclosingNames(inner))
            {
                if (!_writtenInside.TryGetValue(outerName, out var inside)) _writtenInside[outerName] = inside = [];
                inside.Add(inner);
            }
        }
    }

    /// <summary>
    /// Variables of <paramref name="function"/> that a function written inside it uses - or, for the module, that any
    /// function uses. Those can change during any call, so they are never treated as known.
    /// </summary>
    public HashSet<string> Volatile(IrFunction function)
    {
        var shared = new HashSet<string>(StringComparer.Ordinal);

        var inside = function.Name == IrFunction.ModuleBody
            ? _inFile.GetValueOrDefault(function.Span.File)
            : _writtenInside.GetValueOrDefault(function.FullName);
        foreach (var other in inside ?? [])
        {
            if (ReferenceEquals(other, function)) continue;
            shared.UnionWith(FreeNamesOf(other));
        }

        // A Go variable whose address was taken can be changed through that pointer by any code at all - a goroutine included.
        // C and C++ follow what can change such a variable more closely: see HandedAddresses.
        if (_program.Language == SourceLanguage.Go) shared.UnionWith(function.AddressTaken);
        shared.IntersectWith(IrWalk.LocalNames(function));
        return shared;
    }

    /// <summary>
    /// The names a function uses that belong to code around it. Only Python makes a variable of the function's own by
    /// assigning it - and there nonlocal or global say when it does not. Elsewhere a C++ lambda's rows += 7, or a
    /// JavaScript function declared inside another assigning its variable, changes the variable outside.
    /// </summary>
    private HashSet<string> FreeNamesOf(IrFunction function) =>
        _freeNames.TryGetValue(function, out var known)
            ? known
            : _freeNames[function] = new HashSet<string>(
                IrWalk.FreeNames(function, assigningDeclares: _program.Language == SourceLanguage.Python), StringComparer.Ordinal);

    /// <summary>The names of the functions a function is written inside, from the one around it outwards.</summary>
    private List<string> EnclosingNames(IrFunction inner)
    {
        var enclosing = new List<string>();

        for (var parent = inner.EnclosedBy; parent is not null && parent != IrFunction.ModuleBody; parent = _byName.GetValueOrDefault(parent)?.EnclosedBy)
        {
            // Two functions written on one line can each name the other as their parent, and following that is a walk
            // with no end.
            if (enclosing.Contains(parent, StringComparer.Ordinal)) break;
            enclosing.Add(parent);
        }

        return enclosing;
    }
}
