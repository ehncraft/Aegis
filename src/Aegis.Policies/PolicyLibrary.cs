namespace Aegis.Policies;

/// <summary>
/// A shared set of variables/derived roles that <see cref="ResourcePolicy.Imports"/>
/// pulls into a policy. Identified by <see cref="Name"/>, not a resource -- a YAML
/// file is classified as a library (rather than a policy) when it has a
/// <c>name:</c> key and no <c>resource:</c> key. Not part of the public API:
/// only <see cref="YamlPolicyLoader"/> and <see cref="PolicyImportResolver"/> deal
/// with libraries directly -- callers only ever see the merged <see cref="ResourcePolicy"/>.
/// </summary>
internal sealed class PolicyLibrary
{
    public string Name { get; set; } = string.Empty;

    public Dictionary<string, string> Variables { get; set; } = new();

    public Dictionary<string, DerivedRoleDefinition> DerivedRoles { get; set; } = new();

    public string? Source { get; set; }
}