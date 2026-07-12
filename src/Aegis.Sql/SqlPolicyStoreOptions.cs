namespace Aegis.Sql;

/// <summary>
/// Maps <see cref="SqlPolicyProvider"/> onto a table holding one row per
/// policy. The policy body is stored as YAML text and parsed with the same
/// deserializer <c>YamlPolicyLoader</c> uses for files, so a row's contents
/// are exactly what would otherwise be a <c>*.yaml</c> file.
/// </summary>
public sealed class SqlPolicyStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Table { get; set; } = "AegisPolicies";

    /// <summary>Column identifying each policy row -- not necessarily the policy's own "resource" field.</summary>
    public string ResourceNameColumn { get; set; } = "ResourceName";

    /// <summary>Column holding the policy body as YAML text (the same shape as a <c>*.yaml</c> file).</summary>
    public string PolicyYamlColumn { get; set; } = "PolicyYaml";
}