---
mode: agent
description: Add a new API endpoint to ThriveLink with tests, following the repo's conventions and gates.
---

Add a new endpoint to `src/ThriveLink.Api` as described by the user: ${input:endpoint:What should the endpoint do?}

Steps:

1. Read `AGENTS.md`, `src/ThriveLink.Api/Endpoints.cs` and `Program.cs` first. Match the existing minimal-API style.
2. If the endpoint encodes a business rule, put the rule in `src/ThriveLink.Domain` as a pure function and unit-test
   it in `tests/ThriveLink.Domain.Tests`.
3. Return `Results.ValidationProblem` for bad input, `Results.NotFound` for unknown ids. Never return member PII
   beyond first name + last initial outside the member's own endpoint.
4. If the endpoint writes data from partners or devices, protect it with the `RequireIngestApiKey` endpoint filter.
5. Add integration tests in `tests/ThriveLink.Api.Tests` (they run against real SQL Server in Docker): happy path,
   validation failure, not found, and auth failure where relevant.
6. Run the full verify list from `AGENTS.md`. Fix anything that fails; do not weaken analyzers or lint rules.
7. Summarise: files changed, tests added, and anything the human reviewer should look at closely.

Stop and ask before changing infrastructure, auth, or existing database columns.
