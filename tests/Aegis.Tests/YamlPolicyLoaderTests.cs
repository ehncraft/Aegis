using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

public class YamlPolicyLoaderTests
{
    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void LoadFile_ParsesResourceAndActions()
    {
        var policy = YamlPolicyLoader.LoadFile(Path.Combine(FixturesPath, "invoices.yaml"));

        Assert.Equal("invoices", policy.Resource);
        Assert.Equal("invoice-policy", policy.Name);
        Assert.Equal(["Finance"], policy.Actions["view"].Allow!.Roles);
        Assert.Equal("principal.department == resource.department", policy.Actions["approve"].Allow!.When);
    }

    [Fact]
    public void LoadDirectory_LoadsEveryYamlFile()
    {
        var policies = YamlPolicyLoader.LoadDirectory(FixturesPath);

        Assert.Contains(policies, p => p.Resource == "invoices");
    }

    [Fact]
    public void LoadDirectory_ThrowsWhenDirectoryMissing()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => YamlPolicyLoader.LoadDirectory(Path.Combine(FixturesPath, "does-not-exist")));
    }
}