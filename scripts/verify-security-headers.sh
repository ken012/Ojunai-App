#!/usr/bin/env bash
# Smoke-test the cache/security headers on a deployed Ojunai stack.
#
# Usage:
#   scripts/verify-security-headers.sh                                    # prod defaults
#   scripts/verify-security-headers.sh https://api.ojunai.com https://ojunai.com
#   scripts/verify-security-headers.sh http://localhost:5001 http://localhost:3000
#
# Run after every deploy. Exits non-zero on any failure so it slots into CI.

set -uo pipefail

API="${1:-https://api.ojunai.com}"
WEB="${2:-https://ojunai.com}"

pass=0
fail=0

ok()  { printf "  \033[32m✓\033[0m %s\n" "$1"; pass=$((pass+1)); }
bad() { printf "  \033[31m✗\033[0m %s\n" "$1"; fail=$((fail+1)); }

# Fetch a single header value (last one wins) from a URL.
header_of() {
  curl -ksSL -o /dev/null -D - --max-time 8 "$1" 2>/dev/null \
    | tr -d '\r' \
    | awk -v IGNORECASE=1 -v h="$2" 'tolower($1) == tolower(h ":") { $1=""; sub(/^ /, ""); print }' \
    | tail -1
}

assert_header_matches() {
  local url="$1" header="$2" pattern="$3" label="$4"
  local actual
  actual="$(header_of "$url" "$header")"
  if [[ -z "$actual" ]]; then
    bad "$label — header '$header' missing on $url"
  elif echo "$actual" | grep -qi -- "$pattern"; then
    ok "$label — '$header: $actual'"
  else
    bad "$label — '$header: $actual' (want match: $pattern)"
  fi
}

assert_header_lacks() {
  local url="$1" header="$2" pattern="$3" label="$4"
  local actual
  actual="$(header_of "$url" "$header")"
  if echo "$actual" | grep -qi -- "$pattern"; then
    bad "$label — '$header: $actual' should NOT contain '$pattern'"
  else
    ok "$label — header clean of '$pattern' on $url"
  fi
}

echo
echo "API: $API"
echo "Web: $WEB"
echo
echo "── 1. /api/* responses must say no-store ─────────────────────────────"
# Use the auth/login endpoint — exists in every environment, headers are set
# by the global middleware regardless of method/status.
assert_header_matches "$API/api/auth/login"     "Cache-Control" "no-store"     "/api/auth/login Cache-Control"
assert_header_matches "$API/api/auth/login"     "Pragma"        "no-cache"     "/api/auth/login Pragma"
assert_header_matches "$API/api/auth/login"     "Expires"       "0"            "/api/auth/login Expires"

echo
echo "── 2. Static assets must remain cacheable (no Cache-Control: no-store) "
assert_header_lacks   "$WEB/brand/icon-256.png" "Cache-Control" "no-store"     "/brand/icon-256.png"
assert_header_lacks   "$WEB/favicon-32.png"     "Cache-Control" "no-store"     "/favicon-32.png"
assert_header_lacks   "$WEB/manifest.webmanifest" "Cache-Control" "no-store"   "/manifest.webmanifest"

echo
echo "── 3. Defense-in-depth security headers ──────────────────────────────"
assert_header_matches "$API/api/auth/login" "X-Content-Type-Options" "nosniff"            "X-Content-Type-Options"
assert_header_matches "$API/api/auth/login" "X-Frame-Options"        "DENY"               "X-Frame-Options"
assert_header_matches "$API/api/auth/login" "Referrer-Policy"        "strict-origin"      "Referrer-Policy"
assert_header_matches "$API/api/auth/login" "Permissions-Policy"     "geolocation"        "Permissions-Policy"

# HSTS only in prod (the .NET middleware gates it on IsProduction). Skip the
# check if running against a non-https endpoint.
if [[ "$API" == https://* ]]; then
  assert_header_matches "$API/api/auth/login" "Strict-Transport-Security" "max-age" "HSTS (prod)"
fi

echo
echo "── 4. Cookie flags on /api/auth/login response ───────────────────────"
# Trigger a cookie-setting response by POSTing bogus credentials. We don't
# care about success — just whether the Set-Cookie line (if any) is locked
# down. ASP.NET writes Set-Cookie on logout too (with an empty value); same
# flags should apply.
cookie_line="$(curl -ksSL -o /dev/null -D - -X POST \
  -H 'Content-Type: application/json' -d '{}' \
  --max-time 8 "$API/api/auth/login" 2>/dev/null \
  | tr -d '\r' | grep -i '^Set-Cookie:' | head -1)"
if [[ -z "$cookie_line" ]]; then
  ok "Set-Cookie not emitted (login rejected before cookie write — expected)"
else
  echo "$cookie_line" | grep -qi 'HttpOnly'  && ok "Set-Cookie HttpOnly"  || bad "Set-Cookie missing HttpOnly:  $cookie_line"
  echo "$cookie_line" | grep -qi 'Secure'    && ok "Set-Cookie Secure"    || bad "Set-Cookie missing Secure:    $cookie_line"
  echo "$cookie_line" | grep -qi 'SameSite'  && ok "Set-Cookie SameSite"  || bad "Set-Cookie missing SameSite:  $cookie_line"
fi

echo
echo "──────────────────────────────────────────────────────────────────────"
printf "Passed: \033[32m%d\033[0m   Failed: \033[31m%d\033[0m\n" "$pass" "$fail"
echo

if (( fail > 0 )); then
  exit 1
fi
