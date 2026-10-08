#!/usr/bin/env bash
# Checks local prerequisites and prepares the world previewer. Safe to run repeatedly.
set -euo pipefail
cd "$(dirname "$0")/.."

ok() { printf '  \033[32m✓\033[0m %s\n' "$1"; }
miss() { printf '  \033[31m✗\033[0m %s\n' "$1"; MISSING=1; }
MISSING=0

echo "Naija Kart local setup"
command -v dotnet >/dev/null && ok "dotnet $(dotnet --version)" || miss "dotnet SDK 8.0 (https://dotnet.microsoft.com/download)"
command -v node >/dev/null && ok "node $(node --version)" || miss "Node.js 18+ (https://nodejs.org)"
command -v ffmpeg >/dev/null && ok "ffmpeg" || miss "ffmpeg (https://ffmpeg.org)"
command -v python3 >/dev/null && ok "python3 $(python3 --version | cut -d' ' -f2)" || miss "Python 3"
command -v uvx >/dev/null && ok "uvx (Blender MCP server runner)" || echo "  - uvx not found: optional, needed only for the Blender MCP server (install uv: https://docs.astral.sh/uv/)"

if [ "$MISSING" = 1 ]; then echo "Install the missing tools above, then run this script again."; exit 1; fi

echo "Restoring and testing the .NET solution"
dotnet test NaijaKart.sln --nologo -v q

echo "Preparing the world previewer"
( cd tools/world-preview && npm install --no-audit --no-fund && npm run setup )

echo "Validating content"
dotnet run --project Server/NaijaKart.Server --no-build -- validate-config

echo
echo "Done. Next:"
echo "  dotnet run --project Server/NaijaKart.Server -- export-world --track third_mainland_rush --out tools/world-preview/public/world.json"
echo "  dotnet run --project Server/NaijaKart.Server -- simulate --players 8 --seed 16 --replay tools/world-preview/public/replay.json"
echo "  (cd tools/world-preview && node still.js --t=14 --mode=chase --out=frame.png)"
