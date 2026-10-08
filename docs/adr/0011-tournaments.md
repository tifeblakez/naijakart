# ADR-0011: Tournaments as content-defined round robins

## Status
Accepted (2026-10-08)

## Context
Design screen 17 shows the Lagos Championship: 32 racers, rounds ("Round 1 · Advanced", "Round 3 ·
top 8 advance · Race 3 of 3 next", Semi, Final), points per racer, cosmetic rewards (crown banner,
finalist badge, coins every race) and a countdown to the next race. PRD §8 asks for tournaments;
PRD §37 forbids anything but looks as rewards.

## Decision
* `tournaments.json` defines each tournament: entrants cap, minimum real racers, AI fill, laps,
  tracks, rounds (races per round, how many advance, optional UTC start), points by position,
  coins per race, champion and finalist cosmetics, and the gap between races.
* `Core/Tournaments/TournamentEngine` is pure state: entry while not started, grids seeded by
  standings (eight per room), points by position among entrants only (AI fill never scores),
  advancement after each round, champion = top of the standings after the last round. The
  server (`GameServer.TickTournaments`) creates `RaceMode.Tournament` rooms when a race is due and
  enough entrants are online, seats them, fills with AI, and settles points, coins and cosmetics
  when the race results arrive. Entrants who are offline for a race score nothing for it.
* Tournament races settle like ranked races for XP, coins and rating; rewards are cosmetics and
  coins only; `TournamentWins` feeds the Chairman achievement.
* State is in memory for now; persistence will mirror the other stores (ADR-0004).

## Consequences
* A tournament runs itself from the clock; live ops only edit JSON.
* Tests drive the server clock from simulated time (`ServerHarness`), which also makes seasons
  and Rush Hour deterministic.
