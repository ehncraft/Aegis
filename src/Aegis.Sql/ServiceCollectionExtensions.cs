using Aegis.Policies;

using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Sql;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a SQL Server-backed <see cref="IAttributeProvider"/>. Each
    /// call gets its own <see cref="ISqlQueryExecutor"/> built from its own
    /// options rather than a shared one -- combining this with
    /// <see cref="AddSqlServerPolicyProvider"/> against a different
    /// connection string would otherwise silently clobber one executor with
    /// the other's.
    /// </summary>
    public static IServiceCollection AddSqlServerAttributeProvider(
        this IServiceCollection services, Action<SqlAttributeProviderOptions> configure)
    {
        var options = new SqlAttributeProviderOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton<IAttributeProvider>(_ =>
            new SqlServerAttributeProvider(options, new SqlServerQueryExecutor(options.ConnectionString)));
        return services;
    }

    /// <summary>Registers a SQL Server-backed <see cref="IPolicyProvider"/>. See remarks on <see cref="AddSqlServerAttributeProvider"/>.</summary>
    public static IServiceCollection AddSqlServerPolicyProvider(
        this IServiceCollection services, Action<SqlPolicyStoreOptions> configure)
    {
        var options = new SqlPolicyStoreOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton<IPolicyProvider>(_ =>
            new SqlPolicyProvider(options, new SqlServerQueryExecutor(options.ConnectionString)));
        return services;
    }
}