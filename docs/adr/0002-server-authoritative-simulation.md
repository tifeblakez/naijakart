# ADR-0002: One authoritative RaceSimulation, clients send inputs only

**Status:** accepted

## Decision
`RaceSimulation` is the only place where race state changes. It is deterministic for a given seed and
input stream and is ticked at a fixed rate by the server. The wire protocol has no message type that
carries a position, lap, item outcome or result from a client. Clients predict their own kart with the
same model and reconcile against `ParticipantSnapshot.FullState`.

## Alternatives considered
* Client-authoritative movement with server validation (common in casual racers): rejected, PRD §87 forbids client-side competitive logic and §91 requires the server to determine results.
* Lockstep: rejected for 8 mobile players on variable networks.

## Consequences
* Cheating must attack inputs, which are rate-limited and clamped; impossible laps are rejected.
* Item/hazard effects are not predicted; a snapshot corrects them within ~70 ms. Acceptable because those effects are rare and dramatic.
