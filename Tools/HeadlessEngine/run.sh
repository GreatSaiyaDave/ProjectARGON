#!/usr/bin/env bash
# Build and run the WRLDZ rules engine regression/stress suites headlessly
# (no Unity Editor). Exits non-zero if any suite reports a failure.
#
# Usage: Tools/HeadlessEngine/run.sh [--quiet] [dotnet run args...]
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Prefer a repo-local or user dotnet if PATH doesn't already have one.
if ! command -v dotnet >/dev/null 2>&1; then
  if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export PATH="$HOME/.dotnet:$PATH"
  else
    echo "error: dotnet SDK not found. Install .NET 8 SDK (e.g. https://dot.net/v1/dotnet-install.sh --channel 8.0)." >&2
    exit 127
  fi
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

dotnet build "$HERE/HeadlessEngine.csproj" -v quiet -nologo
exec dotnet run --project "$HERE/HeadlessEngine.csproj" --no-build -- "$@"
