#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${TMPDIR:-/tmp}/dotnet-cli-home}"
cd "$ROOT_DIR"

"$DOTNET" tool restore
"$DOTNET" csharpier check .
