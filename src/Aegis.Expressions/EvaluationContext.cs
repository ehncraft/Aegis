namespace Aegis.Expressions;

/// <summary>
/// Named variable scopes (e.g. "principal", "resource", "action") that a
/// <see cref="CompiledExpression"/> resolves dotted member paths against.
/// </summary>
public sealed class EvaluationContext
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, object?>> _scopes = new();

    public EvaluationContext WithScope(string name, IReadOnlyDictionary<string, object?> values)
    {
        _scopes[name] = values;
        return this;
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