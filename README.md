# ThriveLink

A wellness-rewards and claims platform modelled on how insurer wellness programmes work: members link a
fitness watch, activity becomes points, points set a status tier, and status sets a premium discount.
Built on .NET and Azure as a portfolio project. **All data is synthetic.**

## Stack

| Layer | Tech |
|---|---|
| API | ASP.NET Core (.NET 10, C#), minimal APIs |
| Data | EF Core, SQL Server locally (Docker) / Azure SQL Database in Azure |
| Rules | `ThriveLink.Domain`: pure C#, no dependencies, fully unit-tested |
| Stats page | React + TypeScript + Vite + Tailwind + Recharts, served from the API's `wwwroot` |
| Tests | xUnit v3; integration tests run the real API against SQL Server via Testcontainers |
| Infra (next) | Bicep: App Service, Azure SQL, Key Vault, Blob, Functions, APIM, App Gateway, Data Factory, Monitor, Site Recovery |

## Run locally

Prerequisites: .NET 10 SDK, Node 24, Docker Desktop.

```powershell
# 1. Local SQL Server (password lives in .env, which is gitignored)
docker compose up -d

# 2. Connection string goes in user-secrets, never in appsettings
dotnet user-secrets set "ConnectionStrings:Sql" "Server=localhost,14333;Database=ThriveLink;User Id=sa;Password=<from .env>;TrustServerCertificate=True" --project src/ThriveLink.Api

# 3. Build the stats page into the API, then run (migrates and seeds synthetic data in Development)
cd web; npm ci; npm run build; cd ..
dotnet run --project src/ThriveLink.Api --urls http://localhost:5080
```

Open http://localhost:5080. For live-reload on the page, run `npm run dev` in `web/` and open http://localhost:5173.

## API

| Method | Path | Purpose |
|---|---|---|
| GET | `/health` | Liveness |
| GET | `/api/stats` | Programme stats for the dashboard |
| GET | `/api/members/{memberNumber}` | Member status, points, weekly progress, devices |
| POST | `/api/activity` | Ingest one day of activity from a device (latest report for a day wins) |

## Points rules (ThriveLink's own numbers)

- Steps: 5,000 → 30, 7,500 → 60, 10,000+ → 100 points a day
- Workout: 30+ min at ≥ 60% of max heart rate → 60, 60+ min → 100
- Each category is capped at 100 a day. Over 60,000 steps is flagged as implausible and earns nothing until reviewed
- Status by points this year: Bronze, Silver 10k, Gold 20k, Platinum 30k → premium discount 5 / 10 / 12.5 / 15%
- Health checks: 1,000 points per measure

## Quality gates

Every push and pull request runs (`.github/workflows`):

| Gate | Tool |
|---|---|
| C# formatting and style | `dotnet format whitespace/style --verify-no-changes` (`.editorconfig`) |
| C# static analysis | .NET analyzers at `latest-recommended`, **warnings are errors** (`Directory.Build.props`) |
| Tests | xUnit unit + integration (real SQL Server in Docker) |
| Dependency scanning (SCA) | NuGet audit incl. transitive, `npm audit`, Dependabot |
| Web lint | ESLint `strictTypeChecked`, zero warnings; Prettier; `tsc --strict` |
| Code scanning (SAST) | CodeQL for C# and TypeScript |
| Secret scanning | gitleaks over full history |
| Review | PR template incl. an AI-assistance checklist; branch protection requires checks + 1 approval |
