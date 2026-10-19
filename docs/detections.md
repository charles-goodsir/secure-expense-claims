# Detections

Sentinel runs on `log-expense-claims`, turned on in Terraform (`infra/main.tf`). Both rules are code, because the workspace is destroyed and rebuilt each session. They read the `AUDIT` lines the API writes to the console after each audit row is saved (`ClaimsDbContext.SaveChangesAsync`). Only ids go in the line: no names, amounts or bank details.

Container Apps splits each console entry into two records and indents the message, so queries match with `contains "AUDIT action="`, never `startswith`.

## Rules

| Rule                                                                | Threat | Severity | What it catches                                                                                                                                                                                    | Proof                                                                                                                                                                                                                                                                                 |
| ------------------------------------------------------------------- | ------ | -------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Approval or rejection by someone who isn't the employee's manager   | R2, E3 | High     | A decision where the actor isn't the employee's latest manager in the `SetManager` lines. The API already refuses this with a 404, so the rule is a second layer for an API bug or a database edit | Made-up lines: a legitimate approval stays quiet, an approval after the manager changed alerts, an employee with no history is ignored. Flipping `!=` to `==` swapped the rows. Live: a query showing each decision with its manager said "manager matches" for a legitimate approval |
| Approval or rejection soon after the employee's manager was changed | R2     | Medium   | A decision by a manager assigned less than an hour earlier, the pattern for routing someone's claims to a friend                                                                                   | Made-up lines: 30 minutes alerts, 4 hours doesn't, a non-manager doesn't. Live: Adam set Alice's manager to Manny, Manny approved her claim, and an incident appeared                                                                                                                 |

Both run every 5 minutes over the last day and examine decisions from the last 15 minutes, so one event can alert more than once.

Threat T3 (audit records edited or deleted) gets a second copy of each audit row in the logs, which a database edit can't change. No rule compares the two.

## Limits

- **No manager history, no verdict.** Both rules read the manager from `SetManager` lines. If none exists in the retained logs for an employee, the first rule is silent. This happened on the first live check: both approvals had no history, so "no incidents" proved nothing until the manager was set again. Managers are set each session after a rebuild, so it holds there. Production would log the manager at decision time.
- **The second rule fires in test sessions,** because the admin sets managers and approves soon after. That makes it a useful real-path check. Production would tune the one-hour window.
- **Duplicate alerts** are possible because the 15-minute window overlaps the 5-minute schedule. There is no incident grouping.
- **The rules are detective.** They raise an incident and block nothing.
- **Ingestion lag** is a few minutes, so an alert arrives within about 10 to 15 minutes of the event.
- **The daily cap** on the workspace (100 MB) could drop log lines in a noisy session.

## Not built

- **Failed sign-ins and forged tokens (S1).** The API doesn't log token-validation failures, and `Microsoft.AspNetCore` is at Warning, so there are no request or auth-failure lines to read. It would need its own log line, plus the sign-in logs from Entra, which need a paid licence this tenant doesn't have.
- **Policy denials and the Defender malicious-file alert.** The second needs the Defender for Storage plan on, which is off after its trial.
- **Approvals with no manager history** as their own rule (see Limits).

## Sentinel notes

Azure adds a rule of its own when Sentinel is turned on, "Advanced Multistage Attack Detection". It is enabled and not in Terraform. I haven't checked what it costs. Sentinel bills per GB analysed after any trial, and the workspace's daily cap bounds it.
