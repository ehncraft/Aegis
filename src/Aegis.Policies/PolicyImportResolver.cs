namespace Aegis.Policies;

/// <summary>Merges each policy's <see cref="ResourcePolicy.Imports"/> libraries into it.</summary>
internal static class PolicyImportResolver
{
    public static void ResolveImports(IReadOnlyList<ResourcePolicy> policies, IReadOnlyList<PolicyLibrary> libraries)
    {
        var librariesByName = libraries.ToDictionary(l => l.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var policy in policies)
        {
            foreach (var importName in policy.Imports)
            {
                if (!librariesByName.TryGetValue(importName, out var library))
                {
                    throw new PolicyLoadException(
                        policy.Source ?? policy.Resource,
                        new InvalidOperationException($"Unknown import '{importName}'."));
                }

                foreach (var (varName, varExpression) in library.Variables)
                {
                    if (!policy.Variables.TryAdd(varName, varExpression))
                    {
                        throw new PolicyLoadException(
                            policy.Source ?? policy.Resource,
                            new InvalidOperationException(
                                $"Variable '{varName}' is defined locally and also imported from '{importName}' -- rename one of them."));
                    }
                }

                foreach (var (roleName, roleDefinition) in library.DerivedRoles)
                {
                    if (!policy.DerivedRoles.TryAdd(roleName, roleDefinition))
                    {
                        throw new PolicyLoadException(
                            policy.Source ?? policy.Resource,
                            new InvalidOperationException(
                                $"Derived role '{roleName}' is defined locally and also imported from '{importName}' -- rename one of them."));
                    }
                }
            }
        }
    }
}