namespace Aegis.Expressions;

/// <summary>
/// A condition expression parsed once and evaluated many times, e.g.
/// <c>principal.department == resource.department</c>.
///
/// This is a tree-walking evaluator, not a compiled delegate — good enough
/// for a first slice. Compiling to <see cref="Func{T,TResult}"/> trees is a
/// later optimization once the expression surface is stable.
/// </summary>
public sealed class CompiledExpression
{
    private readonly Expr _root;

    private CompiledExpression(string source, Expr root)
    {
        Source = source;
        _root = root;
    }

    public string Source { get; }

    public static CompiledExpression Parse(string source) => new(source, Parser.Parse(source));

    public bool EvaluateBoolean(EvaluationContext context)
    {
        var result = Evaluator.Evaluate(_root, context);
        if (result is bool b)
        {
            return b;
        }

        throw new ExpressionEvaluationException(
            $"Expression '{Source}' did not evaluate to a boolean (got '{result}')");
    }

    public override string ToString() => Source;
}
