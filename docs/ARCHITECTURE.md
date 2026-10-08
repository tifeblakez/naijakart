# Architecture

## 1. Three layers, one gameplay core

```
┌───────────────────────────────┐      ┌───────────────────────────────┐
│ Unity client (NaijaKart.Unity) │      │ Server (NaijaKart.Server, .NET 8) │
│ input · prediction · views ·   │ ──── │ rooms · matchmaking · settlement  │
│ camera · UI · audio            │ wire │ ledger · profiles · rivalries     │
└───────────────┬───────────────┘      └───────────────┬───────────────┘
                │ references                            │ references
        ┌───────┴────────────────────────────────────────┴───────┐
        │ NaijaKart.Core (netstandard2.1, no UnityEngine)        │
        │ Config · Vehicle · Track · Race · Items · Chaos ·      │
        │ Lastma · Economy · Progression · Social · Net · AntiCheat │
        └──────────────────────────────────────────────────────┘
```

`NaijaKart.Core` is compiled twice: by Unity (`NaijaKart.Core.asmdef`, `noEngineReferences`) and by
`Build/NaijaKart.Core/NaijaKart.Core.csproj`. That is what lets the server, the client's prediction
and CI tests share one implementation of driving, checkpoints, items and LASTMA.

## 2. Authority model (PRD §65, §91)

| Owned by server (RaceSimulation) | Owned by client |
|---|---|
| membership, grid, countdown, race state machine | local input sampling (tilt → steer) |
| vehicle state for every kart | prediction of the local kart + reconciliation |
| checkpoints, laps, lap validation, positions, finish order | interpolation of remote karts |
| item boxes, item roll, item effects, hazards, road events | VFX, audio, camera, HUD, dialogs |
| LASTMA targeting, pursuit, fine, bail, arrest, elimination | share cards, settings |
| Coins (ledger), XP, level, rating, rank, rivalries, results | — |

The client's only driving message is `PlayerInputFrame { Sequence, Steer, Drift, UseItem, … }`.
Intents (`PayFine`, `RequestBail`, `PayBail`, `VoteRematch`, room/queue operations) are validated
server-side. There is no message that carries a position, lap or result from client to server.

## 3. Simulation tick

`RaceSimulation.Step()` runs at `simulation.tickRate` (30 Hz) with a fixed `dt`:

1. per active kart: sample surface → `ArcadeVehicleModel.Step` → recovery check
2. kart-vs-kart collision resolution (weight-aware)
3. hazard contacts (zones apply continuous modifiers; statics/crossings apply once with immunity)
4. item boxes → `ItemRoller` (position-weighted) → `ItemInventory`
5. item use (rising edge) → `ItemEffectSystem`
6. `CheckpointTracker.Update` → gate / lap / finish / rejected-lap events
7. hazards tick + expiry, box respawns, inventories
8. `RoadEventScheduler.Tick`, `LastmaSystem.Tick`
9. live positions (+ overtake telemetry), disconnect timeouts, finish conditions

Everything observable is emitted as `RaceEvent`s (HUD, audio, analytics, Wahala highlights) and
periodically as a `RaceSnapshot` (`simulation.snapshotRate`, 15 Hz).

## 4. Networking (ADR-0003)

`ITransport` abstracts the wire. Implementations:

* `LoopbackTransportHub` — in-process, with simulated latency/jitter/loss/disconnect (tests, practice mode).
* `TcpJsonServerTransport` / `TcpJsonClientTransport` — length-prefixed JSON over TCP. First real transport; debuggable, works from Unity. A UDP transport with delta-compressed binary snapshots is the planned replacement for production latency targets.

Messages are flat envelopes (`ClientEnvelope`, `ServerEnvelope`) so a binary codec can replace JSON without changing handlers.

Client prediction: `ClientPredictor` runs the same `ArcadeVehicleModel`, keeps unacknowledged inputs and replays them on top of each authoritative `ParticipantSnapshot.FullState` (`LastInputSequence` acks). Hazard/item effects are not predicted; the next snapshot corrects them, and the view smooths small corrections and snaps large ones (recovery).

## 5. Server hosting

`GameServer` is single-threaded: `Tick()` pumps the transport, runs matchmaking, ticks every `RaceRoom`.
A `RaceRoom` owns one `RaceSimulation` at a time and survives rematches ("RUN AM BACK" creates a new
race in the same room with the voters). Settlement (`RaceSettlementService`) runs once per race id:
XP, Coins (ledger credit with idempotency key `raceId:reward:playerId`), rating (multiplayer Elo for
ranked/tournament only), win streaks, rivalries.

After settlement, `ChallengeEvaluator` applies the race to every daily/weekly/achievement rule in
`challenges.json` and pays rewards through the ledger (ADR-0006). Results are re-sent every
`simulation.resultsResendSeconds` while a room shows them, so a lossy transport cannot lose them.

Persistence is behind `ICoinStore`, `IProfileStore`, `IRivalryStore`, `IChallengeProgressStore`.
In-memory implementations are the default; `--data state.json` switches to `JsonFileStateStore`
(atomic, debounced writes) so balances, the transaction log, profiles, rivalries and challenge
progress survive restarts. A database adapter is the next step (ADR-0004).

Social and live-ops endpoints: `AddFriend`/`RemoveFriend`, `GetProfile`, `GetRivalries`,
`GetLeaderboard` (rating/wins/streak/lastma/level, optional friends filter) and the Wahala Calendar
(`liveEvents.weekdayRules`) applied to public rooms via `RaceRoom.ApplyLiveEvent`.

## 6. Data

`Assets/StreamingAssets/NaijaKart/Config/`:

* `game-config.json` — `GameConfig`: simulation, driving, drift, boost, tilt, race rules, collision, items, road events, LASTMA, economy, progression (ranks), anti-cheat. Regenerate defaults with `naijakart-server export-defaults`.
* `items.json`, `vehicles.json`, `characters.json`, `challenges.json` — content rows.
* `tracks/*.json` — `TrackDefinition`: closed centreline, road width, shortcut roads, ordered checkpoints with alternative gates, item boxes, road-event anchors.

`ConfigValidator` runs at server boot, at client boot and in tests.

## 7. Anti-cheat posture

Structural: clients cannot assert outcomes. Residual checks: input rate limiting and ordering
(`InputRateLimiter`), impossible-lap rejection (`LapValidator`, `CheckpointTracker`), hazard budget,
transaction idempotency, and `MovementValidator` for any future soft-trust path.

## 8. Unity client structure

* `GameBootstrap` → loads/validates config, sets landscape/60 fps, loads Home.
* `NetworkClient` (persistent) → transport, Hello/Welcome with retry, reconnect with the same identity, event fan-out.
* `LocalPracticeHost` + `PracticeServer` → offline practice over loopback using the real protocol.
* `RaceClientController` → fixed-rate input + prediction, remote interpolation, view spawning.
* `TrackGreyboxBuilder` → drivable mesh from `TrackDefinition` (road, kerbs, shortcut roads, gizmos).
* Presenters: `RaceHudPresenter`, `LastmaPromptPresenter`, `LobbyPresenter`, `ResultsPresenter`, `HomePresenter`; `RaceAudioPresenter`; `ChaseCamera`.
* Copy lives in `NaijaCopy` (the game speaks Nigerian).
