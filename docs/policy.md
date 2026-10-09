# Azure Policy

Built-in policies assigned by hand at `rg-expense-claims`. They're not in Terraform on
purpose: the deploy identity is Contributor, which can't write `Microsoft.Authorization`,
so the pipeline can't create, change or remove the guardrails that apply to it. The
resource group isn't destroyed at teardown, so the assignments stay between sessions.

Each policy goes in as Audit first. It only moves to Deny once the compliance scan shows
the current design passes, so a guardrail can't lock out the thing it protects.

## Assigned

| Policy                                                                     | Definition ID                          | Effect | Threat | What it checks                                                                                                          |
| -------------------------------------------------------------------------- | -------------------------------------- | ------ | ------ | ----------------------------------------------------------------------------------------------------------------------- |
| Storage accounts should prevent shared key access                          | `8c6a50c6-9ffd-4ae7-986f-5fa6111f9a54` | Deny   | I5, E5 | `allowSharedKeyAccess` is false, so only Entra identities can reach the data                                            |
| Storage account public access should be disallowed                         | `4fa4b6c0-31ca-4c0d-b10d-24b96f62a751` | Deny   | I2     | `allowBlobPublicAccess` is false, so no container can be made anonymous                                                 |
| Storage accounts should restrict network access                            | `34c877ad-507e-4c82-993e-3452a6e0ad3c` | Deny   | I2, I6 | The storage firewall's default action is Deny                                                                           |
| Secure transfer to storage accounts should be enabled                      | `404c3081-a854-4457-ae30-26a93ef643f9` | Deny   | I2     | HTTPS only                                                                                                              |
| Public network access should be disabled for PostgreSQL flexible servers   | `5e1de0e3-42cb-4ebc-a86d-61d0c619ca48` | Deny   | I6     | Flags a server that's only partly private; one in a delegated subnet with a private DNS zone, like ours, passes         |
| Container Apps should only be accessible over HTTPS                        | `0e80e269-43a4-4ae9-b5bc-178126b8a5cb` | Deny   | S1, I  | Ingress doesn't allow plain HTTP, so tokens never travel unencrypted                                                    |
| Allowed locations: `australiaeast`                                         | `e56962a6-4747-49cd-b67b-bf8b01975c4c` | Deny   | D3     | Nothing is created elsewhere. Resources with location `global` (the private DNS zone) are excluded by the policy itself |
| [Preview] PostgreSQL flexible server should have Entra-only authentication | `fa498b91-8a7e-4710-9578-da944c68d1fe` | Audit  | S, E5  | Password login is off. This policy has no Deny effect, so it reports but can't block                                    |

## Considered and not assigned

| Policy                                                          | Definition ID                          | Why not                                                                                                                                                                                                        |
| --------------------------------------------------------------- | -------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Storage accounts should disable public network access           | `b2982f36-99f2-4db5-8eff-283140c09693` | The receipts account keeps its public endpoint behind a firewall that only admits `snet-apps` through a service endpoint. A private endpoint would satisfy this, at a cost (known shortcut in `bootstrap.md`). |
| Container Apps environment should disable public network access | `d074ddf8-01a5-4b5e-a2b8-964aed452c0a` | The app is meant to be reachable from the internet. Its protection is Entra sign-in, not the network.                                                                                                          |

## Limits

- Policy checks resource configuration, not who reads the data. That's RBAC and the API.
- Terraform plan doesn't evaluate policy. A change that breaks a Deny policy plans fine and fails at apply with `RequestDisallowedByPolicy`.
- These assignments aren't covered by the weekly drift check, which only compares Terraform. Check them with:
  `az policy assignment list -g rg-expense-claims --query "[].{name:displayName, effect:parameters.effect.value}" -o table`
