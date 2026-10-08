using NaijaKart.Core.Config;

namespace NaijaKart.Core.Progression
{
    /// <summary>Level/XP maths (PRD §39). Level is derived from total XP; nothing stores "level" separately.</summary>
    public static class XpCurve
    {
        public static long XpToNextLevel(int level, ProgressionConfig cfg)
        {
            if (level < 1) level = 1;
            return (long)System.Math.Round(cfg.levelBaseXp * System.Math.Pow(level, cfg.levelExponent));
        }

        public static long TotalXpForLevel(int level, ProgressionConfig cfg)
        {
            long total = 0;
            for (int l = 1; l < level; l++) total += XpToNextLevel(l, cfg);
            return total;
        }

        public static int LevelForXp(long totalXp, ProgressionConfig cfg)
        {
            int level = 1;
            long remaining = totalXp;
            while (level < cfg.maxLevel)
            {
                long need = XpToNextLevel(level, cfg);
                if (remaining < need) break;
                remaining -= need;
                level++;
            }
            return level;
        }

        /// <summary>Progress within the current level in [0,1].</summary>
        public static float LevelProgress(long totalXp, ProgressionConfig cfg)
        {
            int level = LevelForXp(totalXp, cfg);
            if (level >= cfg.maxLevel) return 1f;
            long into = totalXp - TotalXpForLevel(level, cfg);
            return (float)into / XpToNextLevel(level, cfg);
        }
    }
}
