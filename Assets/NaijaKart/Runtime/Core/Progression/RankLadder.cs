using NaijaKart.Core.Config;

namespace NaijaKart.Core.Progression
{
    /// <summary>Maps a competitive rating to a Nigerian rank tier from configuration (PRD §7, §40).</summary>
    public sealed class RankLadder
    {
        private readonly RankTierConfig[] _tiers;

        public RankLadder(ProgressionConfig cfg)
        {
            _tiers = cfg.ranks;
        }

        public int TierCount => _tiers.Length;

        public RankTierConfig TierFor(int rating)
        {
            RankTierConfig best = _tiers[0];
            for (int i = 1; i < _tiers.Length; i++)
            {
                if (rating >= _tiers[i].minRating) best = _tiers[i];
                else break;
            }
            return best;
        }

        public int TierIndexFor(int rating)
        {
            int idx = 0;
            for (int i = 1; i < _tiers.Length; i++)
            {
                if (rating >= _tiers[i].minRating) idx = i;
                else break;
            }
            return idx;
        }

        public RankTierConfig Tier(int index) => _tiers[System.Math.Max(0, System.Math.Min(index, _tiers.Length - 1))];
    }
}
