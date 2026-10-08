using System;
using System.Collections.Generic;
using NaijaKart.Core.Simulation;

namespace NaijaKart.Core.Tournaments
{
    /// <summary>
    /// Tournaments (PRD §8, design screen 17): up to 32 entrants race in rounds of N races; points by
    /// finishing position; the top X advance each round; the final decides the champion. Rewards are
    /// looks only. Definitions are content; the engine below is pure state logic the server drives.
    /// </summary>
    [Serializable]
    public sealed class TournamentRoundDefinition
    {
        public string name = "Round 1";
        public int races = 3;
        /// <summary>How many advance after this round (0 = everyone / the final).</summary>
        public int advance;
        /// <summary>ISO UTC start; null = starts as soon as the previous round is done.</summary>
        public string startsUtc;
    }

    [Serializable]
    public sealed class TournamentDefinition
    {
        public string id;
        public string name;
        public int maxEntrants = 32;
        /// <summary>Real racers needed before the first race can start; AI fills grids.</summary>
        public int minRealPlayers = 2;
        public bool fillWithAi = true;
        public int laps = 2;
        public string[] trackIds = Array.Empty<string>();
        public TournamentRoundDefinition[] rounds = Array.Empty<TournamentRoundDefinition>();
        /// <summary>Points by finishing position in each race (1st first).</summary>
        public int[] pointsByPosition = { 10, 8, 6, 5, 4, 3, 2, 1 };
        /// <summary>Coins everyone gets per tournament race (design: "Everyone · Coins every race").</summary>
        public long coinsPerRace = 200;
        public string championCosmeticId;
        public string finalistCosmeticId;
        public int finalistsCount = 8;
        /// <summary>Seconds between a race finishing and the next one ("Next · Lekki Toll Dash 04:32").</summary>
        public int secondsBetweenRaces = 60;
    }

    [Serializable]
    public sealed class TournamentLibrary
    {
        public TournamentDefinition[] tournaments = Array.Empty<TournamentDefinition>();
    }

    [Serializable]
    public sealed class TournamentEntrant
    {
        public string PlayerId;
        public int Points;
        public bool Alive = true;
        public List<int> Positions = new List<int>();
    }

    /// <summary>Runtime state of one tournament instance.</summary>
    [Serializable]
    public sealed class TournamentState
    {
        public string Id;
        public List<TournamentEntrant> Entrants = new List<TournamentEntrant>();
        public int RoundIndex;
        public int RaceIndex;
        public bool Started;
        public bool Finished;
        public string ChampionId;
        /// <summary>Race ids in flight for the current race slot.</summary>
        public List<string> RacesInProgress = new List<string>();
        /// <summary>Earliest UTC time (unix ms) the next race may start.</summary>
        public long NextRaceAtUnixMs;

        public TournamentEntrant Find(string playerId)
        {
            foreach (var e in Entrants) if (e.PlayerId == playerId) return e;
            return null;
        }

        public List<TournamentEntrant> Alive()
        {
            var list = new List<TournamentEntrant>();
            foreach (var e in Entrants) if (e.Alive) list.Add(e);
            return list;
        }

        /// <summary>Points first, then best race position, then id for a stable order.</summary>
        public List<TournamentEntrant> Standings()
        {
            var list = new List<TournamentEntrant>(Entrants);
            list.Sort((a, b) =>
            {
                int c = b.Points.CompareTo(a.Points); if (c != 0) return c;
                int ba = Best(a), bb = Best(b); c = ba.CompareTo(bb); if (c != 0) return c;
                return string.CompareOrdinal(a.PlayerId, b.PlayerId);
            });
            return list;
            static int Best(TournamentEntrant e) { int best = int.MaxValue; foreach (var p in e.Positions) if (p > 0 && p < best) best = p; return best; }
        }
    }

    /// <summary>Pure tournament rules: entry, race scheduling, points, advancement, the champion.</summary>
    public sealed class TournamentEngine
    {
        public TournamentDefinition Definition { get; }
        public TournamentState State { get; }

        public TournamentEngine(TournamentDefinition definition, TournamentState state = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            State = state ?? new TournamentState { Id = definition.id };
        }

        public TournamentRoundDefinition CurrentRound => State.RoundIndex < Definition.rounds.Length ? Definition.rounds[State.RoundIndex] : null;

        public bool Enter(string playerId)
        {
            if (State.Started || State.Finished) return false;
            if (State.Find(playerId) != null) return true;
            if (State.Entrants.Count >= Definition.maxEntrants) return false;
            State.Entrants.Add(new TournamentEntrant { PlayerId = playerId });
            return true;
        }

        public bool Leave(string playerId)
        {
            if (State.Started) return false;
            return State.Entrants.RemoveAll(e => e.PlayerId == playerId) > 0;
        }

        public static long ParseUtc(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return 0;
            return DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
                ? new DateTimeOffset(d).ToUnixTimeMilliseconds() : 0;
        }

        /// <summary>Unix ms when the next race may start, or -1 when nothing is pending.</summary>
        public long NextRaceAtUnixMs()
        {
            if (State.Finished || CurrentRound == null) return -1;
            long scheduled = ParseUtc(CurrentRound.startsUtc);
            return System.Math.Max(scheduled, State.NextRaceAtUnixMs);
        }

        public bool CanStartRace(long nowUnixMs, int realEntrantsOnline)
        {
            if (State.Finished || CurrentRound == null || State.RacesInProgress.Count > 0) return false;
            if (realEntrantsOnline < Definition.minRealPlayers) return false;
            if (State.Alive().Count < 1) return false;
            return nowUnixMs >= NextRaceAtUnixMs();
        }

        /// <summary>Splits alive entrants into grids of gridSize for the next race (seeded by standings).</summary>
        public List<List<string>> Grids(int gridSize, Func<string, bool> isOnline)
        {
            var grids = new List<List<string>>();
            var alive = State.Standings();
            var ids = new List<string>();
            foreach (var e in alive) if (e.Alive && isOnline(e.PlayerId)) ids.Add(e.PlayerId);
            for (int i = 0; i < ids.Count; i += System.Math.Max(1, gridSize)) grids.Add(ids.GetRange(i, System.Math.Min(gridSize, ids.Count - i)));
            if (grids.Count == 0) grids.Add(new List<string>());
            return grids;
        }

        public void RaceStarted(string raceId)
        {
            State.Started = true;
            State.RacesInProgress.Add(raceId);
        }

        public int PointsFor(int finishPosition, bool finished)
        {
            if (!finished || finishPosition <= 0) return 0;
            var table = Definition.pointsByPosition ?? Array.Empty<int>();
            if (table.Length == 0) return 0;
            return table[System.Math.Min(table.Length - 1, finishPosition - 1)];
        }

        /// <summary>Applies one race of the current slot. Returns true when the slot (all grids) is complete.</summary>
        public bool ApplyRaceResult(RaceResults results, long nowUnixMs, out bool roundAdvanced, out bool tournamentFinished)
        {
            roundAdvanced = false; tournamentFinished = false;
            if (results == null || !State.RacesInProgress.Remove(results.RaceId)) return false;
            // Position among tournament entrants only (AI fill never scores)
            var humans = new List<RaceResultEntry>();
            foreach (var e in results.Entries) if (!e.IsBot && State.Find(e.PlayerId) != null) humans.Add(e);
            humans.Sort((a, b) => a.FinishPosition.CompareTo(b.FinishPosition));
            for (int i = 0; i < humans.Count; i++)
            {
                var entrant = State.Find(humans[i].PlayerId);
                int pos = i + 1;
                entrant.Points += PointsFor(pos, humans[i].Finished);
                entrant.Positions.Add(humans[i].Finished ? pos : 0);
            }
            if (State.RacesInProgress.Count > 0) return false;
            // Slot done: entrants who were not seated this race score nothing.
            State.RaceIndex++;
            State.NextRaceAtUnixMs = nowUnixMs + Definition.secondsBetweenRaces * 1000L;
            if (State.RaceIndex >= System.Math.Max(1, CurrentRound.races))
            {
                roundAdvanced = true;
                int advance = CurrentRound.advance;
                if (advance > 0)
                {
                    int kept = 0;
                    foreach (var e in State.Standings()) { if (!e.Alive) continue; if (kept < advance) kept++; else e.Alive = false; }
                }
                State.RoundIndex++;
                State.RaceIndex = 0;
                if (State.RoundIndex >= Definition.rounds.Length)
                {
                    State.Finished = true;
                    tournamentFinished = true;
                    foreach (var e in State.Standings()) if (e.Alive) { State.ChampionId = e.PlayerId; break; }
                }
            }
            return true;
        }

        /// <summary>Standing label for the screen: Champion, Finalist, Out, or the current round.</summary>
        public string NoteFor(TournamentEntrant e)
        {
            if (State.Finished) return e.PlayerId == State.ChampionId ? "Champion" : e.Alive ? "Finalist" : "Out";
            return e.Alive ? "" : "Out";
        }
    }
}
