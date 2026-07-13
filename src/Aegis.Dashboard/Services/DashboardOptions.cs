namespace Aegis.Dashboard.Services;

/// <summary>Binds the <c>Aegis:</c> section of <c>appsettings.json</c>. Every setting is optional -- an unconfigured section just shows an empty state, not a startup failure.</summary>
public sealed class DashboardOptions
{
    public string? PoliciesDirectory { get; set; }

    public string? RelationshipsDirectory { get; set; }

    public AuditLogOptions? AuditLog { get; set; }
}

public sealed class AuditLogOptions
{
    public string? ConnectionString { get; set; }
}

internal static class DashboardPaths
{
    /// <summary>
    /// A configured directory setting is resolved against the app's own
    /// output directory, not the launching process's current working
    /// directory -- a bare relative path like "Policies" would otherwise
    /// only work when launched via <c>cd src/Aegis.Dashboard &amp;&amp; dotnet run</c>,
    /// and silently fail (directory not found) if launched any other way
    /// (`dotnet run --project`, a published binary, a container with a
    /// different working directory).
    /// </summary>
    public static string Resolve(string directory) =>
        Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory);
}