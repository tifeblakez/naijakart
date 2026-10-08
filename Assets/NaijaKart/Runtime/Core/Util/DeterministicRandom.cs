using System;

namespace NaijaKart.Core.Util
{
    /// <summary>
    /// Small, allocation-free xorshift PRNG. Seeded per race so that server-side item rolls and
    /// road events are reproducible from the race seed (essential for replays, debugging and
    /// deterministic tests). Never use System.Random inside the simulation.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong NextULong()
        {
            ulong x = _state;
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            _state = x;
            return x;
        }

        /// <summary>Uniform float in [0,1).</summary>
        public float NextFloat() => (NextULong() >> 40) * (1f / 16777216f);

        public float Range(float minInclusive, float maxExclusive) =>
            minInclusive + (maxExclusive - minInclusive) * NextFloat();

        /// <summary>Integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
        }

        public bool Chance(float probability01) => NextFloat() < probability01;

        /// <summary>Picks an index proportionally to the given non-negative weights. Returns -1 if all are zero.</summary>
        public int WeightedIndex(float[] weights)
        {
            if (weights == null || weights.Length == 0) return -1;
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += System.Math.Max(0f, weights[i]);
            if (total <= 0f) return -1;
            float roll = NextFloat() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                float w = System.Math.Max(0f, weights[i]);
                if (roll < w) return i;
                roll -= w;
            }
            return weights.Length - 1;
        }
    }
}
