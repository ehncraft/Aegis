using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Aegis.AuthZen;
using Aegis.Policies;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Aegis.Tests;

public sealed class AuthZenEndpointsTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private IHost? _host;
    private HttpClient? _client;

    private static ResourcePolicy InvoicePolicy() => new()
    {
        Resource = "invoices",
        Actions = new Dictionary<string, ActionRule>
        {
            ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
            ["approve"] = new()
            {
                Allow = new AllowRule { When = "action.reason == 'audit' && context.mfa_verified == true" },
            },
        },
    };

    public async Task InitializeAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(AegisEngine.FromPolicies([InvoicePolicy()]));
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapAuthZenEndpoints());
            });
        });

        _host = await builder.StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [Fact]
    public async Task Evaluation_RoleMatch_ReturnsAllowAsync()
    {
        var request = new
        {
            subject = new { type = "user", id = "alice", properties = new { roles = new[] { "Finance" } } },
            resource = new { type = "invoices", id = "INV-1" },
            action = new { name = "view" },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluation", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("decision").GetBoolean());
    }

    [Fact]
    public async Task Evaluation_NoRoleMatch_ReturnsDenyWithExplanationContextAsync()
    {
        var request = new
        {
            subject = new { type = "user", id = "bob", properties = new { roles = new[] { "Sales" } } },
            resource = new { type = "invoices", id = "INV-1" },
            action = new { name = "view" },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluation", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("decision").GetBoolean());
        Assert.True(body.TryGetProperty("context", out var context));
        Assert.Equal("deny", context.GetProperty("effect").GetString());
    }

    [Fact]
    public async Task Evaluation_ActionPropertiesAndContext_ResolveInPolicyAsync()
    {
        var request = new
        {
            subject = new { type = "user", id = "alice" },
            resource = new { type = "invoices", id = "INV-1" },
            action = new { name = "approve", properties = new { reason = "audit" } },
            context = new { mfa_verified = true },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluation", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("decision").GetBoolean());
    }

    [Fact]
    public async Task Evaluation_ResourceTypeMapsToResourceKindAsync()
    {
        // A resource type that doesn't match any policy's `resource:` key
        // should deny with "no policy found", proving type -> Kind mapping
        // is what drives policy lookup.
        var request = new
        {
            subject = new { type = "user", id = "alice", properties = new { roles = new[] { "Finance" } } },
            resource = new { type = "documents", id = "DOC-1" },
            action = new { name = "view" },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluation", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("decision").GetBoolean());
        Assert.Contains("documents", body.GetProperty("context").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Evaluation_MissingRequiredField_ReturnsBadRequestAsync()
    {
        using var content = JsonContent.Create(new { resource = new { type = "invoices", id = "INV-1" } }, options: JsonOptions);

        var response = await _client!.PostAsync("/access/v1/evaluation", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Evaluations_ExecuteAll_RunsEveryEntryAsync()
    {
        var request = new
        {
            subject = new { type = "user", id = "alice", properties = new { roles = new[] { "Finance" } } },
            resource = new { type = "invoices", id = "INV-1" },
            evaluations = new object[]
            {
                new { action = new { name = "view" } },
                new { action = new { name = "delete" } }, // no rule -> deny
                new { action = new { name = "view" } },
            },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluations", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var evaluations = body.GetProperty("evaluations");

        Assert.Equal(3, evaluations.GetArrayLength());
        Assert.True(evaluations[0].GetProperty("decision").GetBoolean());
        Assert.False(evaluations[1].GetProperty("decision").GetBoolean());
        Assert.True(evaluations[2].GetProperty("decision").GetBoolean());
    }

    [Fact]
    public async Task Evaluations_DefaultSemanticIsExecuteAllAsync()
    {
        var request = new
        {
            subject = new { type = "user", id = "alice", properties = new { roles = new[] { "Finance" } } },
            resource = new { type = "invoices", id = "INV-1" },
            evaluations = new object[]
            {
                new { action = new { name = "delete" } }, // deny, but no options set -- must not short-circuit
                new { action = new { name = "view" } },
            },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluations", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, body.GetProperty("evaluations").GetArrayLength());
    }

    [Fact]
    public async Task Evaluations_DenyOnFirstDeny_ShortCircuitsWithPartialResultsAsync()
    {
        var request = new
        {
            subject = new { type = "user", id = "alice", properties = new { roles = new[] { "Finance" } } },
            resource = new { type = "invoices", id = "INV-1" },
            evaluations = new object[]
            {
                new { action = new { name = "view" } }, // allow
                new { action = new { name = "delete" } }, // deny -- stop here
                new { action = new { name = "view" } }, // never executed
            },
            options = new { evaluations_semantic = "deny_on_first_deny" },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluations", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var evaluations = body.GetProperty("evaluations");

        Assert.Equal(2, evaluations.GetArrayLength());
        Assert.True(evaluations[0].GetProperty("decision").GetBoolean());
        Assert.False(evaluations[1].GetProperty("decision").GetBoolean());
    }

    [Fact]
    public async Task Evaluations_PermitOnFirstPermit_ShortCircuitsWithPartialResultsAsync()
    {
        var request = new
        {
            subject = new { type = "user", id = "alice", properties = new { roles = new[] { "Finance" } } },
            resource = new { type = "invoices", id = "INV-1" },
            evaluations = new object[]
            {
                new { action = new { name = "delete" } }, // deny
                new { action = new { name = "view" } }, // allow -- stop here
                new { action = new { name = "delete" } }, // never executed
            },
            options = new { evaluations_semantic = "permit_on_first_permit" },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluations", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var evaluations = body.GetProperty("evaluations");

        Assert.Equal(2, evaluations.GetArrayLength());
        Assert.False(evaluations[0].GetProperty("decision").GetBoolean());
        Assert.True(evaluations[1].GetProperty("decision").GetBoolean());
    }

    [Fact]
    public async Task Evaluations_PerEntryOverridesRequestLevelDefaultAsync()
    {
        var request = new
        {
            subject = new { type = "user", id = "alice", properties = new { roles = new[] { "Finance" } } },
            resource = new { type = "invoices", id = "INV-1" },
            evaluations = new object[]
            {
                new { action = new { name = "view" } },
                new { subject = new { type = "user", id = "bob", properties = new { roles = new[] { "Sales" } } }, action = new { name = "view" } },
            },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluations", request, JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var evaluations = body.GetProperty("evaluations");

        Assert.True(evaluations[0].GetProperty("decision").GetBoolean());
        Assert.False(evaluations[1].GetProperty("decision").GetBoolean());
    }

    [Fact]
    public async Task ResponseFieldNames_AreSnakeCaseOnTheWireAsync()
    {
        var request = new
        {
            subject = new { type = "user", id = "alice", properties = new { roles = new[] { "Finance" } } },
            resource = new { type = "invoices", id = "INV-1" },
            action = new { name = "view" },
        };

        var response = await _client!.PostAsJsonAsync("/access/v1/evaluation", request, JsonOptions);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"decision\"", raw);
        Assert.DoesNotContain("\"Decision\"", raw);
    }

    [Fact]
    public async Task SearchEndpoints_AreNotMapped_ReturnNotFoundAsync()
    {
        var response = await _client!.PostAsync("/access/v1/search/subject", JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}