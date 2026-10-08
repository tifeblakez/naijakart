# Vertical slice definition of done (PRD §84) — status

| # | Requirement | Status | Where |
|---|---|---|---|
| 1 | Player launches game | Scaffolded (Bootstrap scene, config load) | `GameBootstrap` |
| 2 | Player enters race | Done (queue / room / practice) | `GameServer`, `HomePresenter` |
| 3 | Other players join | Done (rooms, matchmaking, bots on request) | `RaceRoom`, tests |
| 4 | Countdown occurs | Done + tested | `RaceSimulation.StepCountdown` |
| 5 | Player drives using tilt | Done (mapper tested; device feel pending) | `TiltSteeringMapper`, `TiltInputProvider` |
| 6 | Player can drift | Done + tested (blue/orange/purple) | `ArcadeVehicleModel` |
| 7 | Player can boost | Done + tested (drift release, items) | `ArcadeVehicleModel.ApplyBoost` |
| 8 | Player receives items | Done + tested (position-weighted roll) | `ItemRoller`, `ItemInventory` |
| 9 | Player can use items | Done + tested (10 items) | `ItemEffectSystem` |
| 10 | Traffic affects racing | Done (traffic cars, crossings, stun + shove) | `HazardSystem`, `RoadEventScheduler` |
| 11 | Nigerian road events occur | Done (7 kinds, telegraphed) | `RoadEventScheduler` |
| 12 | LASTMA can pursue any racer | Done + tested (position weights) | `LastmaSystem.PickTarget` |
| 13 | Player can escape LASTMA | Done + tested (pressure model, shortcut relief, shield) | `LastmaSystem` |
| 14 | Player can be fined | Done + tested (ledger debit) | `LastmaSystem.PayFine` |
| 15 | Player can request bail | Done + tested | `LastmaSystem.RequestBail` |
| 16 | Another player can bail them | Done + tested over the wire | `GameServer.BroadcastBailRequest`, `PayBail` |
| 17 | Player can be eliminated | Done + tested (arrest → spectate) | `LastmaSystem`, `RaceSimulation.Eliminate` |
| 18 | Race completes correctly | Done + tested | `RaceSimulation.CheckFinishConditions` |
| 19 | Finish order is authoritative | Done + tested (no client path) | `RaceSimulation`, `ClientCannotDeclareItselfFinished` test |
| 20 | XP and Coins are awarded | Done + tested (idempotent) | `RaceSettlementService` |
| 21 | Rank changes correctly | Done + tested (rating → tier from data) | `RatingCalculator`, `RankLadder` |
| 22 | Results screen appears | Presenter done; layout/prefab pending | `ResultsPresenter` |
| 23 | Player can rematch | Done + tested ("RUN AM BACK") | `RaceRoom.Tick` |
| 24 | Race can be repeated | Done + tested | `RematchCreatesANewRaceInTheSameRoom` |
| 25 | Disconnects do not corrupt results | Done + tested (DNF after window; reconnect keeps place) | server tests |
| 26 | No obvious client-side exploit | Structural (inputs only) + rate limit + lap validation | `AntiCheat`, tests |

**What "done" does not mean yet:** production-quality visuals (PRD §48–§51), tuned game feel on
device (Gate 1), and a deployed server with a UDP transport (Gate 5). Those are the next milestones in
`ROADMAP.md`.
