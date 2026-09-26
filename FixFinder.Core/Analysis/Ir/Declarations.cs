namespace FixFinder.Core.Analysis.Ir;

public enum SourceLanguage { Python, Java, CSharp, C, Cpp, JavaScript, Go }

/// <summary>A type as the source declared it, or as far as it could be worked out.</summary>
public sealed record IrType(string Name, IReadOnlyList<IrType> Arguments, bool Nullable = false)
{
    public static IrType Unknown { get; } = Named("?");
    public static IrType Integer { get; } = Named("int");
    public static IrType Real { get; } = Named("float");
    public static IrType Boolean { get; } = Named("bool");
    public static IrType Text { get; } = Named("str");
    public static IrType Nothing { get; } = Named("None");

    public static IrType Named(string name, params IrType[] arguments) => new(name, arguments);

    public bool IsUnknown => Name == "?";

    /// <summary>
    /// A binary floating-point type - float or double, whatever the language calls it. Dividing one by zero gives
    /// infinity or NaN rather than an error. C#'s decimal is not one: dividing a decimal by zero does fail.
    /// </summary>
    public bool IsFloatingPoint => Name is "float" or "double" or "Float" or "Double" or "Single" or "float32" or "float64";

    public override string ToString() =>
        (Arguments.Count == 0 ? Name : $"{Name}[{string.Join(", ", Arguments)}]") + (Nullable ? "?" : "");
}

/// <summary>How a parameter takes its argument: in order, only by name, or all the rest (*args, **kwargs).</summary>
public enum ParameterKind { Normal, KeywordOnly, Rest, Keywords }

public sealed record IrParameter(SourceSpan Span, string Name, IrType Type, Expr? Default = null, ParameterKind Kind = ParameterKind.Normal);

public sealed record IrField(SourceSpan Span, string Name, IrType Type, Expr? Initial, bool IsStatic)
{
    /// <summary>Declared volatile: every thread sees each write to it straight away.</summary>
    public bool IsVolatile { get; init; }
}

public sealed record IrFunction(
    SourceSpan Span,
    string Name,
    string? Owner,
    IReadOnlyList<IrParameter> Parameters,
    IrType ReturnType,
    IReadOnlyList<Stmt> Body)
{
    public const string ModuleBody = "<module>";

    public bool IsStatic { get; init; }
    public bool IsConstructor { get; init; }
    public bool IsAsync { get; init; }
    public bool IsGenerator { get; init; }

    /// <summary>Whether the whole method holds its object's lock while it runs (Java's synchronized modifier).</summary>
    public bool IsSynchronized { get; init; }

    /// <summary>Whether a decorator wraps it - one that can change what calling it means, like @property.</summary>
    public bool IsDecorated { get; init; }

    /// <summary>The function this one is written inside - it can see and change that function's variables - or the module.</summary>
    public string? EnclosedBy { get; init; }

    /// <summary>Names this function declares as belonging to an outer scope (Python's global and nonlocal).</summary>
    public IReadOnlyList<string> OuterNames { get; init; } = [];

    /// <summary>Variables whose address this function takes, which any call it hands the pointer to can change.</summary>
    public IReadOnlyList<string> AddressTaken { get; init; } = [];

    /// <summary>
    /// For a JavaScript module's top level: the names it takes from other modules, by import or require. Empty for
    /// every other function, and for every other language, whose readers say what is imported in the code itself.
    /// </summary>
    public IReadOnlyList<ModuleImport> Imports { get; init; } = [];

    /// <summary>
    /// For a JavaScript module's top level: what it exports, from the name another module asks for to the value this
    /// module gives under it - one of its own names, or a function written in place. Null where the module may give more
    /// than one value under the name, or passes on what another module exports.
    /// </summary>
    public IReadOnlyDictionary<string, Expr?> Exports { get; init; } = new Dictionary<string, Expr?>();

    public string FullName => Owner is null ? Name : $"{Owner}.{Name}";
}

/// <summary>
/// A name one module takes from another: <c>import { key as local } from "specifier"</c>, or
/// <c>const { key: local } = require("specifier")</c>. With no key the name stands for whatever a plain
/// <c>require</c> gives - the module's exports object, or the one thing it exports whole.
/// </summary>
public sealed record ModuleImport(string Local, string Specifier, string? Key)
{
    /// <summary>The key a default import asks for: <c>import thing from</c>, which ES modules' <c>export default</c> gives.</summary>
    public const string DefaultExport = "default";

    /// <summary>
    /// The key <c>import * as all</c> asks for: an object holding every export as a member of its own, which can be
    /// called through - all.total() - but is never itself a function.
    /// </summary>
    public const string Namespace = "*";

    /// <summary>
    /// The key CommonJS's <c>module.exports = thing</c> is kept under: the module is the thing itself, which is what a
    /// plain require gives - and what a default import of a CommonJS module gives too.
    /// </summary>
    public const string WholeModule = "";
}

public sealed record IrClass(SourceSpan Span, string Name, IReadOnlyList<string> Bases, IReadOnlyList<IrField> Fields, IReadOnlyList<IrFunction> Methods);

/// <summary>A whole program in one form for every language: its classes, its functions, and what could not be read.</summary>
public sealed record IrProgram(
    SourceLanguage Language,
    IReadOnlyList<string> Files,
    IReadOnlyList<IrClass> Classes,
    IReadOnlyList<IrFunction> Functions,
    IReadOnlyList<string> Problems)
{
    public IEnumerable<IrFunction> AllFunctions => Functions.Concat(Classes.SelectMany(c => c.Methods));
}
