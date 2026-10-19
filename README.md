# Secure Expense Claims

An expense claims app (ASP.NET Core API, React + TypeScript) on Azure, built to be
threat-modelled, attacked and monitored. Staff submit claims with receipts, managers
approve them, finance pays them.

Status: Phases 0 to 5 complete (guardrails, app, Azure infrastructure, Entra ID sign-in with
attacked access control, Azure Policy and Defender, Sentinel detections). Next: the wrap-up.
See `docs/threat-model.md` for what each control was tested against, `docs/policy.md` and
`docs/defender-triage.md` for the Phase 4 guardrails, and `docs/detections.md` for the rules.
