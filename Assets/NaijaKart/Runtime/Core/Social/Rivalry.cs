using System;
using System.Collections.Generic;

namespace NaijaKart.Core.Social
{
    /// <summary>Persistent head-to-head record between two players (PRD §9, §56). Keyed by sorted pair.</summary>
    [Serializable]
    public sealed class Rivalry
    {
        public string PlayerA;
        public string PlayerB;
        public int WinsA;
        public int WinsB;
        public int TotalRaces;
        public float FastestLapA = float.MaxValue;
        public float FastestLapB = float.MaxValue;
        public string LastWinner;
        /// <summary>Positive = A's streak, negative = B's streak.</summary>
        public int Streak;
        /// <summary>Winner ids of the most recent races, newest last ("Last 5").</summary>
        public List<string> RecentWinners = new List<string>();
        /// <summary>Bails paid by A for B and by B for A ("He bailed you 4 times").</summary>
        public int BailsAtoB;
        public int BailsBtoA;
        /// <summary>Best race time per track for each side ("Third Mainland · You 3:18.40 · Him 3:18.02").</summary>
        public Dictionary<string, float> BestTimeA = new Dictionary<string, float>();
        public Dictionary<string, float> BestTimeB = new Dictionary<string, float>();
        public long SinceUnixMs;

        public static string KeyFor(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;

        public static Rivalry Create(string a, string b)
        {
            bool aFirst = string.CompareOrdinal(a, b) <= 0;
            return new Rivalry { PlayerA = aFirst ? a : b, PlayerB = aFirst ? b : a };
        }

        /// <summary>Records a race in which both players took part. Position 0 means did not finish.</summary>
        public void Record(string winnerId, float lapA, float lapB) => Record(winnerId, lapA, lapB, null, 0f, 0f, 0, 5);

        public void Record(string winnerId, float lapA, float lapB, string trackId, float timeA, float timeB, long nowUnixMs, int recentKept)
        {
            if (SinceUnixMs == 0 && nowUnixMs > 0) SinceUnixMs = nowUnixMs;
            if (trackId != null)
            {
                if (timeA > 0f && (!BestTimeA.TryGetValue(trackId, out float ba) || timeA < ba)) BestTimeA[trackId] = timeA;
                if (timeB > 0f && (!BestTimeB.TryGetValue(trackId, out float bb) || timeB < bb)) BestTimeB[trackId] = timeB;
            }
            RecentWinners.Add(winnerId);
            while (RecentWinners.Count > System.Math.Max(1, recentKept)) RecentWinners.RemoveAt(0);
            TotalRaces++;
            if (lapA > 0f && lapA < FastestLapA) FastestLapA = lapA;
            if (lapB > 0f && lapB < FastestLapB) FastestLapB = lapB;
            if (winnerId == PlayerA)
            {
                WinsA++;
                Streak = Streak > 0 ? Streak + 1 : 1;
            }
            else if (winnerId == PlayerB)
            {
                WinsB++;
                Streak = Streak < 0 ? Streak - 1 : -1;
            }
            LastWinner = winnerId;
        }

        public void RecordBail(string payerId)
        {
            if (payerId == PlayerA) BailsAtoB++; else if (payerId == PlayerB) BailsBtoA++;
        }

        public int BailsGivenBy(string playerId) => playerId == PlayerA ? BailsAtoB : playerId == PlayerB ? BailsBtoA : 0;
        public Dictionary<string, float> BestTimesFor(string playerId) => playerId == PlayerA ? BestTimeA : BestTimeB;
        public int WinsFor(string playerId) => playerId == PlayerA ? WinsA : playerId == PlayerB ? WinsB : 0;
        public int StreakFor(string playerId) => playerId == PlayerA ? System.Math.Max(0, Streak) : playerId == PlayerB ? System.Math.Max(0, -Streak) : 0;
    }
}
