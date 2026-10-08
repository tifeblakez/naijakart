# ADR-0005: Matchmaking first pass

**Status:** accepted (first pass)

Quick Race: a single queue per mode; a room starts when 8 are queued, or after
`raceRules.matchmakingWaitSeconds` with at least `minPlayersToStart`. Ranked: same, but the group is
sorted by rating before seating so the first eight are closest in skill. Track is random among loaded
tracks. No bots are ever added to public races (PRD §87 "no fake multiplayer"); private-room hosts
may explicitly fill with bots.

Next: rating-window widening over time, region buckets, and tournament brackets as a separate service.
