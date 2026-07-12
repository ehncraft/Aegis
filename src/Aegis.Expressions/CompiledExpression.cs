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

    /// <summary>
    /// The <c>${name}</c> variables this expression references, in the order
    /// they first appear. Exposed so <c>PolicyValidator</c> (Aegis.Evaluator)
    /// can check for undefined variables and circular references without
    /// needing access to the underlying AST types.
    /// </summary>
    public IReadOnlyList<string> ReferencedVariableNames { get; private set; } = [];

    public static CompiledExpression Parse(string source)
    {
        var root = Parser.Parse(source);
        return new CompiledExpression(source, root) { ReferencedVariableNames = CollectVariableReferences(root) };
    }

    /// <summary>Evaluates this expression, returning its raw result (not necessarily boolean).</summary>
    public object? Evaluate(EvaluationContext context) => Evaluator.Evaluate(_root, context);

    public bool EvaluateBoolean(EvaluationContext context)
    {
        var result = Evaluate(context);
        if (result is bool b)
        {
            return b;
        }

        throw new ExpressionEvaluationException(
            $"Expression '{Source}' did not evaluate to a boolean (got '{result}')");
    }

    public override string ToString() => Source;

    private static List<string> CollectVariableReferences(Expr expr)
    {
        var names = new List<string>();
        Walk(expr);
        return names;

        void Walk(Expr node)
        {
            switch (node)
            {
                case VariableExpr variable:
                    names.Add(variable.Name);
                    break;
                case UnaryExpr unary:
                    Walk(unary.Operand);
                    break;
                case BinaryExpr binary:
                    Walk(binary.Left);
                    Walk(binary.Right);
                    break;
            }
        }
    }
}