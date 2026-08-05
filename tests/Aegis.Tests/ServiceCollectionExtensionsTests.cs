using Aegis.Policies;
using Aegis.Sql;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Aegis.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSqlServerAttributeProvider_Resolves()
    {
        var services = new ServiceCollection();
        services.AddSqlServerAttributeProvider(options =>
        {
            options.ConnectionString = "Server=.;Database=Test;Integrated Security=true;";
            options.PrincipalTable = "Users";
            options.PrincipalIdColumn = "UserId";
        });
        var provider = services.BuildServiceProvider();

        var attributeProvider = provider.GetRequiredService<IAttributeProvider>();

        Assert.NotNull(attributeProvider);
    }

    [Fact]
    public void AddSqlServerPolicyProvider_Resolves()
    {
        var services = new ServiceCollection();
        services.AddSqlServerPolicyProvider(options =>
        {
            options.ConnectionString = "Server=.;Database=Test;Integrated Security=true;";
        });
        var provider = services.BuildServiceProvider();

        var policyProvider = provider.GetRequiredService<IPolicyProvider>();

        Assert.NotNull(policyProvider);
    }
}