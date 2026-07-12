using Xunit;

namespace Aegis.Tests;

public class DecisionCacheTests
{
    [Fact]
    public void BuildKey_SameInputs_ProducesTheSameKey()
    {
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"],
            attributes: new Dictionary<string, object?> { ["department"] = "finance" });
        var resource = AegisResource.Create("invoices", "INV-1");

        var key1 = DecisionCache.BuildKey(principal, resource, "view");
        var key2 = DecisionCache.BuildKey(principal, resource, "view");

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void BuildKey_AttributeInsertionOrderDoesNotMatter()
    {
        var principalA = AegisPrincipal.Create("alice",
            attributes: new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 });
        var principalB = AegisPrincipal.Create("alice",
            attributes: new Dictionary<string, object?> { ["b"] = 2, ["a"] = 1 });
        var resource = AegisResource.Create("invoices", "INV-1");

        Assert.Equal(
            DecisionCache.BuildKey(principalA, resource, "view"),
            DecisionCache.BuildKey(principalB, resource, "view"));
    }

    [Fact]
    public void BuildKey_RoleOrderDoesNotMatter()
    {
        var principalA = AegisPrincipal.Create("alice", roles: ["Finance", "Admin"]);
        var principalB = AegisPrincipal.Create("alice", roles: ["Admin", "Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        Assert.Equal(
            DecisionCache.BuildKey(principalA, resource, "view"),
            DecisionCache.BuildKey(principalB, resource, "view"));
    }

    [Fact]
    public void BuildKey_ValuesContainingDelimiterLikeCharacters_DoNotCollideAcrossDifferentShapes()
    {
        // A naive "key1=val1|key2=val2" scheme would conflate these two
        // logically different attribute sets, since the first's single
        // value contains characters that look like a second key/value pair.
        var crafted = AegisPrincipal.Create("alice",
            attributes: new Dictionary<string, object?> { ["a"] = "1|b=2" });
        var distinct = AegisPrincipal.Create("alice",
            attributes: new Dictionary<string, object?> { ["a"] = "1", ["b"] = "2" });
        var resource = AegisResource.Create("invoices", "INV-1");

        Assert.NotEqual(
            DecisionCache.BuildKey(crafted, resource, "view"),
            DecisionCache.BuildKey(distinct, resource, "view"));
    }

    [Fact]
    public void BuildKey_DifferentAction_ProducesDifferentKey()
    {
        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create("invoices", "INV-1");

        Assert.NotEqual(
            DecisionCache.BuildKey(principal, resource, "view"),
            DecisionCache.BuildKey(principal, resource, "approve"));
    }
}