# Working on Naija Kart locally (the hand-off from the cloud session)

From here on the project is worked on your own machine. Nothing lives only in the cloud session:
the code, the content, the generated world, the design files (`design/canvas/`) and the render
pipeline are all in this repository, and the cloud session is no longer needed. The one thing
the cloud could not do, talk to Blender, works locally through the project's `.mcp.json`.

The repository has a single branch, `claude/naija-kart-spec-05dbtf`; treat it as the mainline (or
rename it locally with `git branch -m main` and push that if you prefer).

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
| uv (`uvx`) | any | runs the Blender MCP server |

`scripts/setup-local.sh` (macOS/Linux) or `scripts/setup-local.ps1` (Windows) checks these,
runs the tests and prepares the previewer. Both are safe to run repeatedly.

## 3. Build, test, simulate

```bash
dotnet test NaijaKart.sln
dotnet run --project Server/NaijaKart.Server -- validate-config
dotnet run --project Server/NaijaKart.Server -- simulate --players 8
```

## 4. Render the race video

One command reproduces the showcase video (flyover, start, LASTMA chase, finish, results):

```bash
scripts/render-race-video.sh                 # day → out/renders/naija_kart_day.mp4
scripts/render-race-video.sh --theme night   # night version
SEED=7 FPS=30 scripts/render-race-video.sh   # another race / frame rate
```

Step by step, for look development:

```bash
cd tools/world-preview && npm install && node setup.js && cd ../..
dotnet run --project Server/NaijaKart.Server -- export-world --track third_mainland_rush --out tools/world-preview/public/world.json
dotnet run --project Server/NaijaKart.Server -- simulate --players 8 --seed 16 --replay tools/world-preview/public/replay.json
cd tools/world-preview
node still.js --t=14,33 --mode=chase --out=frame.png       # single frames
node render.js --fps=24 --start=-3 --duration=55 --overview=5 --out=race.mp4
```

Playwright downloads its own Chromium on first install and uses your GPU, so a frame takes a
fraction of a second rather than the two seconds it took in the cloud container. Set
`NK_SOFTWARE_GL=1` on a machine without a GPU, and `NK_CHROME=/path/to/chrome` to use another
Chromium build. `out/` is ignored by git; keep videos out of the repository.

## 5. Run the server

```bash
dotnet run --project Server/NaijaKart.Server -- run --port 7777 --data data/naijakart.json
```

One-time codes for "save progress" are printed to the console in development.

## 6. Claude Code locally, with Blender

1. Install Claude Code on this machine and open the repository folder.
2. `.mcp.json` registers the Blender MCP server (`uvx blender-mcp`, the community server that
   talks to the Blender addon on port 9876). Claude Code asks you to approve project MCP servers
   on first use.
3. In Blender, install the `blender-mcp` addon and press **Connect to Claude** in its sidebar.
4. Start Claude Code in the folder; the `mcp__blender__*` tools (scene info, object info, run
   Blender Python, viewport screenshot) are then available, and `CLAUDE.md` plus `docs/` give it
   the same context the cloud session had.
5. First Blender task: import the generated world with `tools/blender/import_world.py` (see
   `tools/blender/README.md`), either from the command line with `blender -b --python …` or by
   asking Claude Code to run `build(...)` from that file inside the open Blender. A cloud session
   cannot reach a Blender on your machine, so this step is local only.

## 7. Design files

`design/canvas/` is the local copy of the game screens and design system and is the source of
truth from now on; edit the `.dc.html` artboards and `ds/naijakart/tokens.json` here. After a
token change run `python3 -I tools/gen_design_tokens.py` to regenerate
`Assets/NaijaKart/Runtime/Unity/UI/DesignTokens.cs`; the previewer picks the CSS up directly.
