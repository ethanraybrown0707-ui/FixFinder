using System.Globalization;
using FixFinder.Core.Analysis.Flow;

namespace FixFinder.Core.Analysis.Ir;

/// <summary>Writes IR and control-flow graphs as short readable text, for tests, logs and messages.</summary>
public static class IrText
{
    public static string Of(Expr expression) => Write(expression, nested: false);

    private static string Write(Expr expression, bool nested) => expression switch
    {
        Literal { Kind: LiteralKind.Text } text => $"\"{text.Value}\"",
        Literal { Kind: LiteralKind.Character } character => $"'{character.Value}'",
        Literal { Kind: LiteralKind.Null } => "null",
        Literal { Kind: LiteralKind.Boolean, Value: bool flag } => flag ? "true" : "false",
        Literal literal => Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? "",
        Name name => name.Identifier,
        Unary unary => Symbol(unary.Operator) + Write(unary.Operand, nested: true),
        Binary binary => Grouped($"{Write(binary.Left, true)} {Symbol(binary.Operator)} {Write(binary.Right, true)}", nested),
        Conditional choice => Grouped($"{Write(choice.Test, true)} ? {Write(choice.WhenTrue, true)} : {Write(choice.WhenFalse, true)}", nested),
        Call call => $"{Write(call.Callee, true)}({Arguments(call.Arguments)})",
        Member member => $"{Write(member.Target, true)}.{member.MemberName}",
        ElementAccess element => $"{Write(element.Target, true)}[{Of(element.Key)}]",
        Slice slice => $"{Write(slice.Target, true)}[{Optional(slice.Lower)}:{Optional(slice.Upper)}{(slice.Step is null ? "" : ":" + Of(slice.Step))}]",
        NewObject created => $"new {created.Type}({Arguments(created.Arguments)})",
        Cast cast => $"({cast.Type}) {Write(cast.Value, true)}",
        CollectionLiteral { Kind: CollectionKind.Dictionary } dictionary =>
            "{" + string.Join(", ", dictionary.Keys!.Zip(dictionary.Items, (k, v) => $"{Of(k)}: {Of(v)}")) + "}",
        CollectionLiteral collection => collection.Kind switch
        {
            CollectionKind.Tuple => $"({string.Join(", ", collection.Items.Select(Of))})",
            CollectionKind.Set or CollectionKind.Array => "{" + string.Join(", ", collection.Items.Select(Of)) + "}",
            _ => $"[{string.Join(", ", collection.Items.Select(Of))}]",
        },
        AssignValue assigned => Grouped($"{Write(assigned.Target, true)} := {Write(assigned.Value, true)}", nested),
        MoreItems more => $"more({Of(more.Items)})",
        NextItem next => $"next({Of(next.Items)})",
        Opaque opaque => $"<{opaque.What}>",
        _ => expression.GetType().Name,
    };

    private static string Grouped(string text, bool nested) => nested ? $"({text})" : text;

    private static string Optional(Expr? expression) => expression is null ? "" : Of(expression);

    private static string Arguments(IReadOnlyList<Argument> arguments) =>
        string.Join(", ", arguments.Select(a => a.Name is null ? Of(a.Value) : $"{a.Name}={Of(a.Value)}"));

    public static string Symbol(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.FloorDivide => "//",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Power => "**",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.And => "and",
        BinaryOperator.Or => "or",
        BinaryOperator.BitAnd => "&",
        BinaryOperator.BitOr => "|",
        BinaryOperator.BitXor => "^",
        BinaryOperator.ShiftLeft => "<<",
        BinaryOperator.ShiftRight => ">>",
        BinaryOperator.In => "in",
        BinaryOperator.NotIn => "not in",
        BinaryOperator.Is => "is",
        BinaryOperator.IsNot => "is not",
        BinaryOperator.MatrixMultiply => "@",
        _ => op.ToString(),
    };

    private static string Symbol(UnaryOperator op) => op switch
    {
        UnaryOperator.Negate => "-",
        UnaryOperator.Plus => "+",
        UnaryOperator.Not => "not ",
        _ => "~",
    };

    public static string Of(Instruction instruction) => instruction switch
    {
        AssignInstruction assign => $"{Of(assign.Target)} = {Of(assign.Value)}",
        EvaluateInstruction evaluate => Of(evaluate.Value),
        DeclareInstruction declare => $"declare {declare.Variable}: {declare.Type}",
        ForgetInstruction forget => $"forget {string.Join(", ", forget.Names)}",
        ReleaseInstruction release => $"release {Of(release.Resource)}",
        _ => instruction.GetType().Name,
    };

    public static string Of(Terminator terminator) => terminator switch
    {
        Jump jump => $"goto B{jump.Target}",
        Branch branch => $"if {Of(branch.Condition)} then B{branch.WhenTrue} else B{branch.WhenFalse}",
        Leave leave => leave.Value is null ? "return" : $"return {Of(leave.Value)}",
        Raise raise => raise.Exception is null ? "raise" : $"raise {Of(raise.Exception)}",
        Finish => "end",
        _ => terminator.GetType().Name,
    };

    /// <summary>One line per block: its instructions, how it ends, and where exceptions from it go.</summary>
    public static string Of(ControlFlowGraph graph) => string.Join("\n", graph.Blocks.Select(block =>
        $"B{block.Id}{(block.IsLoopHead ? "*" : "")}: " +
        string.Concat(block.Instructions.Select(i => Of(i) + "; ")) +
        (block.Terminator is { } end ? Of(end) : "(open)") +
        (block.ExceptionTargets.Count > 0 ? $" ! {string.Join(",", block.ExceptionTargets.Select(t => "B" + t))}" : "")));
}
