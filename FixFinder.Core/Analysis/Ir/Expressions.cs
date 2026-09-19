namespace FixFinder.Core.Analysis.Ir;

/// <summary>Where a piece of the program is written: 1-based lines, 0-based columns.</summary>
public sealed record SourceSpan(string File, int Line, int Column = 0, int EndLine = 0, int EndColumn = 0)
{
    public static SourceSpan None { get; } = new("", 0);

    public override string ToString() => $"{Path.GetFileName(File)}:{Line}";
}

public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide, FloorDivide, Modulo, Power,
    Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual,
    And, Or,
    BitAnd, BitOr, BitXor, ShiftLeft, ShiftRight,
    In, NotIn, Is, IsNot,
    MatrixMultiply,
}

public enum UnaryOperator { Negate, Plus, Not, BitNot }

public enum LiteralKind { Integer, Real, Text, Character, Boolean, Null }

public enum CollectionKind { List, Tuple, Set, Dictionary, Array }

/// <summary>A value computed by the program.</summary>
public abstract record Expr(SourceSpan Span);

public sealed record Literal(SourceSpan Span, LiteralKind Kind, object? Value) : Expr(Span);

public sealed record Name(SourceSpan Span, string Identifier) : Expr(Span);

public sealed record Unary(SourceSpan Span, UnaryOperator Operator, Expr Operand) : Expr(Span);

public sealed record Binary(SourceSpan Span, BinaryOperator Operator, Expr Left, Expr Right) : Expr(Span);

public sealed record Conditional(SourceSpan Span, Expr Test, Expr WhenTrue, Expr WhenFalse) : Expr(Span);

public sealed record Argument(string? Name, Expr Value);

public sealed record Call(SourceSpan Span, Expr Callee, IReadOnlyList<Argument> Arguments) : Expr(Span)
{
    public string? CalleeName => Callee switch
    {
        Name name => name.Identifier,
        Member member => member.MemberName,
        _ => null,
    };
}

public sealed record Member(SourceSpan Span, Expr Target, string MemberName) : Expr(Span);

public sealed record ElementAccess(SourceSpan Span, Expr Target, Expr Key) : Expr(Span);

public sealed record Slice(SourceSpan Span, Expr Target, Expr? Lower, Expr? Upper, Expr? Step) : Expr(Span);

public sealed record NewObject(SourceSpan Span, IrType Type, IReadOnlyList<Argument> Arguments) : Expr(Span);

public sealed record CollectionLiteral(SourceSpan Span, CollectionKind Kind, IReadOnlyList<Expr> Items, IReadOnlyList<Expr>? Keys = null)
    : Expr(Span);

/// <summary>An assignment used as a value: Python's <c>:=</c>, or <c>x = y</c>, <c>i++</c> inside a C-like expression.</summary>
public sealed record AssignValue(SourceSpan Span, Expr Target, Expr Value, bool ValueBeforeAssigning = false) : Expr(Span);

/// <summary>Whether a for-each loop has another item to give.</summary>
public sealed record MoreItems(SourceSpan Span, Expr Items) : Expr(Span);

/// <summary>The next item a for-each loop takes from what it walks through.</summary>
public sealed record NextItem(SourceSpan Span, Expr Items) : Expr(Span);

/// <summary>Something the front end could not represent: its value is unknown, but what it contains is still visited.</summary>
public sealed record Opaque(SourceSpan Span, string What, IReadOnlyList<Expr> Parts) : Expr(Span)
{
    public static Opaque Of(SourceSpan span, string what, params Expr[] parts) => new(span, what, parts);
}
