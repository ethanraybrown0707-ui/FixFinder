namespace FixFinder.Core.Analysis.Ir;

/// <summary>
/// A function with each of its expressions changed by a rule, from the innermost out: the rule is given each expression
/// once what is inside it has been changed. What the rule leaves alone stays the very object it was, and an expression
/// written in two places - the i of i++, both the place written and the value read - is changed once and still shared.
/// </summary>
public sealed class IrRewrite(Func<Expr, Expr> rule)
{
    private readonly Dictionary<Expr, Expr> _done = new(ReferenceEqualityComparer.Instance);

    public IrFunction Function(IrFunction function)
    {
        var parameters = Same(function.Parameters, parameter => parameter.Default is { } given && Expression(given) is var changed && !ReferenceEquals(changed, given)
            ? parameter with { Default = changed }
            : parameter);
        var body = Block(function.Body);

        return ReferenceEquals(parameters, function.Parameters) && ReferenceEquals(body, function.Body)
            ? function
            : function with { Parameters = parameters, Body = body };
    }

    public IrClass Class(IrClass type)
    {
        var fields = Same(type.Fields, field => field.Initial is { } initial && Expression(initial) is var changed && !ReferenceEquals(changed, initial)
            ? field with { Initial = changed }
            : field);
        var methods = Same(type.Methods, Function);

        return ReferenceEquals(fields, type.Fields) && ReferenceEquals(methods, type.Methods) ? type : type with { Fields = fields, Methods = methods };
    }

    public IReadOnlyList<Stmt> Block(IReadOnlyList<Stmt> block) => Same(block, Statement);

    public Stmt Statement(Stmt statement) => statement switch
    {
        Evaluate evaluate => Changed(evaluate.Value) is var value && ReferenceEquals(value, evaluate.Value) ? evaluate : evaluate with { Value = value },
        Assign assign => (Changed(assign.Target), Changed(assign.Value)) is var (target, value) &&
                         ReferenceEquals(target, assign.Target) && ReferenceEquals(value, assign.Value)
            ? assign
            : assign with { Target = target, Value = value },
        Declare { Initial: { } initial } declare => Changed(initial) is var given && ReferenceEquals(given, initial) ? declare : declare with { Initial = given },
        If branch => (Changed(branch.Condition), Block(branch.Then), Block(branch.Else)) is var (condition, then, otherwise) &&
                     ReferenceEquals(condition, branch.Condition) && ReferenceEquals(then, branch.Then) && ReferenceEquals(otherwise, branch.Else)
            ? branch
            : branch with { Condition = condition, Then = then, Else = otherwise },
        While loop => (Changed(loop.Condition), Block(loop.Body), Block(loop.Else)) is var (condition, body, otherwise) &&
                      ReferenceEquals(condition, loop.Condition) && ReferenceEquals(body, loop.Body) && ReferenceEquals(otherwise, loop.Else)
            ? loop
            : loop with { Condition = condition, Body = body, Else = otherwise },
        For loop => (Block(loop.Setup), Maybe(loop.Condition), Block(loop.Step), Block(loop.Body)) is var (setup, condition, step, body) &&
                    ReferenceEquals(setup, loop.Setup) && ReferenceEquals(condition, loop.Condition) && ReferenceEquals(step, loop.Step) && ReferenceEquals(body, loop.Body)
            ? loop
            : loop with { Setup = setup, Condition = condition, Step = step, Body = body },
        ForEach loop => (Changed(loop.Target), Changed(loop.Items), Block(loop.Body), Block(loop.Else)) is var (target, items, body, otherwise) &&
                        ReferenceEquals(target, loop.Target) && ReferenceEquals(items, loop.Items) && ReferenceEquals(body, loop.Body) && ReferenceEquals(otherwise, loop.Else)
            ? loop
            : loop with { Target = target, Items = items, Body = body, Else = otherwise },
        Return leave => Maybe(leave.Value) is var value && ReferenceEquals(value, leave.Value) ? leave : leave with { Value = value },
        Labeled labeled => Block(labeled.Body) is var body && ReferenceEquals(body, labeled.Body) ? labeled : labeled with { Body = body },
        Throw raise => Maybe(raise.Exception) is var exception && ReferenceEquals(exception, raise.Exception) ? raise : raise with { Exception = exception },
        Try attempt => Tried(attempt),
        AssertThat check => (Changed(check.Condition), Maybe(check.Message)) is var (condition, message) &&
                            ReferenceEquals(condition, check.Condition) && ReferenceEquals(message, check.Message)
            ? check
            : check with { Condition = condition, Message = message },
        Switch choice => Chosen(choice),
        Using used => (Changed(used.Resource), Maybe(used.Variable), Block(used.Body)) is var (resource, variable, body) &&
                      ReferenceEquals(resource, used.Resource) && ReferenceEquals(variable, used.Variable) && ReferenceEquals(body, used.Body)
            ? used
            : used with { Resource = resource, Variable = variable, Body = body },
        OpaqueStmt opaque => Same(opaque.Parts, Changed) is var parts && ReferenceEquals(parts, opaque.Parts) ? opaque : opaque with { Parts = parts },
        _ => statement,
    };

    private Try Tried(Try attempt)
    {
        var body = Block(attempt.Body);
        var handlers = Same(attempt.Handlers, handler => Block(handler.Body) is var caught && ReferenceEquals(caught, handler.Body) ? handler : handler with { Body = caught });
        var otherwise = Block(attempt.Else);
        var last = Block(attempt.Finally);

        return ReferenceEquals(body, attempt.Body) && ReferenceEquals(handlers, attempt.Handlers) && ReferenceEquals(otherwise, attempt.Else) &&
               ReferenceEquals(last, attempt.Finally)
            ? attempt
            : attempt with { Body = body, Handlers = handlers, Else = otherwise, Finally = last };
    }

    private Switch Chosen(Switch choice)
    {
        var subject = Changed(choice.Subject);
        var cases = Same(choice.Cases, @case => (Same(@case.Labels, Changed), Block(@case.Body)) is var (labels, body) &&
                                               ReferenceEquals(labels, @case.Labels) && ReferenceEquals(body, @case.Body)
            ? @case
            : @case with { Labels = labels, Body = body });

        return ReferenceEquals(subject, choice.Subject) && ReferenceEquals(cases, choice.Cases) ? choice : choice with { Subject = subject, Cases = cases };
    }

    /// <summary>The expression with what is inside it changed first, then itself - each object once.</summary>
    public Expr Expression(Expr expression)
    {
        if (_done.TryGetValue(expression, out var done)) return done;

        var inside = expression switch
        {
            Unary unary => Changed(unary.Operand) is var operand && ReferenceEquals(operand, unary.Operand) ? unary : unary with { Operand = operand },
            Binary binary => (Changed(binary.Left), Changed(binary.Right)) is var (left, right) &&
                             ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right)
                ? binary
                : binary with { Left = left, Right = right },
            Conditional choice => (Changed(choice.Test), Changed(choice.WhenTrue), Changed(choice.WhenFalse)) is var (test, whenTrue, whenFalse) &&
                                  ReferenceEquals(test, choice.Test) && ReferenceEquals(whenTrue, choice.WhenTrue) && ReferenceEquals(whenFalse, choice.WhenFalse)
                ? choice
                : choice with { Test = test, WhenTrue = whenTrue, WhenFalse = whenFalse },
            Call call => (Changed(call.Callee), Given(call.Arguments)) is var (callee, arguments) &&
                         ReferenceEquals(callee, call.Callee) && ReferenceEquals(arguments, call.Arguments)
                ? call
                : call with { Callee = callee, Arguments = arguments },
            Member member => Changed(member.Target) is var target && ReferenceEquals(target, member.Target) ? member : member with { Target = target },
            ElementAccess element => (Changed(element.Target), Changed(element.Key)) is var (target, key) &&
                                     ReferenceEquals(target, element.Target) && ReferenceEquals(key, element.Key)
                ? element
                : element with { Target = target, Key = key },
            Slice slice => (Changed(slice.Target), Maybe(slice.Lower), Maybe(slice.Upper), Maybe(slice.Step)) is var (target, lower, upper, step) &&
                           ReferenceEquals(target, slice.Target) && ReferenceEquals(lower, slice.Lower) && ReferenceEquals(upper, slice.Upper) &&
                           ReferenceEquals(step, slice.Step)
                ? slice
                : slice with { Target = target, Lower = lower, Upper = upper, Step = step },
            NewObject created => Given(created.Arguments) is var arguments && ReferenceEquals(arguments, created.Arguments) ? created : created with { Arguments = arguments },
            Cast cast => Changed(cast.Value) is var value && ReferenceEquals(value, cast.Value) ? cast : cast with { Value = value },
            CollectionLiteral collection => (Same(collection.Items, Changed), collection.Keys is { } keys ? Same(keys, Changed) : null) is var (items, changedKeys) &&
                                            ReferenceEquals(items, collection.Items) && ReferenceEquals(changedKeys, collection.Keys)
                ? collection
                : collection with { Items = items, Keys = changedKeys },
            AssignValue assigned => (Changed(assigned.Target), Changed(assigned.Value)) is var (target, value) &&
                                    ReferenceEquals(target, assigned.Target) && ReferenceEquals(value, assigned.Value)
                ? assigned
                : assigned with { Target = target, Value = value },
            MoreItems more => Changed(more.Items) is var items && ReferenceEquals(items, more.Items) ? more : more with { Items = items },
            NextItem next => Changed(next.Items) is var items && ReferenceEquals(items, next.Items) ? next : next with { Items = items },
            Opaque opaque => Same(opaque.Parts, Changed) is var parts && ReferenceEquals(parts, opaque.Parts) ? opaque : opaque with { Parts = parts },
            _ => expression,
        };

        var result = rule(inside);
        _done[expression] = result;
        return result;
    }

    private Expr Changed(Expr expression) => Expression(expression);

    private Expr? Maybe(Expr? expression) => expression is null ? null : Expression(expression);

    private IReadOnlyList<Argument> Given(IReadOnlyList<Argument> arguments) =>
        Same(arguments, argument => Expression(argument.Value) is var value && ReferenceEquals(value, argument.Value) ? argument : argument with { Value = value });

    /// <summary>The list with each item changed, or the very same list when no item was.</summary>
    private static IReadOnlyList<T> Same<T>(IReadOnlyList<T> items, Func<T, T> change) where T : class
    {
        List<T>? changed = null;
        for (var i = 0; i < items.Count; i++)
        {
            var item = change(items[i]);
            if (changed is null && ReferenceEquals(item, items[i])) continue;

            changed ??= [.. items.Take(i)];
            changed.Add(item);
        }

        return changed ?? items;
    }
}
