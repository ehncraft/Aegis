namespace Aegis.Sql;

/// <summary>
/// Maps <see cref="CedarSqlPolicyProvider"/> onto a table holding one row per Cedar policy
/// statement. Unlike <see cref="SqlPolicyStoreOptions"/>'s YAML rows (one row = one
/// <see cref="Policies.ResourcePolicy"/>), a Cedar row's <see cref="PolicyCedarColumn"/> is a
/// standalone <c>permit</c>/<c>forbid</c> statement -- every row for the requested tenant is
/// parsed and lowered together in one batch (see <see cref="CedarSqlPolicyProvider"/>'s own doc
/// comment for why), the same way every <c>*.cedar</c> file in a directory is.
/// </summary>
public sealed class CedarSqlPolicyStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Schema qualifier for <see cref="Table"/> (e.g. <c>"tenant_123"</c>, producing
    /// <c>[tenant_123].[AegisCedarPolicies]</c>) -- for deployments that isolate tenants via a
    /// separate schema per tenant rather than a shared table filtered by <see cref="TenantId"/>.
    /// The two are independent knobs; setting both is legal but redundant (isolation is
    /// already structural once a non-default <see cref="Schema"/> is set).
    ///
    /// Deliberately <c>required</c>, with no default value at all -- SQL Server resolves an
    /// unqualified table name against the connecting login's own default schema, which isn't
    /// necessarily <c>dbo</c> and can silently differ per login/connection. A caller must
    /// always say which schema it means (e.g. the literal <c>"dbo"</c> string for the
    /// conventional default) -- never left to fall back to ambient, connection-dependent
    /// resolution.
    /// </summary>
    public required string Schema { get; set; }

    public string Table { get; set; } = "AegisCedarPolicies";

    /// <summary>Column identifying each policy row -- used only for
    /// <see cref="Policies.PolicyLoadException.PolicySource"/> if that row's own text fails to
    /// parse, not as a lowering input.</summary>
    public string PolicyNameColumn { get; set; } = "PolicyName";

    /// <summary>Column holding one policy's Cedar text (the same shape as a <c>*.cedar</c> file).</summary>
    public string PolicyCedarColumn { get; set; } = "PolicyCedar";

    /// <summary>Column scoping rows to a tenant. Only read when <see cref="TenantId"/> is set.</summary>
    public string TenantIdColumn { get; set; } = "TenantId";

    /// <summary>
    /// Multi-tenancy: when set, only rows matching this tenant are loaded. Isolation between
    /// tenants is structural via a separate <see cref="CedarSqlPolicyProvider"/> instance per
    /// tenant (see <c>MultiTenantAegisEngine</c> in Aegis.Evaluator), not a shared provider
    /// filtering per request -- same convention as <see cref="SqlPolicyStoreOptions.TenantId"/>.
    /// Unset (default) loads every row regardless of tenant.
    /// </summary>
    public string? TenantId { get; set; }
}