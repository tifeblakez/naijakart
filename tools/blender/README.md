# Blender import of the generated world

`import_world.py` brings the generated Third Mainland Rush world and a race replay into Blender:
one mesh per `MeshBatch` with a Principled material per hint (the previewer's roughness, metalness,
clearcoat, glass, water and tower-window logic as shader nodes), every `SignInstance` as a plane
with text objects, templates as a hidden library collection instanced for props, karts and
hazards, the sun and sky from the world's lighting fields, linear distance fog in the compositor,
and the previewer's chase camera (or the intro overview) at a chosen race time. The generator
stays the source of truth (ADR-0008): the script translates `world.json`; it never adds track
geometry of its own.

## Inputs

```bash
dotnet run --project Server/NaijaKart.Server -- export-world --track third_mainland_rush --out tools/world-preview/public/world.json
dotnet run --project Server/NaijaKart.Server -- export-world --track third_mainland_rush --theme night --out tools/world-preview/public/world_night.json
dotnet run --project Server/NaijaKart.Server -- simulate --players 8 --seed 16 --replay tools/world-preview/public/replay.json
```

## Running it

**Blender on your machine (recommended, GPU):**

```bash
blender -b --python tools/blender/import_world.py -- --t 14 \
    --save out/blender/third_mainland_rush.blend --render out/blender/chase_t14.png
```

Open `out/blender/third_mainland_rush.blend` in Blender to orbit the track. Collections:
`World meshes`, `Signs`, `Props`, `Race` (karts, hazards, the `Chase camera`) and
`Templates (library)` (excluded from the view layer; instances point at it).

**Inside a running Blender (Text Editor, or Claude Code through the Blender MCP server):**

```python
exec(open("tools/blender/import_world.py").read())
build(world="tools/world-preview/public/world.json", replay="tools/world-preview/public/replay.json", t=14)
```

**Headless without Blender installed** (the `bpy` wheel, CPU Cycles; this is how the reference
renders in this folder were made):

```bash
uv venv -p 3.11 .venv-bpy && uv pip install -p .venv-bpy/bin/python bpy
.venv-bpy/bin/python tools/blender/import_world.py --t 14 --samples 32 --render out/blender/chase_t14.png
```

## Design fonts for the signs

```bash
pip install fonttools brotli && python3 -I tools/blender/make_fonts.py   # writes out/fonts/*.ttf from the previewer's npm packages
```

## Options

| Option | Meaning |
| --- | --- |
| `--world`, `--replay`, `--no-replay` | inputs (defaults are the previewer's files) |
| `--t 14` | race time in seconds; karts, hazards and item boxes are placed as the replay has them |
| `--mode chase|hero|overview` | the previewer's chase camera, a low wide key-art framing, or the intro flyover |
| `--follow bot_6` | follow another kart than the replay's `followKartId` (useful to sit inside the pack) |
| `--motion-blur`, `--dof 5.6` | bake a short window around `--t` for Cycles motion blur; depth of field focused on the followed kart |
| `--anim START DURATION --fps 24` | also bake kart, hazard and camera keyframes for that window |
| `--engine CYCLES|BLENDER_EEVEE` | render engine (first available wins) |
| `--samples`, `--size 1280x592` | render settings |
| `--fonts DIR` | folder with `LilitaOne-Regular.ttf` and `Nunito-Black.ttf`; `out/fonts` (made by `make_fonts.py`) is used automatically, Blender's font otherwise |
| `--showcase kart_danfo --view front34` | render one template alone on a studio floor; views front34, rear34, front, back, left, right, top34 (the design-sheet angles) |
| `--save file.blend`, `--render file.png` | outputs (`out/` is git-ignored) |

Night: pass `--world tools/world-preview/public/world_night.json`.

## What to check in a render

Compare with a previewer still at the same time (`node still.js --t=14 --mode=chase`): same road
layout, bridge climb, pylon, districts, signs and kart positions. Differences in surface detail
are expected (the previewer's GLSL noise and the node trees are approximations of each other);
differences in layout mean one of the two readers is wrong and must be fixed, never the data.
