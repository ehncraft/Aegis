using Aegis.Policies;

namespace Aegis.AspNetCore;

/// <summary>Configuration surface for <c>services.AddAegis(...)</c>.</summary>
public sealed class AegisOptions
{
    internal string? PoliciesDirectory { get; private set; }

    internal IPolicyProvider? PolicyProvider { get; private set; }

    internal List<IAttributeProvider> AttributeProviders { get; } = [];

    internal Action<ClaimsMappingOptions>? ConfigureClaimsMappingAction { get; private set; }

    /// <summary>Loads policies from every YAML file in <paramref name="directory"/>.</summary>
    public void AddPolicies(string directory) => PoliciesDirectory = directory;

    /// <summary>
    /// Loads policies from a pluggable source instead of the filesystem --
    /// a SQL Server table (<c>Aegis.Sql</c>'s <c>SqlPolicyProvider</c>), etc.
    /// </summary>
    public void AddPolicyProvider(IPolicyProvider policyProvider) => PolicyProvider = policyProvider;

    /// <summary>
    /// Registers an attribute provider inline. Providers already registered
    /// in DI (e.g. via <c>Aegis.Sql</c>'s <c>AddSqlServerAttributeProvider</c>)
    /// are picked up automatically and don't need this -- it's for a
    /// one-off instance that isn't otherwise in the container.
    /// </summary>
    public void AddAttributeProvider(IAttributeProvider attributeProvider) =>
        AttributeProviders.Add(attributeProvider);

    /// <summary>Configures how <c>HttpContext.User</c> claims map to <see cref="AegisPrincipal"/>. Uses <see cref="ClaimsMappingOptions"/> defaults if never called.</summary>
    public void ConfigureClaimsMapping(Action<ClaimsMappingOptions> configure) =>
        ConfigureClaimsMappingAction = configure;
}