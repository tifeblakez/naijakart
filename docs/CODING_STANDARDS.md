# Coding standards

* **Core is pure.** Nothing under `Runtime/Core` may reference UnityEngine, System.Text.Json, Newtonsoft, threads or wall-clock time. Randomness comes from `DeterministicRandom` seeded per race.
* **C# 9 / netstandard2.1** in Core and Unity assemblies (Unity 6 compatibility). No records, no init-only setters, no file-scoped namespaces there. Server and tests may use C# 12 features sparingly.
* **No magic numbers.** Any tunable belongs in `GameConfig` or a content definition. If you type a float literal in gameplay code, stop and add a config field.
* **Server authority.** If a change would let a client influence laps, positions, items, Coins, XP, rank or results, it is wrong by construction.
* **Single responsibility.** Subsystems (`ItemEffectSystem`, `LastmaSystem`, `RoadEventScheduler`, `HazardSystem`) talk to the simulation only through `IRaceContext`.
* **Events over coupling.** Gameplay emits `RaceEvent`; presentation/audio/analytics subscribe. Presenters never compute race state.
* **Naming.** PascalCase types/members, `_camelCase` private fields, camelCase JSON fields (public fields on config POCOs for JsonUtility/STJ/Newtonsoft compatibility).
* **Tests first for logic.** Every Core subsystem has NUnit tests in `Assets/NaijaKart/Tests/EditMode` (run by both Unity and `dotnet test`). Server behaviour is tested end-to-end over the loopback transport.
* **Nigerian copy** goes through `NaijaCopy`. Keep it understandable across Nigeria, not Lagos-only.
* **Bots are honest.** `IsBot` is always set; never present a bot as a human.
* **Commit hygiene.** Small, scoped commits; regenerate JSON with the tools rather than hand-editing generated files (`tools/gen_third_mainland_rush.py`, `export-defaults`).
