# Azure bootstrap

Done once by hand with the az CLI on 2026-10-08. Everything after this is Terraform in `infra/`.

| What               | Name                                                                 | Why it isn't in Terraform                                                                                                            |
| ------------------ | -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| Resource group     | `rg-expense-claims`                                                  | Terraform manages what's inside it                                                                                                   |
| Resource group     | `rg-expense-claims-identity`                                         | Holds the pipeline identities. The pipeline has no rights here, so it can't change its own access                                    |
| Budget             | `expense-claims`, US$10/month, email at 50% actual and 100% forecast | Has to exist before anything billable (D3)                                                                                           |
| State container    | `tfstatecg21836/expense-claims`                                      | Separate from the landing zone's `tfstate` container                                                                                 |
| Identity           | `id-expense-claims-plan`                                             | Reader on the RG, Blob Data Reader on the state container. Federated to `repo:charles-goodsir/secure-expense-claims:pull_request`    |
| Identity           | `id-expense-claims-deploy`                                           | Contributor on the RG, blob data on the state container. Federated to `repo:charles-goodsir/secure-expense-claims:environment:azure` |
| GitHub environment | `azure`                                                              | Required reviewer, `main` only                                                                                                       |
| Provider           | `Microsoft.App`                                                      | Registered for Container Apps                                                                                                        |

No client secrets exist. GitHub holds only IDs, as repository variables: `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_CLIENT_ID_PLAN` and `AZURE_CLIENT_ID_DEPLOY`.

## Checking it

```bash
az identity federated-credential list -g rg-expense-claims-identity --identity-name id-expense-claims-deploy --query "[].subject" -o tsv
```

Each identity should have exactly two role assignments: Reader or Contributor on `rg-expense-claims`, and a Storage Blob Data role on the `expense-claims` state container (Reader for plan, Contributor for deploy). Nothing at subscription scope.

## Known shortcuts

- PR plans run with `-lock=false`, so the plan identity only needs read access to state. A PR branch can't write to state, and two plans at once can't corrupt anything because neither writes.
- Cost: the environment is destroyed at the end of every work session and rebuilt with `apply` at the start. Nothing runs while I'm not working on it.

## Gotcha

In zsh, `"repo:$REPO:environment:azure"` doesn't expand the way it looks. `$REPO:e` is zsh's "file extension" modifier, so the first deploy credential was created with the subject `repo:nvironment:azure` and could never have matched. Write the subject out in full, or use `${REPO}`.
