using Aegis.Sql;

using Xunit;

namespace Aegis.Tests;

public class SqlServerQueryExecutorTests
{
    [Fact]
    public void Create_ReturnsAnExecutor()
    {
        var executor = SqlServerQueryExecutor.Create("Server=.;Database=Test;Integrated Security=true;");

        Assert.NotNull(executor);
    }
}