# Instructions for AI coding agents

Applies to any agent working in this repo (GitHub Copilot agent mode, Claude Code, others).
Humans review and approve every change; these rules make that review fast and safe.

## Ground rules

1. **Synthetic data only.** Never add real names, health data, policy numbers or credentials, including in tests,
   fixtures, prompts and examples. Member numbers look like `TL100001`.
2. **No secrets in the repo.** Connection strings and keys live in user-secrets (local) or Key Vault (Azure).
   If you need a new secret, add a Key Vault reference in `infra/` and say so in the PR.
3. **Never weaken the gates.** Do not lower `AnalysisLevel`, remove `TreatWarningsAsErrors`, add blanket
   `#pragma warning disable`, loosen ESLint rules, or skip tests to make a build pass. Fix the cause. If a rule is
   genuinely wrong for a case, suppress it on that line with a comment saying why.
4. **One concern per change.** Small, reviewable diffs. Don't reformat unrelated files.
5. **Personal information never goes to a model.** Any code that calls an LLM must strip names, member numbers,
   dates of birth and free text the member wrote, and must log what was sent (without the PII).
6. **Stop and ask** before: database migrations that drop or alter columns, changes under `infra/`,
   anything touching auth (API keys, managed identity, SQL permissions), or deleting files.

## Build and verify (run before saying a change is done)

```powershell
dotnet format whitespace --verify-no-changes --exclude "**/Migrations/**"
dotnet format style --verify-no-changes --exclude "**/Migrations/**"
dotnet build -c Release            # analyzers on, warnings are errors
dotnet test -c Release             # needs Docker running (integration tests use SQL Server)
cd web; npm run lint; npm run typecheck; npm run format:check; npm run build
```

Do **not** run plain `dotnet format`: it applies analyzer code fixes, and has inserted wrong code here before.
Use the `whitespace` and `style` subcommands.

## Project map

| Path | What |
|---|---|
| `src/ThriveLink.Domain` | Pure rules (`PointsRules`), entities. No dependencies. Unit-test everything here |
| `src/ThriveLink.Data` | EF Core context, migrations, services shared by API and Functions |
| `src/ThriveLink.Api` | Minimal API endpoints + serves the React page from `wwwroot` |
| `src/ThriveLink.Functions` | Wearable webhook, weekly rewards, stale-device sweep (isolated worker) |
| `web/` | React stats page |
| `infra/` | Bicep and deploy scripts (`deploy.ps1`, `edge.ps1`, `gateway.ps1`, `dr.ps1`) |
| `tests/` | xUnit v3; `ThriveLink.Api.Tests` runs the real API against SQL Server via Testcontainers |

## Conventions

- C#: file-scoped namespaces, primary constructors, `TimeProvider` (never `DateTime.Now`) so time is testable,
  `CultureInfo.InvariantCulture` for parsing, `[LoggerMessage]` source-generated logging in Functions.
- SQL: parameterised only (`ExecuteSqlAsync` with interpolation, never `ExecuteSqlRaw` with concatenation).
- Migrations: `dotnet ef migrations add <Name> -p src/ThriveLink.Data -s src/ThriveLink.Api`. Hand-written SQL in
  a migration must be idempotent-script safe (wrap `CREATE SCHEMA/PROCEDURE` in `EXEC(N'...')`).
- Every new endpoint or rule gets a test. Integration tests for anything touching SQL.
