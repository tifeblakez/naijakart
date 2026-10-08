using System;
using NaijaKart.Core.Config;

namespace NaijaKart.Core.Progression
{
    /// <summary>
    /// Maps Ranked Points to a Nigerian rank tier and division from configuration (PRD §7, §40;
    /// design screens 03.1 and 15). Tiers are RP floors; each tier has ranked.divisionsPerTier
    /// divisions of ranked.rpPerDivision, counted down (III → II → I) like the design's "Chairman II".
    /// </summary>
    public sealed class RankLadder
    {
        private readonly RankTierConfig[] _tiers;
        private readonly RankedConfig _ranked;

        public RankLadder(ProgressionConfig cfg, RankedConfig ranked = null)
        {
            _tiers = cfg.ranks;
            _ranked = ranked ?? new RankedConfig();
        }

        public int TierCount => _tiers.Length;
        public int TierSpan => System.Math.Max(1, _ranked.divisionsPerTier) * System.Math.Max(1, _ranked.rpPerDivision);

        public RankTierConfig TierFor(int rp) => _tiers[TierIndexFor(rp)];

        public int TierIndexFor(int rp)
        {
            int idx = 0;
            for (int i = 1; i < _tiers.Length; i++)
            {
                if (rp >= _tiers[i].minRating) idx = i;
                else break;
            }
            return idx;
        }

        public RankTierConfig Tier(int index) => _tiers[System.Math.Max(0, System.Math.Min(index, _tiers.Length - 1))];

        /// <summary>Division within the tier: divisionsPerTier (lowest) down to 1 (about to promote).</summary>
        public int DivisionFor(int rp)
        {
            int tier = TierIndexFor(rp);
            int into = System.Math.Max(0, rp - _tiers[tier].minRating);
            int div = System.Math.Max(1, _ranked.divisionsPerTier) - into / System.Math.Max(1, _ranked.rpPerDivision);
            return System.Math.Max(1, div);
        }

        /// <summary>RP earned inside the current division (0 … rpPerDivision-1).</summary>
        public int RpInDivision(int rp)
        {
            int tier = TierIndexFor(rp);
            int into = System.Math.Max(0, rp - _tiers[tier].minRating);
            if (tier == _tiers.Length - 1 && into >= TierSpan) return _ranked.rpPerDivision;   // top of the ladder
            return into % System.Math.Max(1, _ranked.rpPerDivision);
        }

        public int RpToNextDivision(int rp) => System.Math.Max(0, _ranked.rpPerDivision - RpInDivision(rp));

        /// <summary>"Chairman II" style label.</summary>
        public string Label(int rp) => TierFor(rp).displayName + " " + Roman(DivisionFor(rp));

        /// <summary>Season reset: everyone drops resetDropTiers tiers, keeping their division progress.</summary>
        public int AfterSeasonReset(int rp) => System.Math.Max(0, rp - System.Math.Max(0, _ranked.resetDropTiers) * TierSpan);

        public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString() };
    }

    /// <summary>Season clock and Rush Hour windows (data in ranked config; the clock is injected).</summary>
    public static class SeasonClock
    {
        public static DateTime SeasonStart(RankedConfig cfg) =>
            DateTime.TryParse(cfg.seasonStartUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.UnixEpoch;

        public static DateTime SeasonEnd(RankedConfig cfg) => SeasonStart(cfg).AddDays(System.Math.Max(1, cfg.seasonLengthDays));

        public static bool SeasonEnded(RankedConfig cfg, DateTime utcNow) => utcNow >= SeasonEnd(cfg);

        public static int DaysLeft(RankedConfig cfg, DateTime utcNow) => System.Math.Max(0, (int)System.Math.Ceiling((SeasonEnd(cfg) - utcNow).TotalDays));

        public static bool IsRushHour(RankedConfig cfg, DateTime utcNow)
        {
            int hour = utcNow.AddHours(cfg.timezoneOffsetHours).Hour;
            return cfg.rushHourStartHour < cfg.rushHourEndHour
                ? hour >= cfg.rushHourStartHour && hour < cfg.rushHourEndHour
                : hour >= cfg.rushHourStartHour || hour < cfg.rushHourEndHour;
        }

        /// <summary>Seconds until Rush Hour ends (when active) or starts (when not).</summary>
        public static int RushHourSecondsTo(RankedConfig cfg, DateTime utcNow)
        {
            var local = utcNow.AddHours(cfg.timezoneOffsetHours);
            int targetHour = IsRushHour(cfg, utcNow) ? cfg.rushHourEndHour : cfg.rushHourStartHour;
            var target = new DateTime(local.Year, local.Month, local.Day, targetHour % 24, 0, 0, DateTimeKind.Unspecified);
            if (target <= local) target = target.AddDays(1);
            return (int)(target - local).TotalSeconds;
        }
    }
}
