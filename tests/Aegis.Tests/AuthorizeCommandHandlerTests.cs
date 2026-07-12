using Aegis.Cli;

using Xunit;

namespace Aegis.Tests;

public class AuthorizeCommandHandlerTests
{
    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public async Task ExecuteAsync_Allowed_ReturnsZeroAndPrintsExplanationAsync()
    {
        var output = new StringWriter();

        var exitCode = await AuthorizeCommandHandler.ExecuteAsync(
            FixturesPath,
            principalId: "alice",
            roles: ["Finance"],
            principalAttributes: new Dictionary<string, object?>(),
            resourceKind: "invoices",
            resourceId: "INV-1",
            resourceAttributes: new Dictionary<string, object?>(),
            action: "view",
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"Effect\": \"allow\"", output.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_Denied_ReturnsOneAsync()
    {
        var output = new StringWriter();

        var exitCode = await AuthorizeCommandHandler.ExecuteAsync(
            FixturesPath,
            principalId: "bob",
            roles: ["Sales"],
            principalAttributes: new Dictionary<string, object?>(),
            resourceKind: "invoices",
            resourceId: "INV-1",
            resourceAttributes: new Dictionary<string, object?>(),
            action: "view",
            output,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"Effect\": \"deny\"", output.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_NonexistentPoliciesDirectory_ReturnsTwoAsync()
    {
        var output = new StringWriter();

        var exitCode = await AuthorizeCommandHandler.ExecuteAsync(
            Path.Combine(FixturesPath, "does-not-exist"),
            principalId: "alice",
            roles: [],
            principalAttributes: new Dictionary<string, object?>(),
            resourceKind: "invoices",
            resourceId: "INV-1",
            resourceAttributes: new Dictionary<string, object?>(),
            action: "view",
            output,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Contains("error:", output.ToString());
    }
}