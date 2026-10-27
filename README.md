# ThriveLink

A wellness-rewards and health-claims platform, modelled on how insurer wellness programmes work: members link a
fitness watch, activity becomes points, points set a status tier, status sets a premium discount, and members
submit health claims that are triaged before a human assessor decides. Built on .NET and Azure as a portfolio
project. **All data is synthetic.**

## Architecture

```
 Wearable aggregators / partners                     Members & assessors
            │  subscription key                                │
            ▼                                                  ▼
   API Management (Consumption) ─────────────┐     Application Gateway WAF v2 (on demand)
     rate limits, keys from Key Vault        │                 │
            │                                ▼                 ▼
            ├──► Azure Functions (Flex) ──► App Service: ASP.NET Core API + React stats page
            │     wearable webhook,               │
            │     weekly rewards, stale sweep     │
            │            │                        ▼
            │            └──────────────► Azure SQL (serverless, Entra-only auth) ◄── Data Factory
            │                                     ▲                                     (partner CSVs)
            ▼                                     │                                         ▲
     Blob Storage: raw payloads, partner files, claim documents ──────────────────────────────┘
                                                  │
     Container Apps Job (claims triage, every 5 min) ── image from Container Registry
                                                  │
     Key Vault · Log Analytics + App Insights · Monitor alerts · Site Recovery (legacy VM → Australia Southeast)
```

Every service authenticates with **managed identity**: no SQL passwords exist, no storage keys or connection
strings with secrets are in app settings. The only secrets (ingest API key, Functions key, optional Anthropic key)
live in Key Vault and are read by identity.

## Azure services and where they're used

| Service | Used for | Code |
|---|---|---|
| App Service (Linux, .NET 10) | API + stats page | `src/ThriveLink.Api`, `infra/modules/appservice.bicep` |
| Azure SQL Database | All data; serverless free offer, Entra-only auth, least-privilege users per identity | `src/ThriveLink.Data`, `infra/modules/sql.bicep` |
| Azure Functions (Flex Consumption) | Wearable webhook, weekly rewards, stale-device sweep | `src/ThriveLink.Functions`, `infra/modules/functions.bicep` |
| Blob Storage | Raw wearable payloads (audit/replay), partner files, claim documents | `infra/modules/storage.bicep` |
| Data Factory | Nightly partner health-check import: CSV → staging → stored procedure → archive | `infra/modules/datafactory.bicep`, `usp_ApplyHealthChecks` |
| API Management | Partner API: subscription keys, rate limiting, backend keys injected from Key Vault | `infra/edge.bicep` |
| Application Gateway (WAF v2) | OWASP rule set in front of the web app; deployed on demand (~US$10/day) | `infra/gateway.bicep` |
| Key Vault | Secrets, RBAC-only, read via managed identity and Key Vault references | `infra/modules/keyvault.bicep` |
| Monitor / Log Analytics / App Insights | Telemetry, availability test, metric + log alerts, KQL | `infra/alerts.bicep`, `docs/kql/` |
| Site Recovery | Legacy policy-admin VM replicated Australia East → Southeast, DR drills | `infra/dr.bicep`, `infra/dr.ps1` |
| Container Apps Jobs + Container Registry | Scheduled claims triage container | `src/ThriveLink.ClaimsTriage`, `infra/claims.bicep` |

## Features

- **Points engine:** daily caps, plausibility checks (a 140,000-step day is flagged, not paid), personal weekly
  targets, Bronze → Platinum status, premium discount.
- **Wearable ingest:** provider-neutral webhook; latest report for a day wins, so re-syncs never double-count.
- **Partner health checks:** pharmacies/GPs drop a CSV; Data Factory loads it; bad rows go to a rejects table
  with a reason; re-sending a file is harmless.
- **Claims:** pre-approval and reimbursement, documents to private Blob (allow-listed types, server-generated
  names), 5-working-day assessment SLA, decisions by humans only (a decline must include a reason).
- **Claims triage:** deterministic rule checks first (missing receipt, high value, duplicate, late pre-approval,
  old treatment). Optional AI review (Claude) on a **redacted** copy: no names, member/policy numbers, NHI,
  phone numbers or emails. **AI can make a recommendation more cautious than the rules, never less**, and a
  failed AI call leaves the rules result in place.

## Run locally

Prerequisites: .NET 10 SDK, Node 24, Docker Desktop.

```powershell
docker compose up -d                                       # SQL Server; password in .env (gitignored)
dotnet user-secrets set "ConnectionStrings:Sql" "Server=localhost,14333;Database=ThriveLink;User Id=sa;Password=<from .env>;TrustServerCertificate=True" --project src/ThriveLink.Api
cd web; npm ci; npm run build; cd ..
dotnet run --project src/ThriveLink.Api --urls http://localhost:5080   # migrates + seeds synthetic data
```

## Deploy to Azure

```powershell
./infra/deploy.ps1 -Env dev -Seed -TestPipeline   # platform, migrations, SQL users, apps, Data Factory test run
./infra/edge.ps1                                  # API Management + end-to-end partner API test
./infra/claims.ps1                                # build triage image in ACR, deploy + run the Container Apps Job
./infra/gateway.ps1 ; ./infra/gateway.ps1 -Remove # WAF demo (create, test, delete)
./infra/dr.ps1 -Setup ; ./infra/dr.ps1 -Status ; ./infra/dr.ps1 -Drill   # Site Recovery
```

To enable AI claims triage: `az keyvault secret set --vault-name <kv> -n Anthropic--ApiKey --value <key>`,
then re-run `./infra/claims.ps1`.

## Quality gates

Every push and pull request runs `.github/workflows`:

| Gate | Tool |
|---|---|
| C# formatting and style | `dotnet format whitespace/style --verify-no-changes` (`.editorconfig`) |
| C# static analysis | .NET analyzers at `latest-recommended`, **warnings are errors** (`Directory.Build.props`) |
| Tests | xUnit v3: unit + integration against real SQL Server (Testcontainers); 58 tests |
| Dependency scanning (SCA) | NuGet audit incl. transitive, `npm audit`, Dependabot |
| Web lint | ESLint `strictTypeChecked`, zero warnings; Prettier; `tsc --strict` |
| Code scanning (SAST) | CodeQL for C# and TypeScript |
| Secret scanning | gitleaks over full history |
| Review | PR template with an AI-assistance checklist; branch protection: checks + 1 approval |

## Working with AI agents

- [`AGENTS.md`](AGENTS.md): ground rules for any coding agent (synthetic data only, never weaken gates, stop and
  ask before infra/auth/migration changes), build commands, conventions.
- [`.github/copilot-instructions.md`](.github/copilot-instructions.md): GitHub Copilot instructions.
- [`.github/prompts/`](.github/prompts): reusable prompt files (add an endpoint, investigate an incident).
- [`.github/agents/pr-reviewer.agent.md`](.github/agents/pr-reviewer.agent.md): an independent, read-only PR
  reviewer agent with a security/privacy/correctness checklist.
