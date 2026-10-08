using System.Collections.Generic;

namespace NaijaKart.Core.Items
{
    /// <summary>One-slot item inventory with roulette delay and per-item cooldowns.</summary>
    public sealed class ItemInventory
    {
        private readonly Dictionary<string, float> _cooldownUntil = new Dictionary<string, float>();

        public string HeldItemId { get; private set; }
        public float RollRemaining { get; private set; }

        public bool HasItem => HeldItemId != null;
        public bool IsReady => HasItem && RollRemaining <= 0f;

        public void Grant(string itemId, float rollSeconds, float cooldownSeconds, float now)
        {
            HeldItemId = itemId;
            RollRemaining = rollSeconds;
            if (cooldownSeconds > 0f) _cooldownUntil[itemId] = now + cooldownSeconds;
        }

        public bool IsOnCooldown(string itemId, float now) =>
            _cooldownUntil.TryGetValue(itemId, out float until) && now < until;

        public string Consume()
        {
            string id = HeldItemId;
            HeldItemId = null;
            RollRemaining = 0f;
            return id;
        }

        public void Tick(float dt)
        {
            if (RollRemaining > 0f) RollRemaining = System.Math.Max(0f, RollRemaining - dt);
        }

        public void Clear()
        {
            HeldItemId = null;
            RollRemaining = 0f;
        }
    }
}
