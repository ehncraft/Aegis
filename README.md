# Aegis

> **A modern, cloud-native authorization platform for .NET**
>
> Build expressive, policy-driven authorization by combining the best ideas from Cerbos, Cedar, Zanzibar, OpenFGA, and AuthZEN—while embracing the .NET ecosystem from the ground up.

Aegis is domain-agnostic, but the primary target vertical is regulated
finance — banking, SACCOs, insurance — where authorization decisions need
to be explainable to an auditor, not just correct. See
[Compliance notes for regulated finance](#compliance-notes-for-regulated-finance)
below for how that shapes the design.

---

## Status

Phase 0 (MSSQL + existing ASP.NET Identity/IdentityServer/OpenIddict auth
server integration) is done. Phase 1's core engine slice — policy loading,
validation, the expression engine, the decision engine, the explain API, DI/
ASP.NET Core registration, and a CLI — is also done, except the compiler/IR
pipeline described below, which is deliberately deferred: still a
tree-walking interpreter, and that's a later optimization once the
expression surface is stable. Policies can also share reusable `${name}`
variables and derived roles (roles computed from a condition, or from
Cedar-style entity-hierarchy membership, rather than held directly by the
principal) via `imports:` -- see [Policy as Code](#policy-as-code) below.
No standalone server, no dashboard yet.

```
src/
  Aegis.Core          Principal, Resource, AuthorizationDecision, DecisionExplanation,
                      IAttributeProvider, IClaimsPrincipalMapper
  Aegis.Expressions    Tokenizer, parser, tree-walking evaluator for condition expressions
                      (member paths, literals, `${name}` variable references)
  Aegis.Policies       ResourcePolicy/ActionRule/AllowRule/DerivedRoleDefinition model,
                      IPolicyProvider, YAML loader (variables/derivedRoles/imports)
  Aegis.Relationships  Cedar-style entity hierarchy (EntityUid/EntityParent), pluggable
                      IRelationshipProvider, RelationshipGraph (`in` membership, #15-#18)
  Aegis.Evaluator      PolicyEvaluator (decision engine), PolicyValidator, opt-in decision
                      caching (AegisEngine.WithDecisionCache), AegisEngine facade,
                      MultiTenantAegisEngine (one isolated engine per tenant, #19)
  Aegis.Sql            SQL Server-backed IAttributeProvider + IPolicyProvider, both with
                      optional per-tenant scoping
  Aegis.AspNetCore     services.AddAegis(...) DI registration, HttpContext.User authorization
  Aegis.AuthZen        MapAuthZenEndpoints() -- Authorization API 1.0 evaluation endpoints, #20
  Aegis.Cli            `aegis validate`/`aegis authorize` -- a dotnet tool (AegisCli)
  Aegis.Testing        ShouldAllowAsync/ShouldDenyAsync -- CI-friendly policy regression tests,
                      no application host required
tests/
  Aegis.Tests          Fast, no external dependencies -- the required CI check
  Aegis.IntegrationTests   Real SQL Server via Testcontainers -- separate, non-required CI job
samples/
  Aegis.Sample         Runnable loan-approval walkthrough (see below)
```

## Getting started

```bash
dotnet test                                 # run the test suite
dotnet run --project samples/Aegis.Sample   # role-based + attribute-based + relationship-based decisions, with explain output
```

The sample models a SACCO/bank loan approval: a loan officer can approve an
application only if it's in their own branch, within their lending limit,
and not their own application.

```csharp
var engine = AegisEngine.Create("Policies");

var decision = await engine.AuthorizeAsync(
    principal: loanOfficer,
    resource: loanApplication,
    action: "approve");

if (decision.Allowed)
{
    // Disburse the loan
}
```

```yaml
resource: loan_applications

imports:
  - lending-common   # shared variables/derived roles -- see lending-common.yaml

actions:
  approve:
    allow:
      when: ${sameBranch} && ${withinLimit} && !${isApplicant}

  flag_conflict_of_interest:
    allow:
      roles:
        - applicant   # a derived role from lending-common.yaml, not a static one
```

Or via DI (`Aegis.AspNetCore`), authorizing straight off `HttpContext.User` --
any `IAttributeProvider`/`IPolicyProvider` registered elsewhere (e.g.
`Aegis.Sql`'s `AddSqlServerAttributeProvider`) is picked up automatically,
in any registration order:

```csharp
services.AddAegis(options => options.AddPolicies("Policies"));

// in an endpoint/controller:
var decision = await engine.AuthorizeAsync(HttpContext, loanApplication, "approve");
```

Or from the command line (`dotnet tool install -g AegisCli`, exposing an
`aegis` command) -- useful in CI to catch a broken policy before it ships,
or to sanity-check a decision without writing code:

```bash
aegis validate Policies

aegis authorize Policies \
  --principal-id officer-1 --role LoanOfficer \
  --principal-attr branch=nairobi-cbd --principal-attr approvalLimit=500000 \
  --resource-kind loan_applications --resource-id LN-1001 \
  --resource-attr branch=nairobi-cbd --resource-attr amount=250000 --resource-attr applicantId=member-42 \
  --action approve   # exit code 0 = allowed, 1 = denied, 2 = error; prints the explain JSON either way
```

Or as CI-friendly policy regression tests (`Aegis.Testing`) -- no application host,
no DI container, works with any test framework:

```csharp
using Aegis.Testing;

var engine = AegisEngine.Create("Policies");

await engine.ShouldAllowAsync(officer, withinLimitApplication, "approve");
await engine.ShouldDenyAsync(officer, ownApplication, "approve"); // segregation of duties
```

A mismatch throws `PolicyAssertionException` with the full decision
explanation attached, so a failing test tells you which condition didn't
evaluate the way the policy intended -- not just "expected allow, got deny".

For multi-tenant deployments, `MultiTenantAegisEngine` keeps every tenant's
policies, relationships, and cache in their own `AegisEngine`, built lazily
the first time that tenant is requested:

```csharp
var registry = MultiTenantAegisEngine.FromTenantDirectories("Tenants"); // Tenants/{tenantId}/*.yaml

var decision = await registry.AuthorizeAsync("acme-sacco", officer, loanApplication, "approve");
```

Or as a standards-based HTTP API (`Aegis.AuthZen`) -- see
[AuthZEN Compatibility](#authzen-compatibility) below:

```csharp
services.AddAegis(options => options.AddPolicies("Policies"));

var app = builder.Build();
app.MapAuthZenEndpoints(); // POST /access/v1/evaluation, POST /access/v1/evaluations
app.Run();
```

## Compliance notes for regulated finance

Three patterns regulated-finance deployments care about, and where each one
stands today versus the roadmap below:

- **Audit trail.** Every decision already carries a `DecisionExplanation` —
  which policy and rule matched, and the result of every condition
  evaluated. That's an audit record for free on every call today; what's
  missing is a persistence layer for it, which is Phase 4's "Audit logs."
- **Segregation of duties.** No special engine feature — it's just an ABAC
  condition, as in the loan sample's `principal.id != resource.applicantId`.
  Any "can't act on your own thing" rule follows the same shape.
- **Branch/tenant isolation.** Branch-scoping within one tenant is still
  ABAC (`principal.branch == resource.branch`) -- that's fine, branches of
  the same SACCO share one policy set on purpose. Across tenants,
  `MultiTenantAegisEngine` gives each tenant a wholly separate `AegisEngine`
  (own policies, own relationship graph, own decision cache), built lazily
  per tenant and cached -- structural isolation, mirroring how AWS Verified
  Permissions scopes access by Policy Store rather than filtering one
  shared store. `Aegis.Sql`'s `SqlPolicyProvider`/`SqlServerAttributeProvider`
  support the same pattern (an optional `TenantId` + tenant column per
  table), for a tenant-scoped provider instance per tenant rather than one
  shared provider trusted to filter correctly on every query.
- **Data residency.** Policy Storage (see below) is designed pluggable
  specifically so a regulated deployment can pick a backend that keeps
  policy data in-region. The filesystem loader and a SQL Server backend
  (`Aegis.Sql`) both exist today; blob storage, Git, and the rest of the
  README's Policy Storage list remain future work.

---

# Vision

Aegis aims to become the **de facto authorization platform for .NET**, much like:

- ASP.NET Core for web applications
- OpenIddict for OAuth/OpenID Connect
- OpenTelemetry for observability

Rather than being a rewrite of an existing project, Aegis is designed as a **first-class .NET authorization ecosystem** that combines proven ideas from existing systems while remaining implementation-independent.

---

# Philosophy

Aegis is **inspired by** existing authorization systems but **not constrained** by them.

| Project | Inspiration | Reference |
|----------|-------------|-----------|
| Cerbos | Policy-driven authorization, PDP architecture, resource policies, explainability | [docs.cerbos.dev](https://docs.cerbos.dev/) |
| Cedar | Safe policy language, formal authorization model, expressive conditions | [Cedar language reference](https://docs.cedarpolicy.com/) · [Cedar paper (Amazon Science)](https://www.amazon.science/publications/cedar-a-new-language-for-expressive-fast-safe-and-analyzable-authorization) |
| Zanzibar | Relationship-based authorization (ReBAC), graph authorization, scalability | [Zanzibar: Google's Consistent, Global Authorization System (USENIX ATC '19)](https://www.usenix.org/conference/atc19/presentation/pang) |
| OpenFGA | Relationship tuples and authorization modeling | [openfga.dev docs](https://openfga.dev/docs/fga) |
| AuthZEN | Standard authorization APIs and interoperability | [Authorization API 1.0 (OpenID AuthZEN WG)](https://openid.github.io/authzen/) |
| ASP.NET Core | Dependency Injection, middleware, authorization handlers | [ASP.NET Core docs](https://learn.microsoft.com/aspnet/core/) |
| OpenTelemetry | Observability, metrics, tracing | [opentelemetry.io](https://opentelemetry.io/) |

The objective is to combine the strongest concepts from each into a cohesive, modern authorization platform.

---

# Design Principles

Aegis should be:

- .NET-first
- Cloud-native
- Embedded or standalone
- Policy-driven
- Explainable
- Extensible
- High-performance
- Standards-friendly
- Observable
- Developer-friendly

---

# What Aegis Is

Aegis is an **authorization engine**, not an authentication system.

```
Authentication

↓

Identity + Access Token

↓

Aegis Authorization Engine

↓

Authorization Decision
```

Authentication remains the responsibility of OAuth, OpenID Connect, Microsoft Entra ID, OpenIddict, Keycloak, etc.

Aegis answers one question:

> **"Can this principal perform this action on this resource?"**

---

# Core Capabilities

## Policy as Code

Policies should live alongside application code.

```yaml
resource: invoices

actions:

  view:
    allow:
      roles:
        - Finance

  approve:
    allow:
      when:
        principal.department == resource.department
```

Policies become versioned, testable, and deployable through normal CI/CD pipelines.

Repeated conditions can be named with `${name}` variables, and roles that
depend on a condition rather than being held directly by the principal can
be expressed as derived roles -- both shareable across policies via
`imports:`:

```yaml
# lending-common.yaml -- a library: identified by `name:`, no `resource:` key
name: lending-common

variables:
  sameBranch: principal.branch == resource.branch
  isApplicant: principal.id == resource.applicantId

derivedRoles:
  applicant:
    when: principal.id == resource.applicantId
```

```yaml
resource: loan_applications

imports:
  - lending-common

actions:
  approve:
    allow:
      when: ${sameBranch} && !${isApplicant}

  flag_conflict_of_interest:
    allow:
      roles:
        - applicant
```

---

## Hybrid Authorization Model

Rather than forcing developers into a single authorization model, Aegis should support multiple models together.

### RBAC

```
User

↓

Role

↓

Permission
```

---

### ABAC

```
Principal Attributes

+

Resource Attributes

+

Environment

↓

Decision
```

Example:

```text
principal.department == resource.department
```

---

### ReBAC

```
Alice

Member

Project

Owns

Repository
```

Relationships become first-class citizens.

---

Policies should be able to combine all three approaches naturally.

---

# Architecture

```
                      Applications

                            │

          ┌─────────────────┼──────────────────┐

          │                 │                  │

     ASP.NET Core      Minimal APIs      Background Workers

          │                 │                  │

          └─────────────────┼──────────────────┘

                            │

                     Aegis SDK

                            │

        ┌───────────────────┼───────────────────┐

        │                   │                   │

    Policy Compiler    Decision Engine    Storage Providers

        │

        ▼

  Intermediate Representation (IR)

        │

        ▼

  Policy Runtime

        │

        ▼

 Relationship Engine

        │

        ▼

 Attribute Providers

        │

        ▼

 Authorization Decision
```

The authorization engine is the core.

Everything else becomes an adapter.

---

# Compilation Pipeline

Policies should be compiled rather than interpreted on every request.

```
YAML / Cedar

↓

Parser

↓

AST

↓

Semantic Validation

↓

Intermediate Representation (IR)

↓

Optimization

↓

Compiled Policy

↓

Evaluation
```

Benefits:

- Faster execution
- Better diagnostics
- Static validation
- Future support for multiple policy languages

---

# Expression Engine

Expressions compile once.

```
Expression

↓

Compile

↓

Delegate

↓

Evaluate
```

Example:

```text
principal.department == resource.department
```

avoids repeated parsing during evaluation.

---

# Explainable Decisions

Every authorization decision should explain **why** it was reached.

Example:

```json
{
  "effect": "allow",
  "matchedPolicy": "invoice-policy",
  "matchedRule": "department-match",
  "conditions": [
    {
      "expression": "principal.department == resource.department",
      "result": true
    }
  ]
}
```

Explainability improves:

- Debugging
- Auditing
- Compliance
- Developer experience

---

# Policy Storage

Support multiple policy sources.

```
Filesystem

Git

Database

Azure Blob Storage

Amazon S3

Redis

HTTP
```

Storage should be pluggable through provider interfaces.

---

# Attribute Providers

Authorization often requires additional data.

Example:

```
Principal

↓

SQL

↓

Manager

↓

Department

↓

Decision
```

Developers can register providers:

```csharp
services.AddAttributeProvider<UserAttributeProvider>();
```

---

# Relationship Engine

Relationships become data rather than application logic. Originally scoped
against [Zanzibar](https://www.usenix.org/conference/atc19/presentation/pang)/[OpenFGA](https://openfga.dev/docs/fga)-style
named relation tuples, but built against
[Cedar](https://docs.cedarpolicy.com/)'s entity-hierarchy model instead —
Aegis is leaning toward Cedar's conventions since adopting Cedar itself is
the direction of travel, so reusing its shape here means less to reconcile
later. An entity has a set of parents; a derived role's `in` check is
Cedar's own `in` operator
(https://docs.cedarpolicy.com/policies/syntax-datatypes.html#operator-in):
true if the principal equals the target entity, or reaches it by walking up
the parent hierarchy — covering direct membership and nested groups
(a member of a sub-team is transitively a member of the parent team) alike.

```yaml
# Relationships/audit_committee.yaml -- Cedar's own entity data format
# (https://docs.cedarpolicy.com/auth/entities-syntax.html), in YAML.
entities:
  - uid: { type: User, id: officer-1 }
    parents:
      - type: Group
        id: senior-auditors

  - uid: { type: Group, id: senior-auditors }
    parents:
      - type: Group
        id: audit-committee
```

```yaml
derivedRoles:
  committeeMember:
    in:
      type: Group
      id: "'audit-committee'"

actions:
  review:
    allow:
      roles:
        - committeeMember
```

```csharp
var engine = await AegisEngine.Create("Policies")
    .WithRelationshipsAsync(new YamlRelationshipProvider("Relationships"));
```

`officer-1` is allowed to `review` even though they only hold a *direct*
membership in `senior-auditors`, not `audit-committee` itself — the graph
walk (breadth-first, cycle-safe) finds the two-hop path. Storage is
pluggable via `IRelationshipProvider` (`YamlRelationshipProvider` for
files, `InMemoryRelationshipProvider` for tests), separate from policy
storage.

---

# AuthZEN Compatibility

`Aegis.AuthZen` exposes the [Authorization API 1.0](https://openid.github.io/authzen/)
specification's evaluation endpoints from the OpenID Foundation's AuthZEN
Working Group, mapped directly onto an `AegisEngine` resolved from DI
(register one first, e.g. via `Aegis.AspNetCore`'s `services.AddAegis(...)`):

```csharp
app.MapAuthZenEndpoints(); // defaults to the spec's /access/v1 prefix
```

- **`POST /access/v1/evaluation`** -- single decision.
- **`POST /access/v1/evaluations`** -- batch, with `options.evaluations_semantic`
  (`execute_all` default, `deny_on_first_deny`, `permit_on_first_permit`) --
  the latter two short-circuit and return a partial results array, per the
  spec's own example.

The spec's search endpoints (subject/resource/action search -- "which
resources can this subject act on") aren't mapped: they need entity
enumeration Aegis has no capability for yet, so those routes just 404.

```json
{
  "subject": { "type": "user", "id": "alice", "properties": { "roles": ["Finance"] } },
  "resource": { "type": "invoices", "id": "INV-1" },
  "action": { "name": "view" }
}
```

`subject.type`/`resource.type` map onto `AegisResource.Kind`-equivalent
concepts, and `subject.properties.roles` (a JSON array of strings) becomes
`AegisPrincipal.Roles` -- AuthZEN itself has no first-class RBAC notion, so
this is Aegis's own convention, documented here rather than left implicit.
Every other property becomes an attribute. The response's `context` field
(optional per spec) carries Aegis's own `DecisionExplanation`, so an AuthZEN
caller gets the matched policy/rule and every condition's result for free,
not just a bare boolean.

`action.properties` and the request's top-level `context` flow all the way
into policy evaluation too -- `action.properties` merges into the existing
`action` expression scope (alongside `action.name`), and `context` becomes
a new top-level `context` scope, both via a new `AegisEngine.AuthorizeAsync`
overload usable from any caller, not just AuthZEN:

```yaml
when: action.reason == 'audit' && context.mfa_verified == true
```

One wire-format subtlety: `subject`/`resource`/`action`/`decision` (Aegis's
own DTO field names) are serialized snake_case per the spec, but
`properties`/`context` are passthrough key-value bags -- whatever key a
caller sends is the key a policy sees, unaffected by any naming convention.

The runtime remains independent of transport protocols -- `Aegis.AuthZen`
is one HTTP-shaped adapter over the same `AegisEngine` every other
integration uses, not a special code path.

---

# Observability

Every decision should produce telemetry.

Metrics:

- Policy compilation time
- Evaluation latency
- Cache hit ratio
- Cache misses
- Authorization throughput

Tracing:

```
HTTP Request

↓

Authentication

↓

Authorization

↓

Database

↓

Response
```

Logging:

- Policy loaded
- Policy updated
- Rule matched
- Decision produced

---

# .NET-Native Experience

Aegis should embrace modern .NET features.

- Dependency Injection
- Generic Host
- Minimal APIs
- Native AOT
- `ILogger`
- `IOptions`
- `System.Text.Json`
- Source Generators
- OpenTelemetry

Developers should feel like they're using another Microsoft-style library.

---

# Package Layout

```
Aegis

├── Aegis.Core
├── Aegis.Compiler
├── Aegis.IR
├── Aegis.Expressions
├── Aegis.Evaluator
├── Aegis.Relationships
├── Aegis.Storage
├── Aegis.Models
├── Aegis.AuthZEN
├── Aegis.AspNetCore
├── Aegis.Server
├── Aegis.Cli
├── Aegis.Dashboard
└── Aegis.Testing
```

---

# Development Roadmap

Tracked as issues, not duplicated here — this list drifts from reality the
moment something ships or gets reprioritized, and it already has once.

- **[Milestones](https://github.com/ehncraft/Aegis/milestones)** — Phase 0
  (MSSQL & existing auth server integration, first priority) through
  Phase 4 (Platform), each with its issues.
- **[Project board](https://github.com/users/ehncraft/projects/1)** — same
  issues, Todo/In Progress/Done view.

---

# Long-Term Vision

```
                          Aegis Platform

                     +-----------------------+
                     |     Dashboard         |
                     +-----------+-----------+
                                 |
                                 v
                      Aegis Authorization Server
                                 |
             +-------------------+-------------------+
             |                                       |
             v                                       v
      AuthZEN API                           REST / gRPC API
             |                                       |
             +-------------------+-------------------+
                                 |
                                 v
                        Aegis Runtime
                                 |
      +-----------+--------------+---------------+-------------+
      |           |              |               |             |
      v           v              v               v             v
  Compiler       IR        Evaluator     Relationships   Policy Store
                                 |
                                 v
                    YAML / Cedar / JSON / Git / Database
```

---

# Guiding Principle

Aegis should not compete by being **"Cerbos in C#"**.

Instead, it should become a **modern authorization platform for .NET** that:

- Learns from Cerbos' policy architecture.
- Adopts Cedar's expressive and safe policy model.
- Incorporates Zanzibar/OpenFGA relationship-based authorization.
- Exposes AuthZEN-compatible APIs for interoperability.
- Feels completely natural to .NET developers.
- Supports embedded libraries and standalone PDP deployments.
- Prioritizes performance, observability, and explainability.

The ultimate goal is to provide a robust, extensible authorization platform that empowers .NET developers to build secure systems with confidence while remaining adaptable to evolving standards and authorization models.
