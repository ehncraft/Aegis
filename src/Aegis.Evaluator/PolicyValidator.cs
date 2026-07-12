using Aegis.Expressions;
using Aegis.Policies;

namespace Aegis;

/// <summary>
/// Validates a loaded policy set before it's ever evaluated, so a typo
/// surfaces once at startup instead of on the first request that happens
/// to hit it. Checks (aggregated, not fail-fast):
///
/// - every `when` expression parses;
/// - every action rule has a recognized effect (today, just `allow` --
///   an action listed with no matching key underneath it is almost always
///   a typo'd key, not an intentionally-empty rule, since YAML
///   deserialization silently drops unmatched properties);
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

            foreach (var (actionName, rule) in policy.Actions)
            {
                if (rule.Allow is null)
                {
                    errors.Add(
                        $"Resource '{policy.Resource}' ({source}), action '{actionName}': no recognized effect " +
                        "(expected an 'allow' rule) -- check for a typo'd key.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.Allow.When))
                {
                    continue;
                }

                try
                {
                    CompiledExpression.Parse(rule.Allow.When);
                }
                catch (ExpressionSyntaxException ex)
                {
                    errors.Add(
                        $"Resource '{policy.Resource}' ({source}), action '{actionName}': invalid 'when' " +
                        $"expression '{rule.Allow.When}' -- {ex.Message}");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new PolicyValidationException(errors);
        }
    }
}