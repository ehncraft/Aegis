using Aegis.Cli;

using Xunit;

namespace Aegis.Tests;

public class ValidateCommandHandlerTests
{
    [Fact]
    public void Execute_ValidPolicies_ReturnsZeroAndReportsCount()
    {
        var fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var output = new StringWriter();

        var exitCode = ValidateCommandHandler.Execute(fixturesPath, output);

        Assert.Equal(0, exitCode);
        Assert.Contains("valid", output.ToString());
    }

    [Fact]
    public void Execute_InvalidPolicies_ReturnsOneAndReportsEachError()
    {
        var fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "InvalidPolicies");
        var output = new StringWriter();

        var exitCode = ValidateCommandHandler.Execute(fixturesPath, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("invalid 'when' expression", output.ToString());
    }

    [Fact]
    public void Execute_NonexistentDirectory_ReturnsOneAndReportsError()
    {
        var output = new StringWriter();

        var exitCode = ValidateCommandHandler.Execute(
            Path.Combine(AppContext.BaseDirectory, "does-not-exist"), output);

        Assert.Equal(1, exitCode);
        Assert.Contains("error:", output.ToString());
    }
}