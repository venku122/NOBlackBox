#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${TMPDIR:-/tmp}/dotnet-cli-home}"
cd "$ROOT_DIR"

"$DOTNET" tool restore

FORMAT_INCLUDE=()
for path in src tests bridge; do
  if [ -e "$path" ]; then
    FORMAT_INCLUDE+=(--include "$path")
  fi
done

WORKSPACE=""
if compgen -G "*.sln" > /dev/null; then
  WORKSPACE="$(ls *.sln | head -n 1)"
elif compgen -G "*.csproj" > /dev/null; then
  WORKSPACE="$(ls *.csproj | head -n 1)"
fi

if [ -n "$WORKSPACE" ]; then
  "$DOTNET" format "$WORKSPACE" style --severity info "${FORMAT_INCLUDE[@]}"
  "$DOTNET" format "$WORKSPACE" analyzers --severity warn "${FORMAT_INCLUDE[@]}"
fi

"$DOTNET" csharpier format .
