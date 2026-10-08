# Design files

`design/canvas/` is a local copy of the Claude design canvas **"Naija Kart Game Screens"**
(https://claude.ai/artifact/GY9EgorEEGE9GvJ9RuSWp7, version 1791499064-a1da, copied 2026-10-08):
34 artboards (`*.dc.html`, 844×390 pt phone frame), the canvas index (`canvas.json`), the shared
screen CSS (`screens.css`, `mobile.css`) and the design system (`ds/naijakart/`: `tokens.json`,
`tokens.css`, `components/bundle.css`, `README.md`).

It is the layout of record for every screen (see `docs/UI_MAPPING.md` for screen → system):

* `tools/world-preview/index.html` builds the Race HUD, countdown, LASTMA chase, caught and results
  overlays from `RaceHUD`, `Countdown`, `LastmaChase`, `Caught` and `Results` with the same tokens
  and classes.
* Unity presenters follow the same tokens (`ds/naijakart/tokens.json`) and copy.

The artboards reference three uploaded images by `/_blob/...` ids (key art behind menus, the danfo
photo, the racer portrait); those stay in the canvas and are not needed by the code.

## Refreshing the copy

When the canvas changes, read its files again (Artifact `read` with `scope: "files"` → the
`project/` paths) and replace this folder; keep this README. Do not hand-edit the copy: changes go
to the canvas first.
