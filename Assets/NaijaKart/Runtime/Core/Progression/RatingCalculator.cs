using System.Collections.Generic;
using NaijaKart.Core.Config;

namespace NaijaKart.Core.Progression
{
    /// <summary>
    /// Multiplayer Elo: each racer is compared pairwise against every other finisher. Finishing above
    /// someone is a "win" against them. Deltas are averaged over opponents so an 8-player race moves
    /// rating about as much as a 1v1 would. DNF/eliminated racers are treated as finishing last.
    /// </summary>
    public static class RatingCalculator
    {
        public struct Entry
        {
            public string PlayerId;
            public int Rating;
            /// <summary>1-based finishing position (ties allowed).</summary>
            public int FinishPosition;
        }

        public static Dictionary<string, int> ComputeDeltas(IList<Entry> entries, ProgressionConfig cfg)
        {
            var deltas = new Dictionary<string, int>();
            int n = entries.Count;
            if (n < 2)
            {
                foreach (var e in entries) deltas[e.PlayerId] = 0;
                return deltas;
            }
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double expected = 1.0 / (1.0 + System.Math.Pow(10.0, (entries[j].Rating - entries[i].Rating) / cfg.ratingSpread));
                    double actual = entries[i].FinishPosition < entries[j].FinishPosition ? 1.0
                        : entries[i].FinishPosition == entries[j].FinishPosition ? 0.5 : 0.0;
                    sum += cfg.ratingK * (actual - expected);
                }
                deltas[entries[i].PlayerId] = (int)System.Math.Round(sum / (n - 1));
            }
            return deltas;
        }
    }
}
