using System.Text.Encodings.Web;
using System.Text.Json;

using Aegis;
using Aegis.Audit;
using Aegis.Relationships;

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

var policiesPath = Path.Combine(AppContext.BaseDirectory, "Policies");
var relationshipsPath = Path.Combine(AppContext.BaseDirectory, "Relationships");
var auditLog = new InMemoryAuditLogStore();
var engine = (await AegisEngine.Create(policiesPath)
        .WithRelationshipsAsync(new YamlRelationshipProvider(relationshipsPath)))
    .WithAuditLog(auditLog);

// A loan officer at the Nairobi CBD branch with a 500,000 approval limit.
var officer = AegisPrincipal.Create(
    "officer-1",
    roles: ["LoanOfficer"],
    attributes: new Dictionary<string, object?>
    {
        ["branch"] = "nairobi-cbd",
        ["approvalLimit"] = 500_000,
    });

var withinLimit = AegisResource.Create(
    "loan_applications",
    "LN-1001",
    attributes: new Dictionary<string, object?>
    {
        ["branch"] = "nairobi-cbd",
        ["amount"] = 250_000,
        ["applicantId"] = "member-42",
    });

var overLimit = AegisResource.Create(
    "loan_applications",
    "LN-1002",
    attributes: new Dictionary<string, object?>
    {
        ["branch"] = "nairobi-cbd",
        ["amount"] = 750_000,
        ["applicantId"] = "member-77",
    });

// Same officer applying for their own loan -- segregation of duties should block this.
var ownApplication = AegisResource.Create(
    "loan_applications",
    "LN-1003",
    attributes: new Dictionary<string, object?>
    {
        ["branch"] = "nairobi-cbd",
        ["amount"] = 100_000,
        ["applicantId"] = "officer-1",
    });

Explain("Can the officer view LN-1001? (role-based rule)",
    await engine.AuthorizeAsync(officer, withinLimit, LoanActions.View));

Explain("Can the officer approve LN-1001? (own branch, within limit, not their own application)",
    await engine.AuthorizeAsync(officer, withinLimit, LoanActions.Approve));

Explain("Can the officer approve LN-1002? (exceeds their approval limit)",
    await engine.AuthorizeAsync(officer, overLimit, LoanActions.Approve));

Explain("Can the officer approve LN-1003? (their own application -- segregation of duties)",
    await engine.AuthorizeAsync(officer, ownApplication, LoanActions.Approve));

Explain("Can the officer flag LN-1003 for conflict of interest? (derived role: they are the applicant)",
    await engine.AuthorizeAsync(officer, ownApplication, LoanActions.FlagConflictOfInterest));

Explain("Can the officer flag LN-1001 for conflict of interest? (derived role: they are not the applicant)",
    await engine.AuthorizeAsync(officer, withinLimit, LoanActions.FlagConflictOfInterest));

Explain(
    "Can the officer review LN-1001 for the audit committee? " +
    "(ReBAC derived role: officer-1 is in senior-auditors, which is in audit-committee -- transitive, two-hop)",
    await engine.AuthorizeAsync(officer, withinLimit, LoanActions.Review));

// Multi-tenancy: acme-sacco and beta-bank each have their own
// loan_applications policy -- same resource name, different rules, and
// structurally isolated (Tenants/{tenantId} loads into its own AegisEngine,
// built lazily and cached on first request for that tenant).
var tenantsPath = Path.Combine(AppContext.BaseDirectory, "Tenants");
await using var tenantRegistry = MultiTenantAegisEngine.FromTenantDirectories(tenantsPath);

Explain(
    "Can the officer view LN-1001 under acme-sacco's policy? (acme-sacco requires LoanOfficer, which they hold)",
    await tenantRegistry.AuthorizeAsync("acme-sacco", officer, withinLimit, LoanActions.View));

Explain(
    "Can the officer view LN-1001 under beta-bank's policy? " +
    "(beta-bank requires Underwriter instead -- a wholly separate policy set for the same resource name, not a filtered view of acme-sacco's)",
    await tenantRegistry.AuthorizeAsync("beta-bank", officer, withinLimit, LoanActions.View));

// Audit trail: WithAuditLog(store) recorded every decision above for
// officer-1 -- allow and deny alike -- as it happened, queryable after the
// fact rather than only returned inline from AuthorizeAsync.
var officerHistory = await auditLog.QueryAsync(new AuditLogQuery { PrincipalId = "officer-1" });
Console.WriteLine($"Audit trail: {officerHistory.Count} decisions recorded for officer-1:");
foreach (var record in officerHistory)
{
    Console.WriteLine(
        $"  {record.Timestamp:O}  {record.Action,-25} {record.ResourceKind}/{record.ResourceId,-10} -> {(record.Allowed ? "allow" : "deny")}");
}

void Explain(string question, AuthorizationDecision decision)
{
    Console.WriteLine(question);
    Console.WriteLine(JsonSerializer.Serialize(decision.Explanation, jsonOptions));
    Console.WriteLine();
}

file static class LoanActions
{
    public const string View = "view";
    public const string Approve = "approve";
    public const string FlagConflictOfInterest = "flag_conflict_of_interest";
    public const string Review = "review";
}