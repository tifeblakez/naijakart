using System;

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

        public static string KeyFor(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;

        public static Rivalry Create(string a, string b)
        {
            bool aFirst = string.CompareOrdinal(a, b) <= 0;
            return new Rivalry { PlayerA = aFirst ? a : b, PlayerB = aFirst ? b : a };
        }

        /// <summary>Records a race in which both players took part. Position 0 means did not finish.</summary>
        public void Record(string winnerId, float lapA, float lapB)
        {
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

        public int WinsFor(string playerId) => playerId == PlayerA ? WinsA : playerId == PlayerB ? WinsB : 0;
        public int StreakFor(string playerId) => playerId == PlayerA ? System.Math.Max(0, Streak) : playerId == PlayerB ? System.Math.Max(0, -Streak) : 0;
    }
}
