using Microsoft.Extensions.DependencyInjection;

namespace Aegis.AspNetCore;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AegisEngine"/> and <see cref="IClaimsPrincipalMapper"/>
    /// for injection. The engine is a singleton built once, at first
    /// resolution, from every <see cref="IAttributeProvider"/> registered in
    /// the container at that point -- regardless of whether they came from
    /// <see cref="AegisOptions.AddAttributeProvider"/> or a separate call
    /// like <c>Aegis.Sql</c>'s <c>AddSqlServerAttributeProvider</c>, in any
    /// registration order.
    /// </summary>
    public static IServiceCollection AddAegis(this IServiceCollection services, Action<AegisOptions> configure)
    {
        var options = new AegisOptions();
        configure(options);

        if (options.PoliciesDirectory is null && options.PolicyProvider is null)
        {
            throw new InvalidOperationException(
                "AddAegis requires a policy source -- call options.AddPolicies(directory) or options.AddPolicyProvider(...).");
        }

        var claimsMappingOptions = new ClaimsMappingOptions();
        options.ConfigureClaimsMappingAction?.Invoke(claimsMappingOptions);
        services.AddSingleton(claimsMappingOptions);
        services.AddSingleton<IClaimsPrincipalMapper>(new ClaimsPrincipalMapper(claimsMappingOptions));

        foreach (var attributeProvider in options.AttributeProviders)
        {
            services.AddSingleton<IAttributeProvider>(attributeProvider);
        }

        services.AddSingleton(sp =>
        {
            var attributeProviders = sp.GetServices<IAttributeProvider>().ToArray();

            // Blocking on async here is deliberate: this factory only runs
            // once, at first resolution during host startup, and ASP.NET
            // Core has no captured SynchronizationContext to deadlock
            // against -- the common, safe pattern for async-sourced
            // singletons (SqlPolicyProvider's query, here).
            return options.PolicyProvider is not null
                ? AegisEngine.CreateAsync(options.PolicyProvider, attributeProviders).GetAwaiter().GetResult()
                : AegisEngine.Create(options.PoliciesDirectory!, attributeProviders);
        });

        return services;
    }
}