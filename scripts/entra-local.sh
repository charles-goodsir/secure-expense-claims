#!/usr/bin/env bash
# Runs the API in Production mode and the web app with Entra sign-in, against the Compose
# database and Azurite, to test real Entra tokens locally. Ctrl+C stops both.
# Needs: Docker running, `az login` to the tenant, and a migrated database (run Compose or
# the dev API once first).
set -euo pipefail
cd "$(dirname "$0")/.."

# Client IDs are in docs/bootstrap.md. The tenant ID comes from az so it stays out of the repo.
API_CLIENT_ID=3ed6cc2b-9474-4e94-ac5b-30b0bd8d4046 # gitleaks:allow (client ID, not a secret)
WEB_CLIENT_ID=105aa896-ce51-4404-8807-7a6a09161a8b
TENANT_ID=$(az account show --query tenantId -o tsv)

set -a
source .env
set +a

docker compose up -d db blobs

ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__Claims="Host=localhost;Database=expenseclaims;Username=expenseclaims;Password=$POSTGRES_PASSWORD" \
ConnectionStrings__Receipts=UseDevelopmentStorage=true \
Authentication__Schemes__Bearer__Authority="https://login.microsoftonline.com/$TENANT_ID/v2.0" \
Authentication__Schemes__Bearer__ValidAudiences__0="$API_CLIENT_ID" \
  dotnet run --project src/ExpenseClaims.Api --no-launch-profile --urls http://localhost:5105 &

# dotnet run starts the API as a child process, so stop the whole process group.
trap 'kill 0' EXIT

VITE_ENTRA_CLIENT_ID="$WEB_CLIENT_ID" \
VITE_ENTRA_TENANT_ID="$TENANT_ID" \
VITE_API_SCOPE="api://$API_CLIENT_ID/Claims.ReadWrite" \
  npm --prefix web run dev
