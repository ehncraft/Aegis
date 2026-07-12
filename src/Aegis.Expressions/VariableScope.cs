namespace Aegis.Expressions;

/// <summary>
/// The set of <c>${name}</c> variables available to expressions evaluated
/// against a particular <see cref="EvaluationContext"/> -- typically a
/// policy's own <c>variables:</c> section, already compiled once at load
/// time so evaluation never re-parses a variable's expression text.
/// </summary>
public sealed class VariableScope
{
    private readonly IReadOnlyDictionary<string, CompiledExpression> _variables;

    public VariableScope(IReadOnlyDictionary<string, CompiledExpression> variables)
    {
        _variables = variables;
    }

    public static VariableScope Empty { get; } = new(new Dictionary<string, CompiledExpression>());

    internal bool TryGet(string name, out CompiledExpression expression) =>
        _variables.TryGetValue(name, out expression!);
}