#!/usr/bin/env bash
# Assigns the Azure Policy guardrails in docs/policy.md to rg-expense-claims.
# Usage: scripts/assign-policies.sh Audit   (first, to see what would break)
#        scripts/assign-policies.sh Deny    (once the compliance scan is clean)
# Re-running updates the existing assignments. The Entra-only policy has no Deny
# effect, so it always stays Audit. Run as yourself: the pipeline can't do this.
set -euo pipefail

EFFECT=${1:?usage: scripts/assign-policies.sh Audit|Deny}
[[ $EFFECT == Audit || $EFFECT == Deny ]] || { echo "Effect must be Audit or Deny" >&2; exit 1; }
RG=rg-expense-claims
MESSAGE="Blocked by a guardrail in secure-expense-claims: see docs/policy.md"

assign() { # assign <name> <definition id> <effect> [extra parameters as JSON]
  local params="{\"effect\":{\"value\":\"$3\"}${4:+,$4}}"
  az policy assignment create -g "$RG" -n "$1" --display-name "SEC: $1" \
    --policy "$2" --params "$params" --query "{name:name, effect:parameters.effect.value}" -o tsv
  az policy assignment non-compliance-message create -g "$RG" -n "$1" -m "$MESSAGE" -o none
}

assign storage-no-shared-key     8c6a50c6-9ffd-4ae7-986f-5fa6111f9a54 "$EFFECT"
assign storage-no-public-blobs   4fa4b6c0-31ca-4c0d-b10d-24b96f62a751 "$EFFECT"
assign storage-restrict-network  34c877ad-507e-4c82-993e-3452a6e0ad3c "$EFFECT"
assign storage-https-only        404c3081-a854-4457-ae30-26a93ef643f9 "$EFFECT"
assign postgres-no-public        5e1de0e3-42cb-4ebc-a86d-61d0c619ca48 "$EFFECT"
assign containerapp-https-only   0e80e269-43a4-4ae9-b5bc-178126b8a5cb "$EFFECT"
assign allowed-locations         e56962a6-4747-49cd-b67b-bf8b01975c4c "$EFFECT" \
  '"listOfAllowedLocations":{"value":["australiaeast"]}'
assign postgres-entra-only       fa498b91-8a7e-4710-9578-da944c68d1fe Audit
