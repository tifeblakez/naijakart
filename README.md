# NAIJA KART

**Race. Survive. Compete. Run Am Back.**

Naija Kart is a stylised 3D Nigerian multiplayer arcade racer for iOS and Android (landscape, tilt
steering, up to 8 players, 3–5 minute races). This repository holds the Unity client, the shared
gameplay core, the authoritative race server and the content data.

The product contract is the PRD (see `docs/PRD.md`). This README is the engineering entry point.

## Repository layout

```
Assets/NaijaKart/Runtime/Core     Engine-agnostic gameplay core (no UnityEngine). Compiled by Unity AND by dotnet.
Assets/NaijaKart/Runtime/Unity    Unity client: input (tilt), networking, prediction, views, camera, UI, audio.
Assets/NaijaKart/Editor           Editor tooling: scene scaffolding, greybox builder, content assets → JSON.
Assets/NaijaKart/Tests/EditMode   NUnit tests for the core (run in Unity Test Runner and via dotnet).
Assets/StreamingAssets/NaijaKart/Config   THE balance + content data (JSON). Shared by client and server.
Build/                            dotnet projects that compile the Unity source folders out-of-tree.
Server/NaijaKart.Server           Authoritative race server (.NET 8): rooms, matchmaking, LASTMA, economy, settlement.
Server/NaijaKart.Server.Tests     End-to-end server tests over a loopback transport (latency, loss, disconnects).
docs/                             Architecture, ADRs, roadmap, vertical-slice status, coding standards.
tools/                            Content generators (e.g. Third Mainland Rush greybox layout).
```

## Quick start (no Unity needed)

```bash
dotnet test NaijaKart.sln                                   # core + server test suites
dotnet run --project Server/NaijaKart.Server -- validate-config
dotnet run --project Server/NaijaKart.Server -- simulate --players 8    # headless 8-bot race on Third Mainland Rush
dotnet run --project Server/NaijaKart.Server -- run --port 7777         # start the authoritative server
```

## Quick start (Unity)

1. Open the project with Unity 6000.0 LTS (see `ProjectSettings/ProjectVersion.txt`). Packages restore automatically.
2. Menu **Naija Kart ▸ Scaffold Scenes** creates Bootstrap, Home, Lobby and Race scenes and adds them to Build Settings.
3. Menu **Naija Kart ▸ Greybox ▸ Build Third Mainland Rush in Current Scene** builds the drivable greybox.
4. Press Play in `Bootstrap`. **Practice** runs an in-process authoritative simulation with bots (same wire protocol as online);
   **Quick Race / Who Get Mouth? / Room** connect to a running `NaijaKart.Server` (host/port in settings).
5. Run **Window ▸ General ▸ Test Runner ▸ EditMode** for the core tests inside Unity.

## Principles that are enforced in code

* **Server authority.** Clients send `PlayerInputFrame` and intents only. Laps, positions, items, LASTMA, Coins, XP, rank and results exist only on the server (`RaceSimulation`, `CoinLedger`, `RaceSettlementService`).
* **Data-driven.** Every balance value lives in `game-config.json`; items, vehicles, characters, tracks and rank tiers are JSON rows. `ConfigValidator` fails fast on bad data.
* **Deterministic core.** `RaceSimulation` is a pure function of (seed, inputs). The same code predicts on the client.
* **Tilt is primary.** `TiltSteeringMapper` + `TiltInputProvider`; buttons are an accessibility fallback.
* **No fake multiplayer.** Bots are always flagged (`RaceParticipant.IsBot`) and only fill grids when a host asks.
* **No pay-to-win, no real-money betting.** The economy has one ledger with idempotent transactions and no purchase path in this build.

See `docs/ARCHITECTURE.md` for the full picture and `docs/VERTICAL_SLICE.md` for what is done and what is next.
