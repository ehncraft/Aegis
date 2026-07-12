using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Sql;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers a SQL Server-backed <see cref="IAttributeProvider"/>.</summary>
    public static IServiceCollection AddSqlServerAttributeProvider(
        this IServiceCollection services, Action<SqlAttributeProviderOptions> configure)
    {
        var options = new SqlAttributeProviderOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton<ISqlQueryExecutor>(new SqlServerQueryExecutor(options.ConnectionString));
        services.AddSingleton<IAttributeProvider, SqlServerAttributeProvider>();
        return services;
    }
}