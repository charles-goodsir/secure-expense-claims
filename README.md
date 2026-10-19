# Secure Expense Claims

An expense claims app (ASP.NET Core API, React + TypeScript) on Azure, built to be
threat-modelled, attacked and monitored. Staff submit claims with receipts, managers
approve them, finance pays them, and an admin manages reporting lines. Every state change
writes an audit record.

Status: all phases complete (guardrails, app, Azure infrastructure, Entra ID sign-in with
attacked access control, Azure Policy and Defender, Sentinel detections). `docs/threat-model.md`
lists what each control was tested against.

## Architecture

```
browser ──► Container App (ASP.NET Core API + the built React app, one origin)
               │  managed identity, no secrets
               ├──► Postgres Flexible Server  (private subnet, Entra-only auth)
               └──► Blob Storage              (firewalled to the app subnet, no keys)
                      └─ Defender for Storage scan tag, checked before any download
console logs ──► Log Analytics ──► Microsoft Sentinel rules
GitHub Actions ─ OIDC, no secrets ─► Terraform plan (read-only) / apply (needs approval)
```

Azure resources, all in `infra/`: a virtual network with two subnets, a private-DNS Postgres
Flexible Server, a storage account and receipts container, a Container Apps environment with
the API and a migration job, two user-assigned managed identities, a Log Analytics workspace
with Sentinel and two detection rules, and the Defender for Storage enrolment. The guardrail
policies, the app registrations and the bootstrap (state, pipeline identities, budgets) are
made by hand, because the pipeline can't be allowed to change them. See `docs/bootstrap.md`.

## What was proven, and how

| Area           | Control                                                                                           | How it was shown                                                                                              |
| -------------- | ------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Repo and CI    | Signed commits, eight required checks, every Action pinned by SHA, secret scan, SAST, image scan  | A fake secret blocked a PR; a broken workflow copy failed the lint job; a vulnerable package failed restore   |
| Access control | Ownership in the query (404, not 403), separation of duties, audit row in the same transaction    | 28 attacks with real Entra tokens, none got through; each control removed once to see its test fail           |
| Identity       | Entra ID tokens validated by issuer, audience, signature and roles; no passwords or keys in Azure | Forged and role-edited tokens return 401; an unassigned account got through once, which led to a second layer |
| Pipeline       | OIDC, a read-only plan identity, an apply that waits for approval, a weekly drift check           | A PR pointed at the deploy identity was refused; a portal tag turned the drift check red                      |
| Guardrails     | Eight Azure policies, assigned by hand so the pipeline can't remove them                          | Each Deny policy tripped as Owner; a Terraform policy assignment planned green and failed on apply            |
| Uploads        | Content type by magic bytes, server-chosen names, and a fail-closed download gate on a scan tag   | A clean blob returns 200; a Malicious tag and a missing tag both return 409                                   |
| Detection      | Audit rows copied to the logs, and two Sentinel rules written as Terraform                        | Each rule tested on made-up lines first, then on the live app, where the second raised a real incident        |

## Problems hit

- A cancelled apply left an orphaned storage account and a locked state blob.
- A Postgres destroy that runs out of its timeouts, even at 120 minutes, and a re-run that finishes it.
- "Assignment required" was enforced on the wrong app, so an unassigned account got a token.
- An apply that reported success while Defender's malware scanning stayed off, found by reading the setting back.
- A quiet Sentinel check that proved nothing, because the rule had no manager history to compare.
- A 403 on the state lock that was a free trial expiring and pausing the subscription.

## Known shortcuts

Each is in `docs/bootstrap.md`, `docs/policy.md`, `docs/detections.md` or `docs/threat-model.md`.

- The environment is destroyed after each session, so the SPA redirect URI, the manager
  assignments and the Defender scanning setting are done by hand after every rebuild.
- The Defender for Storage plan is off after its trial, so receipt downloads return 409 in
  new sessions. That's the gate failing closed, not a fault.
- The pipeline can't switch on Defender's malware scanning, so Terraform shows one standing
  change in every plan.
- The Azure policies and the app registrations aren't in Terraform and aren't in the drift check.
- Private endpoints, a custom domain, shorter token lifetimes, incident grouping and a rule for
  failed sign-ins were left out. Each has a note on what production would do.
- The subscription is pay-as-you-go with no spending limit. Budget alerts only send email, so
  destroying after each session is the real cost control.

## Teardown

Finished on 2026-10-20. The environment was destroyed with Terraform, and then the state,
the pipeline identities and the resource groups were deleted, so nothing in the Azure
subscription is billing. The Infra and drift workflows are disabled. The subscription stays
open on pay-as-you-go, with the two budget alerts, and the Defender plans are off.

Rebuilding means running the bootstrap in `docs/bootstrap.md` again: the state storage, the
two pipeline identities and their federated credentials, the guardrail policies, and the
Entra app registrations.
