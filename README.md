# Secure Expense Claims

An expense claims app (ASP.NET Core API, React + TypeScript) on Azure, built to be
threat-modelled, attacked and monitored. Staff submit claims with receipts, managers
approve them, finance pays them.

Status: Phases 0 to 3 complete (guardrails, app, Azure infrastructure, Entra ID sign-in with
attacked access control). Next: Azure Policy and Defender. See `docs/threat-model.md` for
what each control was tested against.
