namespace Aegis.Sql;

/// <summary>
/// Maps <see cref="SqlAuditLogStore"/> onto a table holding one row per
/// recorded decision. Aegis owns this table's schema (see
/// <c>Scripts/CreateAuditLogTable.sql</c>), so unlike the attribute
/// provider's tables, only the table name and tenant column are
/// configurable -- every other column name is fixed.
/// </summary>
public sealed class SqlAuditLogStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Table { get; set; } = "AegisAuditLog";

    /// <summary>Column scoping rows to a tenant. Only read when <see cref="TenantId"/> is set.</summary>
    public string TenantIdColumn { get; set; } = "TenantId";

    /// <summary>
    /// Multi-tenancy: when set, only rows matching this tenant are
    /// queried, and every recorded entry is written with this value.
    /// Unset (default) reads/writes every row regardless of tenant.
    /// </summary>
    public string? TenantId { get; set; }
}