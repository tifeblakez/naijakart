# Claude Code guidance for this repository

Read `docs/PRD.md` (product contract), `docs/ARCHITECTURE.md`, `docs/CODING_STANDARDS.md` and
`docs/ROADMAP.md` before changing anything.

## Non-negotiables (PRD §87, §108)
* Server authority for all competitive state. Never add a client→server message that carries a position, lap, item result or outcome.
* Tilt is the primary control. Buttons are an accessibility fallback only.
* No hard-coded balance values: add a field to `GameConfig` / content JSON instead.
* No fake multiplayer; bots are always flagged.
* No pay-to-win; no real-money betting/cash-out.
* Keep `Runtime/Core` free of UnityEngine.

## Build & test
```
dotnet test NaijaKart.sln
dotnet run --project Server/NaijaKart.Server -- validate-config
dotnet run --project Server/NaijaKart.Server -- simulate --players 8
```
Regenerate data rather than hand-editing: `python3 -I tools/gen_third_mainland_rush.py`,
`python3 -I tools/gen_design_tokens.py` (design tokens → `DesignTokens.cs`),
`dotnet run --project Server/NaijaKart.Server -- export-defaults --config Assets/StreamingAssets/NaijaKart/Config`.

## Where things go
* Gameplay rule → `Assets/NaijaKart/Runtime/Core/<System>/` + test in `Assets/NaijaKart/Tests/EditMode/`.
* Server behaviour → `Server/NaijaKart.Server/Hosting/` + end-to-end test in `Server/NaijaKart.Server.Tests/`.
* Presentation → `Assets/NaijaKart/Runtime/Unity/<Area>/`. Presenters subscribe to `RaceEvent`s; they never compute race state.
* Copy → `NaijaCopy`.

## When unsure
Prefer the existing architecture, prefer configuration, preserve fairness and mobile performance,
and record material decisions as an ADR in `docs/adr/`.
