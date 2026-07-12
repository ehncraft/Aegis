namespace Aegis.Sql;

/// <summary>
/// Maps <see cref="SqlServerAttributeProvider"/> onto an existing database's
/// schema. Nothing here is hardcoded to a specific auth server's table
/// shape -- every table/column name is configured, since that schema isn't
/// this library's to dictate.
/// </summary>
public sealed class SqlAttributeProviderOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Table holding one row per principal, e.g. an existing "Users" table.</summary>
    public string PrincipalTable { get; set; } = "Users";

    /// <summary>Column on <see cref="PrincipalTable"/> matched against <c>AegisPrincipal.Id</c>.</summary>
    public string PrincipalIdColumn { get; set; } = "Id";

    /// <summary>Output attribute name -> column name, for columns on <see cref="PrincipalTable"/>.</summary>
    public Dictionary<string, string> PrincipalAttributeColumns { get; set; } = [];

    /// <summary>
    /// Table holding one row per (principal, role) pair. Leave every Role*
    /// property unset to skip role lookup entirely.
    /// </summary>
    public string? RoleTable { get; set; }

    /// <summary>Column on <see cref="RoleTable"/> matched against <c>AegisPrincipal.Id</c>.</summary>
    public string? RoleUserIdColumn { get; set; }

    /// <summary>Column on <see cref="RoleTable"/> holding the role name.</summary>
    public string? RoleNameColumn { get; set; }

    /// <summary>Resource attribute lookup, keyed by <c>AegisResource.Kind</c> (case-insensitive).</summary>
    public Dictionary<string, SqlResourceTableMapping> ResourceTables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Where to find attributes for one <c>AegisResource.Kind</c>.</summary>
public sealed class SqlResourceTableMapping
{
    public required string Table { get; init; }

    /// <summary>Column matched against <c>AegisResource.Id</c>.</summary>
    public required string IdColumn { get; init; }

    /// <summary>Output attribute name -> column name.</summary>
    public Dictionary<string, string> AttributeColumns { get; init; } = [];
}