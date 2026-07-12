using System.Text.Encodings.Web;
using System.Text.Json;

using Aegis;

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

var policiesPath = Path.Combine(AppContext.BaseDirectory, "Policies");
var engine = AegisEngine.Create(policiesPath);

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
}