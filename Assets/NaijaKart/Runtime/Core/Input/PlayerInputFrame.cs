using System;
using NaijaKart.Core.Math;

namespace NaijaKart.Core.Input
{
    /// <summary>
    /// The ONLY thing a client sends about driving. Positions, laps and results are never accepted
    /// from clients (PRD §65, §91). Values are sanitised by the server before use.
    /// </summary>
    [Serializable]
    public struct PlayerInputFrame
    {
        /// <summary>Client-side sequence number used for prediction reconciliation.</summary>
        public int Sequence;
        /// <summary>Steering in [-1, 1]. Negative = left.</summary>
        public float Steer;
        public bool Drift;
        public bool UseItem;
        /// <summary>Inventory slot to use when UseItem is set (-1 = first filled slot).</summary>
        public int ItemSlot;
        public bool Boost;
        public bool LookBack;
        public bool Horn;

        public static PlayerInputFrame Neutral => new PlayerInputFrame { ItemSlot = -1 };

        public PlayerInputFrame Sanitised()
        {
            var f = this;
            if (float.IsNaN(f.Steer) || float.IsInfinity(f.Steer)) f.Steer = 0f;
            f.Steer = MathUtil.Clamp(f.Steer, -1f, 1f);
            return f;
        }
    }
}
