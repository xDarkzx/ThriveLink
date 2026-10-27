# GitHub Copilot instructions

Follow [AGENTS.md](../AGENTS.md) at the repo root. It holds the ground rules, build/verify commands, project map
and conventions for every AI agent in this repo.

Short version for inline suggestions:

- C# (.NET 10): file-scoped namespaces, primary constructors, `TimeProvider` instead of `DateTime.Now`,
  `CultureInfo.InvariantCulture` when parsing or formatting, parameterised SQL only.
- TypeScript: strict mode, no `any`, no non-null assertions; ESLint `strictTypeChecked` must pass with zero warnings.
- Synthetic data only. Never suggest real personal or health information, keys or connection strings.
- Prefer small functions in `ThriveLink.Domain` for business rules so they can be unit-tested without a database.
