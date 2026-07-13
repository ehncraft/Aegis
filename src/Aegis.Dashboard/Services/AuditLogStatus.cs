namespace Aegis.Dashboard.Services;

/// <summary>
/// Whether the registered <c>IAuditLogStore</c> is real persistence
/// (<c>SqlAuditLogStore</c>) or the empty in-memory placeholder used when
/// no connection string is configured -- lets the audit page explain an
/// empty result set instead of looking broken.
/// </summary>
public sealed record AuditLogStatus(bool IsPersistent);