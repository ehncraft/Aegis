using Aegis.Expressions;
using Aegis.Policies;

namespace Aegis;

/// <summary>
/// Validates a loaded policy set before it's ever evaluated, so a typo
/// surfaces once at startup instead of on the first request that happens
/// to hit it. Checks (aggregated, not fail-fast):
///
/// - every `when` expression -- action rules, derived roles, and variables
///   themselves -- parses;
/// - every action rule has a recognized effect (today, just `allow` --
///   an action listed with no matching key underneath it is almost always
///   a typo'd key, not an intentionally-empty rule, since YAML
///   deserialization silently drops unmatched properties);
/// - every `${name}` reference resolves to a variable defined on the same
///   policy (locally or via `imports`);
/// - no variable's expression (transitively) references itself;
/// - no two policies claim the same resource.
/// </summary>
public static class PolicyValidator
{
    public static void Validate(IReadOnlyList<ResourcePolicy> policies)
    {
        var errors = new List<string>();
        var resourceSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var policy in policies)
        {
            var source = policy.Source ?? policy.Name ?? policy.Resource;

            if (resourceSources.TryGetValue(policy.Resource, out var firstSource))
            {
                errors.Add(
                    $"Duplicate resource '{policy.Resource}': already defined in '{firstSource}', also defined in '{source}'.");
            }
            else
            {
                resourceSources[policy.Resource] = source;
            }

            var compiledVariables = new Dictionary<string, CompiledExpression>(StringComparer.Ordinal);

            foreach (var (varName, varExpression) in policy.Variables)
            {
                try
                {
                    compiledVariables[varName] = CompiledExpression.Parse(varExpression);
                }
                catch (ExpressionSyntaxException ex)
                {
                    errors.Add(
                        $"Resource '{policy.Resource}' ({source}), variable '${{{varName}}}': invalid expression " +
                        $"'{varExpression}' -- {ex.Message}");
                }
            }

            foreach (var (varName, compiled) in compiledVariables)
            {
                CheckUndefinedVariables(policy, source, $"variable '${{{varName}}}'", compiled, errors);
            }

            DetectVariableCycles(policy, source, compiledVariables, errors);

            foreach (var (roleName, roleDefinition) in policy.DerivedRoles)
            {
                ValidateDerivedRole(policy, source, roleName, roleDefinition, errors);
            }

            foreach (var (actionName, rule) in policy.Actions)
            {
                if (rule.Allow is null && rule.Forbid is null)
                {
                    errors.Add(
                        $"Resource '{policy.Resource}' ({source}), action '{actionName}': no recognized effect " +
                        "(expected an 'allow' or 'forbid' rule) -- check for a typo'd key.");
                    continue;
                }

                ValidateActionEffectWhen(policy, source, actionName, "allow", rule.Allow?.When, errors);
                ValidateActionEffectWhen(policy, source, actionName, "forbid", rule.Forbid?.When, errors);
            }
        }

        if (errors.Count > 0)
        {
            throw new PolicyValidationException(errors);
        }
    }

    /// <summary>
    /// Shared by <c>allow.when</c> and <c>forbid.when</c> -- <paramref name="effectName"/>
    /// is <c>"allow"</c> or <c>"forbid"</c>, used only in error messages.
    /// </summary>
    private static void ValidateActionEffectWhen(
        ResourcePolicy policy, string source, string actionName, string effectName, string? when, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(when))
        {
            return;
        }

        try
        {
            var compiled = CompiledExpression.Parse(when);
            CheckUndefinedVariables(policy, source, $"action '{actionName}' {effectName}", compiled, errors);
        }
        catch (ExpressionSyntaxException ex)
        {
            errors.Add(
                $"Resource '{policy.Resource}' ({source}), action '{actionName}' '{effectName}': invalid 'when' " +
                $"expression '{when}' -- {ex.Message}");
        }
    }

    /// <summary>
    /// A derived role is either ABAC-style (<c>when</c>) or ReBAC-style
    /// (<c>in</c>, Cedar's entity-hierarchy membership check) -- exactly
    /// one, not both, not neither.
    /// </summary>
    private static void ValidateDerivedRole(
        ResourcePolicy policy, string source, string roleName, DerivedRoleDefinition roleDefinition, List<string> errors)
    {
        var hasWhen = !string.IsNullOrWhiteSpace(roleDefinition.When);
        var hasIn = roleDefinition.In is not null;

        if (hasWhen && hasIn)
        {
            errors.Add(
                $"Resource '{policy.Resource}' ({source}), derived role '{roleName}': specifies both 'when' and " +
                "'in' -- a derived role is either condition-based or relationship-based, not both.");
            return;
        }

        if (!hasWhen && !hasIn)
        {
            errors.Add(
                $"Resource '{policy.Resource}' ({source}), derived role '{roleName}': missing 'when' condition or 'in'.");
            return;
        }

        if (hasWhen)
        {
            try
            {
                var compiled = CompiledExpression.Parse(roleDefinition.When!);
                CheckUndefinedVariables(policy, source, $"derived role '{roleName}'", compiled, errors);
            }
            catch (ExpressionSyntaxException ex)
            {
                errors.Add(
                    $"Resource '{policy.Resource}' ({source}), derived role '{roleName}': invalid 'when' " +
                    $"expression '{roleDefinition.When}' -- {ex.Message}");
            }

            return;
        }

        var hierarchyCheck = roleDefinition.In!;

        if (string.IsNullOrWhiteSpace(hierarchyCheck.Type))
        {
            errors.Add(
                $"Resource '{policy.Resource}' ({source}), derived role '{roleName}': 'in' requires a 'type'.");
        }

        if (string.IsNullOrWhiteSpace(hierarchyCheck.Id))
        {
            errors.Add(
                $"Resource '{policy.Resource}' ({source}), derived role '{roleName}': 'in' requires an 'id'.");
            return;
        }

        try
        {
            var compiled = CompiledExpression.Parse(hierarchyCheck.Id);
            CheckUndefinedVariables(policy, source, $"derived role '{roleName}' id", compiled, errors);
        }
        catch (ExpressionSyntaxException ex)
        {
            errors.Add(
                $"Resource '{policy.Resource}' ({source}), derived role '{roleName}': invalid 'in.id' " +
                $"expression '{hierarchyCheck.Id}' -- {ex.Message}");
        }
    }

    private static void CheckUndefinedVariables(
        ResourcePolicy policy, string source, string location, CompiledExpression compiled, List<string> errors)
    {
        foreach (var name in compiled.ReferencedVariableNames.Distinct(StringComparer.Ordinal))
        {
            if (!policy.Variables.ContainsKey(name))
            {
                errors.Add(
                    $"Resource '{policy.Resource}' ({source}), {location}: references undefined variable '${{{name}}}'.");
            }
        }
    }

    /// <summary>
    /// Depth-first cycle detection over the variable dependency graph
    /// (edges only to variables that themselves compiled successfully --
    /// an undefined reference is already reported by <see cref="CheckUndefinedVariables"/>
    /// and shouldn't also produce a confusing "cycle").
    /// </summary>
    private static void DetectVariableCycles(
        ResourcePolicy policy,
        string source,
        Dictionary<string, CompiledExpression> compiledVariables,
        List<string> errors)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var name in compiledVariables.Keys)
        {
            if (!state.ContainsKey(name))
            {
                Visit(name);
            }
        }

        void Visit(string name)
        {
            if (state.TryGetValue(name, out var visitState))
            {
                if (visitState == 1)
                {
                    var cycleStart = path.IndexOf(name);
                    var cycle = string.Join(" -> ", path.Skip(cycleStart).Append(name));
                    errors.Add($"Resource '{policy.Resource}' ({source}): circular variable reference: {cycle}");
                }

                return;
            }

            state[name] = 1;
            path.Add(name);

            if (compiledVariables.TryGetValue(name, out var compiled))
            {
                foreach (var dependency in compiled.ReferencedVariableNames.Distinct(StringComparer.Ordinal))
                {
                    if (compiledVariables.ContainsKey(dependency))
                    {
                        Visit(dependency);
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[name] = 2;
        }
    }
}