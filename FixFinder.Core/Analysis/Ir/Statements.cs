namespace FixFinder.Core.Analysis.Ir;

/// <summary>One step of the program, in the structured form the source was written in.</summary>
public abstract record Stmt(SourceSpan Span);

public sealed record Evaluate(SourceSpan Span, Expr Value) : Stmt(Span);

public sealed record Assign(SourceSpan Span, Expr Target, Expr Value, BinaryOperator? Compound = null) : Stmt(Span);

public sealed record Declare(SourceSpan Span, string Variable, IrType Type, Expr? Initial) : Stmt(Span);

public sealed record If(SourceSpan Span, Expr Condition, IReadOnlyList<Stmt> Then, IReadOnlyList<Stmt> Else) : Stmt(Span);

public sealed record While(SourceSpan Span, Expr Condition, IReadOnlyList<Stmt> Body, IReadOnlyList<Stmt> Else, bool TestsFirst = true)
    : Stmt(Span);

public sealed record For(SourceSpan Span, IReadOnlyList<Stmt> Setup, Expr? Condition, IReadOnlyList<Stmt> Step, IReadOnlyList<Stmt> Body)
    : Stmt(Span);

public sealed record ForEach(SourceSpan Span, Expr Target, Expr Items, IReadOnlyList<Stmt> Body, IReadOnlyList<Stmt> Else) : Stmt(Span);

public sealed record Return(SourceSpan Span, Expr? Value) : Stmt(Span);

public sealed record Break(SourceSpan Span) : Stmt(Span);

public sealed record Continue(SourceSpan Span) : Stmt(Span);

public sealed record Throw(SourceSpan Span, Expr? Exception) : Stmt(Span);

public sealed record Handler(SourceSpan Span, IReadOnlyList<string> ExceptionTypes, string? Variable, IReadOnlyList<Stmt> Body);

public sealed record Try(
    SourceSpan Span, IReadOnlyList<Stmt> Body, IReadOnlyList<Handler> Handlers, IReadOnlyList<Stmt> Else, IReadOnlyList<Stmt> Finally)
    : Stmt(Span);

public sealed record AssertThat(SourceSpan Span, Expr Condition, Expr? Message) : Stmt(Span);

public sealed record SwitchCase(IReadOnlyList<Expr> Labels, IReadOnlyList<Stmt> Body, bool FallsThrough);

public sealed record Switch(SourceSpan Span, Expr Subject, IReadOnlyList<SwitchCase> Cases) : Stmt(Span);

/// <summary>A resource used for a block and closed after it: Python's <c>with</c>, C#'s <c>using</c>, Java's try-with-resources.</summary>
public sealed record Using(SourceSpan Span, Expr Resource, Expr? Variable, IReadOnlyList<Stmt> Body) : Stmt(Span);

/// <summary>A statement the front end could not represent; the names it may change are forgotten.</summary>
public sealed record OpaqueStmt(SourceSpan Span, string What, IReadOnlyList<string> MayAssign, IReadOnlyList<Expr> Parts) : Stmt(Span);
