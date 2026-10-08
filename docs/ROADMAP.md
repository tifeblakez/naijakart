# Roadmap and phase status

Phases follow the PRD (§85). Gates (§106) are questions answered by playtesting, not by code.

| Phase | Scope | Status |
|---|---|---|
| 0 Foundation | Unity project, folder structure, asmdefs, config system, logging, CI, coding standards | **Done** (this repo) |
| 1 Driving prototype | arcade vehicle model, tilt input, drift levels, boost, camera, collision, recovery | **Core done + tested**; Unity feel tuning pending (Gate 1 needs a device build) |
| 2 Track prototype | Third Mainland Rush greybox, checkpoints, laps, shortcut, traffic, hazards, item zones | **Done as data + greybox mesh builder**; art pass pending |
| 3 Race system | state machine, countdown, positions, checkpoints, laps, finish, results | **Done + tested** |
| 4 Items | boxes, inventory, use, 10 effects, position-weighted balance, VFX/audio hooks | **Done + tested**; VFX/audio assets pending |
| 5 LASTMA | targeting, warning, pursuit, escape, fine, Coins, bail, arrest, spectate | **Done + tested** end to end (incl. over the wire) |
| 6 Multiplayer | authoritative server, rooms, matchmaking, prediction/reconciliation, disconnect/reconnect, 8 players | **Done over loopback + TCP transport**; UDP transport and hosted deployment pending (ADR-0003) |
| 7 Progression | XP, Coins, level, rank tiers, rating, rewards, settlement, achievements, daily/weekly challenges | **Done + tested** (17 challenge/achievement rules shipped as data) |
| 8 Social | rivalries, rematch, private rooms, invites, bail from friends, friends list, leaderboards, sharing | **Done except share deep links** (friends are a symmetric first pass, ADR-0006) |
| 9 Full content | 8 characters, 10 vehicles, 6 tracks, item library, customisation, tournaments, seasons | Data for 8 characters / 10 vehicles / 10 items shipped; 5 tracks, cosmetics, tournaments, seasons pending |
| 10 Polish | VFX, audio, animation, UI, camera, performance, onboarding | Pending |
| 11 QA | network matrix, devices, exploits, crashes | Automated matrix exists in tests; device QA pending |

## UI-driven changes (see ADR-0007 and UI_MAPPING.md)

The Garage, control-choice, PULL OVER and Race HUD designs are implemented behind the server
(options message, garage/purchases, four item slots, banked boost charges, penalty-by-default LASTMA,
shortcut heat, touch drag steering) and as Unity presenters. Remaining for those screens: prefabs/
layout in the editor and the 3D art the HUD mock shows.

## Immediate next steps (in order)

1. Open in Unity 6, scaffold scenes, build greybox, drive with tilt on a device. **Gate 1: is driving fun?** Tune `driving`/`drift` in `game-config.json` only.
2. Deploy `NaijaKart.Server` (any Linux box; `dotnet run -- run --port 7777`) and race 8 phones through it. **Gate 5.**
3. Replace JSON/TCP with a UDP binary transport behind `ITransport` once Gate 5 passes with real latency numbers.
4. Replace the JSON-file persistence (`--data state.json`) with a managed database adapter behind the same interfaces; guest → account linking.
5. Production art for Third Mainland Rush, one vehicle (Danfo) and one character (Tunde) — the vertical slice visual target.
