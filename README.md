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

Phase 1 slice: policy loading, the expression engine, the decision engine,
and the explain API. Deliberately a tree-walking interpreter, not the
compiler/IR pipeline described below — that's a later optimization once the
expression surface is stable. No relationships, no server, no dashboard yet.

```
src/
  Aegis.Core          Principal, Resource, AuthorizationDecision, DecisionExplanation
  Aegis.Expressions    Tokenizer, parser, tree-walking evaluator for condition expressions
  Aegis.Policies       ResourcePolicy/ActionRule/AllowRule model + YAML loader
  Aegis.Evaluator      PolicyEvaluator (decision engine) + AegisEngine facade
tests/
  Aegis.Tests
samples/
  Aegis.Sample         Runnable loan-approval walkthrough (see below)
```

## Getting started

```bash
dotnet test                                 # run the test suite
dotnet run --project samples/Aegis.Sample   # role-based + attribute-based + segregation-of-duties decisions, with explain output
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

actions:
  approve:
    allow:
      when: principal.branch == resource.branch && resource.amount <= principal.approvalLimit && principal.id != resource.applicantId
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
- **Branch/tenant isolation.** Also expressible today as ABAC
  (`principal.branch == resource.branch`), which is fine for single-tenant
  or branch-scoped deployments. True multi-tenant isolation — separate
  policy sets per tenant, enforced at the storage layer rather than by
  convention in every policy — is Phase 3 work. Until then, ABAC-based
  scoping is the pattern, and it's on the policy author to apply it
  consistently rather than the engine enforcing it structurally.
- **Data residency.** Policy Storage (see below) is designed pluggable
  specifically so a regulated deployment can pick a backend that keeps
  policy data in-region (e.g. a regional Blob Storage account or an
  on-prem Git server) — not yet implemented beyond the filesystem loader.

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

| Project | Inspiration |
|----------|-------------|
| Cerbos | Policy-driven authorization, PDP architecture, resource policies, explainability |
| Cedar | Safe policy language, formal authorization model, expressive conditions |
| Zanzibar | Relationship-based authorization (ReBAC), graph authorization, scalability |
| OpenFGA | Relationship tuples and authorization modeling |
| AuthZEN | Standard authorization APIs and interoperability |
| ASP.NET Core | Dependency Injection, middleware, authorization handlers |
| OpenTelemetry | Observability, metrics, tracing |

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

Inspired by Zanzibar and OpenFGA.

Relationships become data rather than application logic.

Example:

```
user:alice

member

project:payments
```

Questions such as:

> Can Alice approve invoice 123?

become graph evaluations rather than hardcoded business logic.

---

# AuthZEN Compatibility

Aegis should expose a standards-based authorization API.

```
Application

↓

AuthZEN Request

↓

Aegis Runtime

↓

Authorization Decision
```

The runtime should remain independent of transport protocols.

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

## Phase 1 — Core Engine

- Policy loading
- Policy validation
- Compiler
- Expression engine
- Decision engine
- Explain API
- ASP.NET integration

---

## Phase 2 — Advanced Authorization

- Derived roles
- Variables
- Imports
- Decision caching
- Attribute providers
- Policy testing
- CLI

---

## Phase 3 — Relationships

- ReBAC
- Relationship graph
- Tuple storage
- Graph evaluation
- Multi-tenancy

---

## Phase 4 — Platform

- AuthZEN server
- Dashboard
- Policy editor
- Audit logs
- Git integration
- Distributed cache
- High availability

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
