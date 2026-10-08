# Azure bootstrap

Done once by hand with the az CLI on 2026-10-08. Everything after this is Terraform in `infra/`.

| What               | Name                                                                 | Why it isn't in Terraform                                                                                                                                 |
| ------------------ | -------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Resource group     | `rg-expense-claims`                                                  | Terraform manages what's inside it                                                                                                                        |
| Resource group     | `rg-expense-claims-identity`                                         | Holds the pipeline identities. The pipeline has no rights here, so it can't change its own access                                                         |
| Budget             | `expense-claims`, US$10/month, email at 50% actual and 100% forecast | Has to exist before anything billable (D3)                                                                                                                |
| State container    | `tfstatecg21836/expense-claims`                                      | Separate from the landing zone's `tfstate` container                                                                                                      |
| State blob         | `expense-claims.tfstate`, empty                                      | Seeded once by running `terraform init` with a temporary self-assigned Storage Blob Data Contributor role on the container, removed afterwards            |
| Identity           | `id-expense-claims-plan`                                             | Reader on the RG, Blob Data Reader on the state container. Federated to `repo:charles-goodsir@175671216/secure-expense-claims@1399313160:pull_request`    |
| Identity           | `id-expense-claims-deploy`                                           | Contributor on the RG, blob data on the state container. Federated to `repo:charles-goodsir@175671216/secure-expense-claims@1399313160:environment:azure` |
| GitHub environment | `azure`                                                              | Required reviewer, `main` only                                                                                                                            |
| Provider           | `Microsoft.App`                                                      | Registered for Container Apps                                                                                                                             |

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

The repo uses GitHub's immutable OIDC subjects, so the subject includes the owner and repo IDs: `repo:charles-goodsir@175671216/secure-expense-claims@1399313160:pull_request`. A name-only subject fails with AADSTS700213. Check the format with `gh api repos/charles-goodsir/secure-expense-claims/actions/oidc/customization/sub`. The IDs are why it's safer: a deleted or renamed repo's name can be reused by someone else, but its ID can't.

A read-only identity can't initialise state. When no state blob exists, `terraform init` creates an empty one, which needs a lock and a write, so the plan identity's first run failed with `403 AuthorizationPermissionMismatch` after `404 The specified blob does not exist`. Once the blob exists, plans only read it.
