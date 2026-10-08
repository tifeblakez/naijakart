# ADR-0010: Ranked Points, divisions, Rush Hour and seasons

## Status
Accepted (2026-10-08)

## Context
The Ranked hub, Results and Leaderboards screens (03.1, 10, 15) show a points ladder: seven tiers
(JJC, Sharp Sharp, No Dull, Sabi, Odogwu, Chairman, Oga Patapata), each with divisions III → I of
100 RP, "Win: about +24 RP, last place: about -12 RP", Rush Hour (+25% RP, 7pm–10pm), at least
four real racers per ranked grid, AI never changing RP, seasons with "everyone drops one tier at
reset", and a "Last 5 races" list. PRD §7 named ten rating-threshold ranks and §40 an Elo rating.

## Decision
* Two numbers per player: a hidden Elo `Rating` kept for matchmaking quality (unchanged
  `RatingCalculator`), and visible `RankedPoints` (RP) that drive tier, division and leaderboards.
  Players only ever see RP.
* `GameConfig.ranked` holds the RP table by position, divisions per tier, RP per division, Rush
  Hour window and multiplier (Lagos time, UTC+1), the minimum real racers, the season id, start
  and length, the reset drop and how many recent results are kept. `progression.ranks` carries
  the seven design tiers as RP floors (300 apart). The PRD's ten names are superseded; the design
  system's names are pending validation with Nigerian players, as its README notes.
* RP per race is the table entry for the finishing position among real racers, spread across the
  table when fewer than eight real racers finish, scaled by the Rush Hour multiplier for gains
  only, and floored at 0 overall. AI racers fill grids but never move anyone's RP.
* Seasons roll over on the clock; the reset is applied lazily when a profile is next seen in a
  new season (drop `resetDropTiers` tiers, keep division progress, clear the recent list). "Top
  100 become Oga Patapata" stays a leaderboard title at season end (not yet implemented).
* `GetSeason` returns days left, Rush Hour state and the ranked rules so the hub shows live
  numbers rather than copy.

## Consequences
* Results and profiles carry RP deltas, divisions and labels ("Chairman II"); settlement stays
  idempotent per race id.
* Existing tests that raced as guests opt out of the account gate and the four-real-racer minimum
  explicitly; production keeps both.
