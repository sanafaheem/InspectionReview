#!/usr/bin/env bash
# Starts the local SQL Server and Azurite containers, using the sa password stored in .NET user secrets
# (taken from the Password= part of ConnectionStrings:DefaultConnection).
set -euo pipefail
cd "$(dirname "$0")/.."

conn=$(dotnet user-secrets list --project src/InspectionReview.Web \
  | sed -n 's/^ConnectionStrings:DefaultConnection = //p')
password=$(printf '%s' "$conn" | sed -n 's/.*[Pp]assword=\([^;]*\).*/\1/p')

if [ -z "$password" ] || [ "$password" = "CHANGE_ME" ]; then
  echo "No real password found in user secrets (ConnectionStrings:DefaultConnection)." >&2
  exit 1
fi

MSSQL_SA_PASSWORD="$password" docker compose up -d
echo "SQL Server is starting on localhost:1433 (first start takes ~20s)."
echo "Azurite (blob storage) is starting on localhost:10000."
