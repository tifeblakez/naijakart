# ADR-0006: Friends, leaderboards and challenges — first pass

**Status:** accepted (first pass)

* **Friends.** `AddFriend` creates a symmetric friendship immediately. This is acceptable while
  identities are device guests (ADR-0004). When accounts arrive, this becomes request/accept with
  the same `FriendIds` storage, and bail/leaderboard visibility rules stay unchanged.
* **Leaderboards.** Computed from `IProfileStore.All()` on request (rating, wins, streak, lastma,
  level) with an optional friends filter. Fine for thousands of profiles; a database adapter will
  sort and page server-side and add Nigeria/city scopes (PRD §60) once profiles carry a region.
* **Challenges and achievements.** Data rows in `challenges.json` evaluated by `ChallengeEvaluator`
  after settlement from `ParticipantStats`. Daily/weekly periods are ISO/UTC; rewards go through the
  ledger with idempotency keys `challenge:{player}:{rule}:{period}` so a replay never pays twice.
  Titles are granted by achievements; the first earned title becomes the profile title.
* **Wahala Calendar.** `liveEvents.weekdayRules` in `game-config.json` applies to public races
  only (items on/off, LASTMA on/off or frequency, item allow-list, drift boost, ranked XP multiplier,
  preferred track). Private rooms keep the host's settings. A live-ops feed can replace the config
  source without changing `RaceRoom.ApplyLiveEvent`.
