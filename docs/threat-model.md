# Threat model

STRIDE model for Secure Expense Claims, written on 1 October 2026 before any app code.
Each threat names the control I plan to use and the phase it lands in. What each control
was tested against, and what it found, is recorded under the results sections at the end.

## What the app does

Employees submit expense claims with receipts. A manager approves or rejects claims from
their direct reports. Finance pays approved claims. An admin manages users and reporting
lines and reads the audit log. Every state change (submit, approve, reject, pay) writes
an audit record.

## Assets

| Asset                             | Why it matters                                                                  |
| --------------------------------- | ------------------------------------------------------------------------------- |
| Claims (amount, status, approver) | Changing one means paying money that wasn't approved                            |
| Receipts (files in Blob Storage)  | Personal data: names, card digits, addresses, travel                            |
| Employee bank details             | Where the money goes. Changing them redirects payments                          |
| Audit log                         | The only evidence of who approved or paid what                                  |
| Reporting lines                   | Decide who can approve whose claims                                             |
| Secrets and identities            | The pipeline identity and the app's managed identity can reach everything above |

## Trust boundaries

1. **Browser to API** over the internet. Nothing from the client is trusted, including role claims the UI shows.
2. **Entra ID to API.** The API trusts tokens only after checking issuer, audience, signature and expiry.
3. **API to data** (Postgres, Blob Storage, Key Vault) over private endpoints inside the VNet, using the app's managed identity.
4. **CI/CD to Azure.** The pipeline signs in with OIDC, scoped to this resource group only.
5. **Developer to repo.** Code reaches `main` only through a PR with passing checks and signed commits.

## Threats

### Spoofing

| ID  | Threat                                                                     | Control                                                                                    | Phase |
| --- | -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ | ----- |
| S1  | Attacker forges or replays a token, or uses one issued for a different app | Validate issuer, audience, signature and expiry; short token lifetime                      | 3     |
| S2  | The local stub auth used in Phase 1 ends up enabled in Azure               | Stub registered only when the environment is Development; test that it fails in Production | 1, 3  |
| S3  | Someone impersonates the pipeline to deploy to Azure                       | OIDC federated credential bound to this repo and branch or environment; no client secret   | 2     |

### Tampering

| ID  | Threat                                                                          | Control                                                                                                                    | Phase |
| --- | ------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- | ----- |
| T1  | Employee edits `amount` or `status` on a claim after approval (mass assignment) | Request DTOs that only contain editable fields; status changes only through workflow endpoints; claims locked after submit | 1     |
| T2  | Malicious or disguised file uploaded as a receipt                               | Server-side size limit, allow-list checked against file content (magic bytes), server-generated blob names                 | 1     |
| T3  | Audit records edited or deleted                                                 | No update or delete path in the API; audit rows written in the same transaction as the change; copy to Log Analytics       | 1, 5  |
| T4  | Malicious dependency or GitHub Action                                           | Actions pinned to SHAs, committed lock files, Dependabot with a cooldown, vulnerability scans in CI                        | 0     |
| T5  | Infra changed outside Terraform                                                 | Weekly drift check; Azure Policy denies the riskiest changes                                                               | 2, 4  |

### Repudiation

| ID  | Threat                                                                         | Control                                                                                                           | Phase |
| --- | ------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------- | ----- |
| R1  | Manager or finance user denies approving or paying a claim                     | Audit record with the actor's ID taken from the token, timestamp, old and new status                              | 1     |
| R2  | Admin changes a reporting line so a friend's claims go to them, then denies it | Admin changes written to the audit log; Sentinel alert on an approval by someone who isn't the employee's manager | 1, 5  |

### Information disclosure

| ID  | Threat                                                                       | Control                                                                                                      | Phase            |
| --- | ---------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ | ---------------- |
| I1  | Employee reads another employee's claim or receipt by changing the ID (IDOR) | Resource-based authorization on every claim and receipt read; 404 for claims the user can't see              | 1, attacked in 3 |
| I2  | Receipts readable straight from Blob Storage                                 | Storage public access off, private endpoint, receipts streamed through the API after the authorization check | 2                |
| I3  | Bank details shown to the wrong role                                         | Bank details returned only to the owner and Finance                                                          | 1                |
| I4  | Stack traces or SQL errors in API responses                                  | Global exception handler returning generic problem details; details go to logs only                          | 1                |
| I5  | Secrets committed to the repo                                                | gitleaks on every PR; managed identity and OIDC, so there are no app secrets to commit                       | 0, 2             |
| I6  | Database or vault reachable from the internet                                | Public network access off, private endpoints, enforced by Azure Policy                                       | 2, 4             |

### Denial of service

| ID  | Threat                                                     | Control                                                                                            | Phase |
| --- | ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------- | ----- |
| D1  | Huge uploads exhaust memory or storage                     | Request body size limit on the upload endpoint                                                     | 1     |
| D2  | Request flooding against the API                           | ASP.NET Core rate limiting per user; WAF is optional and costs money                               | 1, 4  |
| D3  | Runaway Azure cost (scaling, logging, forgotten resources) | Budget alert before any resource exists; consumption tiers; max replica count; tear down when idle | 2     |

### Elevation of privilege

| ID  | Threat                                                                    | Control                                                                                 | Phase |
| --- | ------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- | ----- |
| E1  | Employee calls approve or pay endpoints                                   | Role policies on every workflow endpoint, enforced in the API                           | 1     |
| E2  | Manager approves claims of another manager's reports                      | Resource check that the approver is the claimant's manager                              | 1     |
| E3  | Anyone approves their own claim                                           | Rule that approver and claimant can't be the same person, tested explicitly             | 1     |
| E4  | Admin grants themselves Finance or approves claims                        | Roles assigned in Entra ID, outside the app; the Admin role has no workflow permissions | 3     |
| E5  | App's managed identity or pipeline identity has more access than it needs | Data-plane roles scoped to single resources; pipeline scoped to this resource group     | 2     |

## Out of scope

- Payments. "Mark paid" records that finance paid; there's no bank integration.
- The Entra ID tenant's own security. The tenant has security defaults on, so every test user has to register for MFA. Conditional access needs a paid licence and isn't used.

## Phase 3 results: Entra ID and attacking access control

`scripts/attack.sh` runs 28 attacks against the deployed app using real Entra ID tokens for
the four test users, and fails if any of them gets through. The run from 9 October 2026 is
in `docs/phase3-attack-results.txt`: all 28 were refused. I tested the script itself by
removing the ownership check from the claim read endpoint, and it reported the IDOR.

| Threat | Attacked with                                                                                      | Result                |
| ------ | -------------------------------------------------------------------------------------------------- | --------------------- |
| S1     | No token; a real Entra token for Microsoft Graph; Alice's token with every role added; `alg: none` | 401 for all           |
| S1     | Wrong audience, wrong tenant, expired, wrong key, edited payload, missing `oid` (unit tests)       | 401 for all           |
| S2     | Dev sign-in headers sent to the API running as Production, locally and in Azure                    | 401                   |
| I1     | Fiona reads Alice's claim by ID; compared with an ID that doesn't exist                            | 404 for both          |
| I2     | Fiona lists and downloads receipts on Alice's unapproved claim; Alice's manager does the same      | 404; manager gets 200 |
| E1     | Alice approves and pays; Fiona (Finance) approves; Manny calls admin endpoints                     | 403                   |
| E2     | Manny approves Fiona's claim after she's moved to another manager, then again after she moves back | 404, then 200         |
| E3     | Admin makes Manny his own manager; Manny approves his own claim; Fiona pays her own claim          | 400, 404, 404         |
| E4     | Adam (Admin) approves, pays and lists claims                                                       | 403                   |
| T1     | Edit after submit; create with `status` or `employeeId` smuggled in; pay twice                     | 409, 400, 409         |

### Finding: assignment required on the wrong app

I'd turned on "Assignment required" on the API's enterprise app and assumed it stopped
unassigned users getting tokens. It didn't: my own account, with no role, got a token for
the API through the web app, passed validation and got a row in `Users`. Entra enforces
assignment on the app the user signs in to, which is the web app.

Fixed in two layers: the web app now requires assignment (unassigned users get
AADSTS50105), and the API rejects any token without a role before creating a user. Both
layers were checked live.

### Known gaps and shortcuts

- **Sign-out doesn't revoke access tokens.** A token copied before sign-out still got a 200
  afterwards. It stays valid until it expires (60 to 90 minutes). For production I'd use
  shorter token lifetimes, or Continuous Access Evaluation for revocation.
- **Tokens are in `sessionStorage`.** If the page had an XSS bug, script could read them.
  React's escaping and the lack of any HTML injection keep that unlikely. A
  Content-Security-Policy header would be the next control.
- **The manager picker lists every user, not just Managers.** The database doesn't store
  roles; Entra does. Assigning a non-manager fails safe, because approving still needs the
  Manager role and the reporting line (E1, E2).
- **Users are created on their first valid sign-in** and reporting lines are set by an Admin
  each session, because the database is rebuilt every session.
- **The SPA redirect URI is added by hand each session**, because the Container App's
  domain changes on every rebuild, and removed at teardown. A custom domain would fix it.
- **Client and tenant IDs are public.** They're baked into the JavaScript in a public image.
  They're identifiers, not credentials.
- **The dev sign-in code is still in the production bundle.** The server ignores those
  headers outside Development (S2), so this is cosmetic.
