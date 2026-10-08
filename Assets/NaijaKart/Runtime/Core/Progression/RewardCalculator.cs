using NaijaKart.Core.Config;
using NaijaKart.Core.Race;

namespace NaijaKart.Core.Progression
{
    public struct RaceRewards
    {
        public int Xp;
        public long Coins;
    }

    /// <summary>Converts a finishing position and race telemetry into XP and Coins from config tables only.</summary>
    public static class RewardCalculator
    {
        public static RaceRewards Compute(int finishPosition, bool finished, int lapsCompleted, ParticipantStats stats, GameConfig cfg)
        {
            var p = cfg.progression;
            var e = cfg.economy;
            var r = new RaceRewards();
            if (finished && finishPosition >= 1)
            {
                r.Xp += Pick(p.xpByPosition, finishPosition - 1);
                r.Coins += Pick(e.coinsByPosition, finishPosition - 1);
            }
            else
            {
                r.Xp += p.xpDnf;
                r.Coins += e.coinsDnf;
            }
            r.Xp += p.xpPerLapCompleted * lapsCompleted;
            r.Coins += e.coinsPerLapCompleted * lapsCompleted;
            if (stats != null)
            {
                r.Xp += p.xpLastmaEscape * stats.LastmaEscapes;
                r.Coins += e.coinsLastmaEscapeBonus * stats.LastmaEscapes;
                r.Xp += p.xpPerOvertake * stats.Overtakes;
                r.Xp += p.xpPerItemHit * stats.ItemHitsLanded;
            }
            return r;
        }

        private static int Pick(int[] table, int index)
        {
            if (table == null || table.Length == 0) return 0;
            return table[System.Math.Min(index, table.Length - 1)];
        }

        private static long Pick(long[] table, int index)
        {
            if (table == null || table.Length == 0) return 0;
            return table[System.Math.Min(index, table.Length - 1)];
        }
    }
}
