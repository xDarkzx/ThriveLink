---
mode: agent
description: Investigate a ThriveLink production alert or support ticket using logs and code, and propose a fix.
---

Investigate this incident: ${input:incident:Paste the alert, ticket or symptom}

Work through it like an on-call engineer and show your evidence at each step:

1. **Restate** the symptom and its impact (which members, partners or jobs are affected, since when).
2. **Gather evidence** before forming a theory. Use the KQL in `docs/kql/` against the Log Analytics workspace
   (`az monitor log-analytics query`), App Service / Functions logs, and Data Factory run history
   (`az datafactory pipeline-run query-by-factory`). Quote the rows that matter.
3. **Trace the code path** involved (`src/ThriveLink.*`). Name the exact file and function.
4. **Root cause**: one sentence, backed by the evidence. If you can't prove it, say what's missing.
5. **Fix**: smallest change that addresses the cause, plus a test that would have caught it. Do not apply changes
   to infrastructure, SQL permissions or production data. Propose them for a human to approve.
6. **Prevention**: an alert, test or guardrail so it doesn't recur.
7. **Customer-facing note**: two plain-English sentences support can send to the affected partner or member.

Never paste member PII into the conversation; refer to members by member number only.
