# Running Naija Kart locally

Everything is in the repository (branch `claude/naija-kart-spec-05dbtf`); nothing depends on a
cloud session. Local work also gives Claude Code access to tools that only exist on your machine,
such as the Blender MCP server.

## 1. Clone

```bash
git clone https://github.com/tifeblakez/naijakart.git
cd naijakart
git checkout claude/naija-kart-spec-05dbtf
```

## 2. Prerequisites

| Tool | Version | Why |
| --- | --- | --- |
| .NET SDK | 8.0 | server, core library, tests (`dotnet test NaijaKart.sln`) |
| Node.js | 22 (18+ works) | world previewer (`tools/world-preview`) |
| ffmpeg | any recent | turning rendered frames into MP4 |
| Python 3 | 3.10+ | `tools/gen_third_mainland_rush.py` (track data) |
| Unity | 6000.0.50f1 (URP) | the mobile client, when you get to Unity work |
| Blender | 4.x / 5.x | optional, via MCP for asset work |

`scripts/setup-local.sh` checks these and installs the previewer's npm packages.

## 3. Build, test, simulate

```bash
dotnet test NaijaKart.sln
dotnet run --project Server/NaijaKart.Server -- validate-config
dotnet run --project Server/NaijaKart.Server -- simulate --players 8
```

## 4. Preview the world as a video

```bash
cd tools/world-preview && npm install && npm run setup && cd ../..
dotnet run --project Server/NaijaKart.Server -- export-world --track third_mainland_rush --out tools/world-preview/public/world.json
dotnet run --project Server/NaijaKart.Server -- simulate --players 8 --seed 16 --replay tools/world-preview/public/replay.json
cd tools/world-preview
node still.js --t=14,33 --mode=chase --out=frame.png        # single frames for look development
node render.js --fps=24 --start=-3 --duration=55 --overview=5 --out=race.mp4
```

Playwright downloads its own Chromium on first install. With a GPU the render is many times
faster than in the cloud container. Add `--world=world_night.json` after
`export-world --theme night` for the night version.

## 5. Run the server

```bash
dotnet run --project Server/NaijaKart.Server -- run --port 7777 --data data/naijakart.json
```

One-time codes for "save progress" are printed to the console in development.

## 6. Claude Code locally (with Blender)

1. Install Claude Code on this machine and open the repository folder.
2. The repository ships `.mcp.json`, which registers the Blender MCP server (`uvx blender-mcp`,
   the community server that talks to the Blender addon on port 9876). Claude Code asks you to
   approve project MCP servers on first use.
3. In Blender, install the `blender-mcp` addon and press **Connect to Claude** in its sidebar.
4. Start Claude Code in the folder; the `mcp__blender__*` tools (scene info, object info, run
   Blender Python, viewport screenshot) will be available.

`uvx` comes with [uv](https://docs.astral.sh/uv/); install it if `uvx` is missing.
