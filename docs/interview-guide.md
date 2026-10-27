# ThriveLink: interview guide

## 10-minute demo script

1. **The page** (`https://app-thrivelink-dev-<suffix>.azurewebsites.net`): programme overview: members, devices,
   weekly target hit rate, sync health, flagged readings. Then the **Claims queue** tab.
2. **Why it's built this way** (30 seconds): .NET 10 API on App Service, Azure SQL, everything signs in with
   managed identity; no passwords exist on the SQL server.
3. **A partner sends data** (APIM):
   `POST https://apim-…azure-api.net/partner/wearables/sync` with a subscription key. Show: no key → 401;
   with key → APIM injects the Functions key from Key Vault → Function archives the raw payload to Blob →
   points awarded. The partner never sees a backend credential.
4. **A partner file arrives** (Data Factory): drop a CSV in `partner-files/incoming`, run the pipeline,
   show the results, the rejected row ("Unknown member") and the archived file.
5. **Claims triage** (Container Apps Job): show the queue, each claim with rule flags and recommendation.
   Explain: rules first, AI optional on **redacted** data, AI can only make it *more* cautious, a human decides.
   Decline needs a reason.
6. **Operations**: App Insights availability test, alerts, KQL in `docs/kql/operations.kql`, Site Recovery drill
   result for the legacy VM.
7. **The repo**: `AGENTS.md`, PR template with the AI checklist, CI gates, CodeQL, Trivy, Dependabot.

## Job ad → where it's shown

| They asked for | In ThriveLink |
|---|---|
| .NET, C# | ASP.NET Core minimal APIs, EF Core, isolated-worker Functions, console job, xUnit v3 |
| Microsoft SQL Server | Azure SQL: migrations, stored procedure (`usp_ApplyHealthChecks`), MERGE, staging schema, least-privilege users |
| App Service | API + React page, Key Vault references, health check, Linux .NET 10 |
| Application Gateway | WAF v2 with Microsoft Default Rule Set in Prevention mode; probes verified blocked (403) |
| Data Factory | GetMetadata → If → Copy (CSV → SQL) → Stored procedure → Copy/archive; nightly trigger; diagnostics to Log Analytics |
| Key Vault | RBAC-only; app settings use Key Vault references; APIM named values from Key Vault |
| Blob Storage | Raw payload archive, partner files, claim documents; no public access; identity-based access |
| API Management | Product + subscription keys, rate limiting, backend credential injection, header stripping |
| Azure Functions | Flex Consumption, HTTP + timer triggers, identity-based storage, structured logging |
| Site Recovery | Azure-to-Azure replication (East → Southeast), policy, mappings, scripted test failover drill |
| Monitor / Log Analytics | Availability test from 3 regions, metric + log alerts, KQL queries, workspace-based App Insights |
| Container Apps / ACR | Scheduled Container Apps Job, image in ACR, pull via managed identity, Trivy scan in CI |
| PowerShell / Bash / Python | All deploy automation in PowerShell; CI in Bash |
| CI/CD, DevSecOps, SAST/DAST/SCA | Analyzers-as-errors, CodeQL (SAST), NuGet/npm audit + Dependabot (SCA), Trivy (containers), gitleaks (secrets), WAF probes (DAST-style) |
| AI-assisted / agentic delivery, reusable AI assets | `AGENTS.md`, Copilot instructions, prompt files, PR reviewer agent, PR template AI checklist |
| Responsible AI, prompt injection, credentials | Redaction before any model call, AI can't override rules, structured output, no secrets in prompts, Key Vault for model key |

## Stories worth telling (true things that happened while building this)

- **The formatter that wrote bad code.** Running `dotnet format` applied an analyzer "fix" that marked a field
  `[Obsolete]` and inserted stubs throwing `NotImplementedException`. Caught on review; CI now only *verifies*
  formatting and never rewrites code. → *why automated changes still need a human checkpoint.*
- **A silent failure in my own deploy script.** The script reported "granted SQL access" but the user didn't
  exist; the job then failed with `Login failed for <token-identified principal>`. Root cause: PowerShell's
  `ErrorActionPreference = 'Continue'` let a .NET exception pass. Fixed with explicit try/catch + retries for
  newly created identities. → *root-cause analysis, not just a retry.*
- **The WAF test that proved nothing.** The first SQL-injection probe returned 404, not 403: the payload was in the
  URL path, where the rules don't score it the way they score arguments. Rewrote the test with query-string probes;
  both now return 403. → *a test that passes isn't the same as a test that proves something.*
- **Supply chain.** NuGet audit flagged a high-severity vulnerability in a package pulled in transitively by the
  SQL driver; pinned the patched version. Trivy scan of the container: 0 high/critical.
- **Free-trial realities.** ACR Tasks are disabled on trial subscriptions → build locally and push; small VM sizes
  were capacity-restricted → script tries a list of sizes; Key Vault role propagation delays → retry with backoff.

## Questions to ask them

- Do you run Azure DevOps or GitHub (or both)? Where do the pipelines live?
- How do you govern AI-generated code today: review rules, approved tools, what data can go into prompts?
- What does the core-platform estate look like: how much is still IaaS (and protected by Site Recovery)?
- How do you handle incidents: on-call rota, runbooks, post-incident reviews?
