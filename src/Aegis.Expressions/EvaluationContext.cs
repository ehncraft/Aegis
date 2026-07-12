namespace Aegis.Expressions;

/// <summary>
/// Named variable scopes (e.g. "principal", "resource", "action") that a
/// <see cref="CompiledExpression"/> resolves dotted member paths against.
/// </summary>
public sealed class EvaluationContext
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, object?>> _scopes = new();
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);
    private VariableScope _variables = VariableScope.Empty;

    public EvaluationContext WithScope(string name, IReadOnlyDictionary<string, object?> values)
    {
        _scopes[name] = values;
        return this;
    }

    /// <summary>
    /// Makes <c>${name}</c> variables resolvable against <paramref name="variables"/>
    /// for the lifetime of this context.
    /// </summary>
    public EvaluationContext WithVariables(VariableScope variables)
    {
        _variables = variables;
        return this;
    }

    /// <summary>
    /// Resolves a <c>${name}</c> reference by evaluating its compiled
    /// expression against this same context. Guards against a variable
    /// (directly or transitively) referencing itself -- <see cref="PolicyValidator"/>
    /// in Aegis.Evaluator already rejects such cycles at load time via
    /// <see cref="CompiledExpression.ReferencedVariableNames"/>, so this is
    /// defense-in-depth, not the primary safeguard.
    /// </summary>
    internal object? ResolveVariable(string name)
    {
        if (!_variables.TryGet(name, out var expression))
        {
            throw new ExpressionEvaluationException($"Unknown variable '${{{name}}}'");
        }

        if (!_resolving.Add(name))
        {
            throw new ExpressionEvaluationException($"Circular variable reference involving '${{{name}}}'");
        }

        try
        {
            return expression.Evaluate(this);
        }
        finally
        {
            _resolving.Remove(name);
        }
    }

    internal bool TryResolve(IReadOnlyList<string> path, out object? value)
    {
        value = null;
        if (path.Count == 0 || !_scopes.TryGetValue(path[0], out var scope))
        {
            return false;
        }

        if (path.Count == 1)
        {
            value = scope;
            return true;
        }

        if (!scope.TryGetValue(path[1], out value))
        {
            return false;
        }

        for (var i = 2; i < path.Count; i++)
        {
            if (value is IReadOnlyDictionary<string, object?> nested && nested.TryGetValue(path[i], out value))
            {
                continue;
            }

            value = null;
            return false;
        }

        return true;
    }
}