#!/bin/bash
# SessionStart hook for Claude Code cloud sessions: installs the .NET 10 SDK
# and restores NuGet packages so `dotnet build` / `dotnet test` work.
set -euo pipefail

# Only run in Claude Code cloud sessions; local machines manage their own SDK.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

DOTNET_ROOT="$HOME/.dotnet"
DOTNET_CHANNEL="10.0"

# Install the SDK once; the container is cached after this hook completes.
if ! "$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL}\."; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_ROOT" --no-path
fi

export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
# PDA.Media.Desktop targets net10.0-windows (WPF); this lets it restore/build on Linux.
export EnableWindowsTargeting=true

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_ROOT\""
    echo "export PATH=\"$DOTNET_ROOT:\$HOME/.dotnet/tools:\$PATH\""
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    echo 'export EnableWindowsTargeting=true'
  } >> "$CLAUDE_ENV_FILE"
fi

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"
dotnet restore PDA.Media.slnx
