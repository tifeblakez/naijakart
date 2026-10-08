# ADR-0004: Identity and persistence are adapters, not features of the game loop

**Status:** accepted (interfaces); **pending** (production adapters)

## Decision
* Players identify with a `PlayerId` in `Hello`. Today this is a device-generated guest id (PRD §67 "guest account"); the server accepts it as-is. Production adds an `IAuthenticator` that validates `AuthToken` (OAuth/Play Games/Game Center) and maps to the same `PlayerId`. Reconnect takes over the previous session for the same id.
* Coins, profiles and rivalries are behind `ICoinStore`, `IProfileStore`, `IRivalryStore`. In-memory implementations ship for development; production uses a managed database with the same idempotency keys (`raceId:reward:playerId`, `lastma-event:fine`, `lastma-event:bail:payer`).

## Consequences
Nothing in `RaceRoom`/`RaceSimulation` changes when real auth and a database arrive. Until then,
treat the server as a development server: identities are not verified.
