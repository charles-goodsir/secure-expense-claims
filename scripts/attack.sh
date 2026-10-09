#!/usr/bin/env bash
# Phase 3 attacks on access control: each request is something a user shouldn't be able to
# do, with the status the API should answer. Prints PASS or FAIL per attack and exits 1 if
# any failed. Creates its own claims, so run it on a fresh session's database.
#
# Usage: scripts/attack.sh https://<app-url>
#   Asks you to copy each test user's access token in turn (DevTools > Network > any /api
#   request > Authorization header, without "Bearer "). Tokens last about an hour.
# Local check of the script itself: DEV=1 scripts/attack.sh http://localhost:5105
#   uses the dev sign-in headers and the @example.com seed users instead.
# Values are read through ${!name}, which ShellCheck can't follow.
# shellcheck disable=SC2034,SC2154
set -euo pipefail

BASE=${1:?usage: scripts/attack.sh <app-url>}
API="$BASE/api"
[[ ${DEV:-} == 1 ]] && API=$BASE
failures=0

# macOS ships bash 3.2, which has no associative arrays, so each user's values live in
# variables named after them: TOKEN_alice, ID_alice and so on.
ROLES_alice=Employee ROLES_manny=Employee,Manager ROLES_fiona=Employee,Finance ROLES_adam=Admin
ID_adam=0199f0a4-0000-7000-8000-0000000000ad # dev mode only, until the real IDs are read
if [[ ${DEV:-} != 1 ]]; then
  # Read from the clipboard: macOS terminals accept at most 1024 characters on one input
  # line, and an Entra access token is longer than that.
  for user in alice manny fiona adam; do
    read -rp "Copy $user's access token, then press Enter: "
    printf -v "TOKEN_$user" '%s' "$(pbpaste | tr -d '[:space:]')"
    token="TOKEN_$user"
    [[ ${!token} == eyJ* ]] || { echo "That doesn't look like a token (should start eyJ)." >&2; exit 1; }
  done
  pbcopy </dev/null # don't leave the last token on the clipboard
fi
id() { local name="ID_$1"; echo "${!name}"; }

sign_in() { # sign_in <user>: curl arguments that sign in as that user
  if [[ ${DEV:-} == 1 ]]; then
    local roles="ROLES_$1"
    printf '%s\n' -H "X-Dev-User: $(id "$1")" -H "X-Dev-Roles: ${!roles}"
  else
    local token="TOKEN_$1"
    printf '%s\n' -H "Authorization: Bearer ${!token}"
  fi
}
call() { # call <user> <method> <path> [json] [curl args...]: prints the body, then the status
  local user=$1 method=$2 path=$3 body=${4:-} args=()
  shift 4 2>/dev/null || shift $#
  while IFS= read -r arg; do args+=("$arg"); done < <(sign_in "$user")
  [[ -n $body ]] && args+=(-H 'Content-Type: application/json' --data "$body")
  curl -s -X "$method" "${args[@]}" "$@" -w '\n%{http_code}' "$API$path"
}
status() { call "$@" | tail -n1; }
body() { call "$@" | sed '$d'; }
check() { # check <threat> <description> <expected> <actual>
  if [[ $4 == "$3" ]]; then
    printf 'PASS  %-4s %-62s %s\n' "$1" "$2" "$4"
  else
    printf 'FAIL  %-4s %-62s expected %s, got %s\n' "$1" "$2" "$3" "$4"
    failures=$((failures + 1))
  fi
}
new_claim() { body "$1" POST /claims "{\"description\":\"$2\",\"amount\":12.50}" | jq -r .id; }
set_manager() { status adam PUT "/admin/users/$(id "$1")/manager" "{\"managerId\":\"$(id "$2")\"}"; }

# Who's who, as the Admin sees it. Every user must have signed in once.
DOMAIN=$([[ ${DEV:-} == 1 ]] && echo example.com || echo onmicrosoft.com)
users=$(body adam GET /admin/users)
for user in alice manny fiona adam; do
  found=$(jq -r --arg u "$user@" --arg d "$DOMAIN" \
    '.[] | select((.email | startswith($u)) and (.email | endswith($d))) | .id' <<<"$users")
  [[ -n $found ]] || { echo "No user row for $user: sign in as them once first." >&2; exit 1; }
  printf -v "ID_$user" '%s' "$found"
done
set_manager alice manny >/dev/null
set_manager fiona manny >/dev/null

# Alice's claim, with a receipt, submitted to Manny.
alice_claim=$(new_claim alice "Attack test: taxi")
printf '\x89PNG\r\n\x1a\n0000' >/tmp/attack-receipt.png
receipt=$(call alice POST "/claims/$alice_claim/receipts" "" \
  -F "file=@/tmp/attack-receipt.png;type=image/png" | sed '$d' | jq -r .id)
status alice POST "/claims/$alice_claim/submit" >/dev/null
echo "Alice's claim $alice_claim, receipt $receipt"
echo

echo "== I1/I2: reading someone else's claim or receipt"
check I1 "Fiona reads Alice's claim by ID" 404 "$(status fiona GET "/claims/$alice_claim")"
check I1 "...same answer as a claim that doesn't exist" 404 "$(status fiona GET "/claims/$(uuidgen)")"
check I2 "Fiona lists receipts on Alice's unapproved claim" 404 "$(status fiona GET "/claims/$alice_claim/receipts")"
check I2 "Fiona downloads Alice's receipt" 404 "$(status fiona GET "/claims/$alice_claim/receipts/$receipt")"
check I2 "Manny (her manager) downloads Alice's receipt" 200 "$(status manny GET "/claims/$alice_claim/receipts/$receipt")"

echo "== E1: workflow actions without the role"
check E1 "Alice approves her own claim" 403 "$(status alice POST "/claims/$alice_claim/approve")"
check E1 "Alice pays her own claim" 403 "$(status alice POST "/claims/$alice_claim/pay")"
check E1 "Fiona (Finance, not Manager) approves Alice's claim" 403 "$(status fiona POST "/claims/$alice_claim/approve")"
check E1 "Manny calls the admin user list" 403 "$(status manny GET /admin/users)"
check E1 "Manny changes his own manager" 403 "$(status manny PUT "/admin/users/$(id manny)/manager" "{\"managerId\":\"$(id alice)\"}")"

echo "== E4: the Admin can't use the workflow"
check E4 "Adam approves Alice's claim" 403 "$(status adam POST "/claims/$alice_claim/approve")"
check E4 "Adam pays Alice's claim" 403 "$(status adam POST "/claims/$alice_claim/pay")"
check E4 "Adam lists claims" 403 "$(status adam GET /claims)"

echo "== T: tampering with a claim"
check T1 "Alice edits her claim after submitting it" 409 "$(status alice PUT "/claims/$alice_claim" '{"description":"Attack test: taxi","amount":9999}')"
check T1 "Alice creates a claim that's already approved" 400 "$(status alice POST /claims '{"description":"x","amount":1,"status":"Approved"}')"
check T1 "Alice creates a claim as Fiona" 400 "$(status alice POST /claims "{\"description\":\"x\",\"amount\":1,\"employeeId\":\"$(id fiona)\"}")"

echo "== E2: approving someone who isn't your report"
fiona_claim=$(new_claim fiona "Attack test: lunch")
set_manager fiona alice >/dev/null
status fiona POST "/claims/$fiona_claim/submit" >/dev/null
check E2 "Manny approves Fiona's claim after she moved to Alice" 404 "$(status manny POST "/claims/$fiona_claim/approve")"
set_manager fiona manny >/dev/null

echo "== E3: approving or paying your own claim"
check E3 "Adam makes Manny his own manager" 400 "$(set_manager manny manny)"
manny_claim=$(new_claim manny "Attack test: parking")
set_manager manny alice >/dev/null
status manny POST "/claims/$manny_claim/submit" >/dev/null
check E3 "Manny approves his own claim" 404 "$(status manny POST "/claims/$manny_claim/approve")"
check E3 "Manny approves Fiona's claim (now his report again)" 200 "$(status manny POST "/claims/$fiona_claim/approve")"
check E3 "Fiona pays her own approved claim" 404 "$(status fiona POST "/claims/$fiona_claim/pay")"

echo "== Happy path, then replay"
check -   "Manny approves Alice's claim" 200 "$(status manny POST "/claims/$alice_claim/approve")"
check -   "Fiona pays Alice's claim" 200 "$(status fiona POST "/claims/$alice_claim/pay")"
check T1  "Fiona pays it a second time" 409 "$(status fiona POST "/claims/$alice_claim/pay")"

if [[ ${DEV:-} != 1 ]]; then
  echo "== S1: tokens that aren't Alice's real token"
  check S1 "No token" 401 "$(curl -s -o /dev/null -w '%{http_code}' "$API/me")"
  graph=$(az account get-access-token --resource-type ms-graph --query accessToken -o tsv)
  check S1 "A real Entra token for Microsoft Graph, not this API" 401 \
    "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $graph" "$API/me")"
  forged=$(python3 - "$TOKEN_alice" <<'PY'
import base64, json, sys
header, payload, signature = sys.argv[1].split(".")
decode = lambda part: json.loads(base64.urlsafe_b64decode(part + "=" * (-len(part) % 4)))
encode = lambda obj: base64.urlsafe_b64encode(json.dumps(obj).encode()).rstrip(b"=").decode()
claims = decode(payload)
claims["roles"] = ["Employee", "Manager", "Finance", "Admin"]
print(f"{header}.{encode(claims)}.{signature}")
PY
  )
  check S1 "Alice's token with every role added" 401 \
    "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $forged" "$API/admin/users")"
  unsigned=$(python3 - "$TOKEN_alice" <<'PY'
import base64, sys
header, payload, _ = sys.argv[1].split(".")
none = base64.urlsafe_b64encode(b'{"alg":"none","typ":"JWT"}').rstrip(b"=").decode()
print(f"{none}.{payload}.")
PY
  )
  check S1 "Alice's token re-sent with alg none and no signature" 401 \
    "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $unsigned" "$API/me")"
fi

echo
if ((failures > 0)); then
  echo "$failures attack(s) got through."
  exit 1
fi
echo "Every attack was refused."
