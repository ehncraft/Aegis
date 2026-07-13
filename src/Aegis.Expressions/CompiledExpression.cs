namespace Aegis.Expressions;

/// <summary>
/// A condition expression parsed once and evaluated many times, e.g.
/// <c>principal.department == resource.department</c>.
///
/// <see cref="Parse"/> runs the full pipeline: <see cref="Parser"/> builds
/// the parse tree, <see cref="IrLowering"/> lowers it to IR,
/// <see cref="SemanticAnalyzer"/> rejects provably-wrong expressions,
/// <see cref="IrOptimizer"/> constant-folds the result, and
/// <see cref="ExpressionCompiler"/> compiles the optimized IR to a delegate
/// via <see cref="System.Linq.Expressions"/>. <see cref="Evaluate"/> then
/// just invokes that delegate -- no per-call tree walk or node-type
/// dispatch.
/// </summary>
public sealed class CompiledExpression
{
    private readonly Func<EvaluationContext, object?> _compiled;

    private CompiledExpression(string source, Func<EvaluationContext, object?> compiled)
    {
        Source = source;
        _compiled = compiled;
    }

    public string Source { get; }

    /// <summary>
    /// The <c>${name}</c> variables this expression references, in the order
    /// they first appear. Exposed so <c>PolicyValidator</c> (Aegis.Evaluator)
    /// can check for undefined variables and circular references without
    /// needing access to the underlying AST/IR types.
    /// </summary>
    public IReadOnlyList<string> ReferencedVariableNames { get; private set; } = [];

    public static CompiledExpression Parse(string source)
    {
        var ast = Parser.Parse(source);
        var ir = IrLowering.Lower(ast);
        var referencedVariableNames = IrVariableCollector.Collect(ir);

        SemanticAnalyzer.Validate(ir);

        var optimized = IrOptimizer.Optimize(ir);
        var compiled = ExpressionCompiler.Compile(optimized);

        return new CompiledExpression(source, compiled) { ReferencedVariableNames = referencedVariableNames };
    }

    /// <summary>Evaluates this expression, returning its raw result (not necessarily boolean).</summary>
    public object? Evaluate(EvaluationContext context) => _compiled(context);

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
}
