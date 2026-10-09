# Windows equivalent of setup-local.sh: checks prerequisites, tests the solution, prepares the previewer.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
$missing = $false
function Check($cmd, $label, $hint) {
  if (Get-Command $cmd -ErrorAction SilentlyContinue) { Write-Host "  OK  $label" } else { Write-Host "  --  $label  ($hint)"; $script:missing = $true }
}
Write-Host "Naija Kart local setup"
Check dotnet  ".NET SDK 8.0" "https://dotnet.microsoft.com/download"
Check node    "Node.js 18+"  "https://nodejs.org"
Check ffmpeg  "ffmpeg"       "https://ffmpeg.org  (winget install Gyan.FFmpeg)"
Check python  "Python 3"     "https://python.org"
if (-not (Get-Command uvx -ErrorAction SilentlyContinue)) { Write-Host "  -   uvx not found: optional, only for the Blender MCP server (https://docs.astral.sh/uv/)" }
if ($missing) { Write-Host "Install the missing tools above, then run this script again."; exit 1 }

Write-Host "Restoring and testing the .NET solution"
dotnet test NaijaKart.sln --nologo -v q
Write-Host "Preparing the world previewer"
Push-Location tools/world-preview; npm install --no-audit --no-fund; node setup.js; Pop-Location
Write-Host "Validating content"
dotnet run --project Server/NaijaKart.Server --no-build -- validate-config
Write-Host ""
Write-Host "Done. Next:"
Write-Host "  dotnet run --project Server/NaijaKart.Server -- export-world --track third_mainland_rush --out tools/world-preview/public/world.json"
Write-Host "  dotnet run --project Server/NaijaKart.Server -- simulate --players 8 --seed 16 --replay tools/world-preview/public/replay.json"
Write-Host "  cd tools/world-preview; node still.js --t=14 --mode=chase --out=frame.png"
