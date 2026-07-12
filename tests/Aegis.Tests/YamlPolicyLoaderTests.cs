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

    [Fact]
    public void LoadDirectory_MergesImportedVariablesAndDerivedRoles()
    {
        var policies = YamlPolicyLoader.LoadDirectory(Path.Combine(FixturesPath, "Imports"));

        var accounts = Assert.Single(policies, p => p.Resource == "accounts");
        Assert.Equal("principal.department == 'finance'", accounts.Variables["isFinance"]);
        Assert.Equal("principal.id == resource.ownerId", accounts.DerivedRoles["owner"].When);
    }

    [Fact]
    public void LoadDirectory_DoesNotReturnLibraryFilesAsPolicies()
    {
        var policies = YamlPolicyLoader.LoadDirectory(Path.Combine(FixturesPath, "Imports"));

        Assert.DoesNotContain(policies, p => p.Resource == string.Empty);
        Assert.Single(policies);
    }

    [Fact]
    public void LoadDirectory_LocalVariableCollidesWithImport_Throws()
    {
        var ex = Assert.Throws<PolicyLoadException>(
            () => YamlPolicyLoader.LoadDirectory(Path.Combine(FixturesPath, "ImportCollision")));

        Assert.Contains("isFinance", ex.Message);
    }

    [Fact]
    public void LoadDirectory_UnknownImport_Throws()
    {
        var ex = Assert.Throws<PolicyLoadException>(
            () => YamlPolicyLoader.LoadDirectory(Path.Combine(FixturesPath, "ImportMissing")));

        Assert.Contains("nonexistent", ex.Message);
    }
}