#!/usr/bin/env sh
# Creates .env from .env.example and replaces every <generated> placeholder with a random secret.
# Idempotent: an existing .env is never overwritten (pass --force to regenerate; this also requires
# deleting the Docker volumes, because SQL Server keeps the original sa password).
set -eu

cd "$(dirname "$0")/.."

if [ -f .env ] && [ "${1:-}" != "--force" ]; then
  echo ".env already exists; keeping it. Use --force to regenerate."
  exit 0
fi

# 24 chars from [A-Za-z0-9] plus a fixed "Aa1" suffix so SQL Server and Identity complexity rules always pass.
generate() {
  LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 24
  printf 'Aa1'
}

tmp=$(mktemp)
while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in
    \#*) printf '%s\n' "$line" ;;
    *"<generated>"*) printf '%s\n' "$(printf '%s' "$line" | sed "s/<generated>/$(generate)/")" ;;
    *) printf '%s\n' "$line" ;;
  esac
done < .env.example > "$tmp"
mv "$tmp" .env
chmod 600 .env

echo "Created .env with generated secrets."
echo "Sign-in credentials are in .env (ADMIN_EMAIL / ADMIN_PASSWORD)."
echo "Next: docker compose up -d --build   then open http://localhost:3000"
