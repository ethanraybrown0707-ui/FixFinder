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

    public override string ToString() =>
        (Arguments.Count == 0 ? Name : $"{Name}[{string.Join(", ", Arguments)}]") + (Nullable ? "?" : "");
}

public sealed record IrParameter(SourceSpan Span, string Name, IrType Type, Expr? Default = null);

public sealed record IrField(SourceSpan Span, string Name, IrType Type, Expr? Initial, bool IsStatic);

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

    /// <summary>The function this one is written inside - it can see and change that function's variables - or the module.</summary>
    public string? EnclosedBy { get; init; }

    /// <summary>Names this function declares as belonging to an outer scope (Python's global and nonlocal).</summary>
    public IReadOnlyList<string> OuterNames { get; init; } = [];

    public string FullName => Owner is null ? Name : $"{Owner}.{Name}";
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
