using System.Collections.Generic;

namespace NaijaKart.Core.Items
{
    /// <summary>Multi-slot item inventory (HUD shows four) with per-slot roulette delay and per-item cooldowns.</summary>
    public sealed class ItemInventory
    {
        private readonly string[] _slots;
        private readonly float[] _rollRemaining;
        private readonly Dictionary<string, float> _cooldownUntil = new Dictionary<string, float>();

        public ItemInventory(int slotCount = 1)
        {
            _slots = new string[System.Math.Max(1, slotCount)];
            _rollRemaining = new float[_slots.Length];
        }

        public int SlotCount => _slots.Length;
        public string ItemAt(int slot) => slot >= 0 && slot < _slots.Length ? _slots[slot] : null;
        public float RollRemainingAt(int slot) => slot >= 0 && slot < _slots.Length ? _rollRemaining[slot] : 0f;
        public bool IsReady(int slot) => ItemAt(slot) != null && _rollRemaining[slot] <= 0f;

        /// <summary>First filled slot's item (HUD convenience).</summary>
        public string HeldItemId
        {
            get { for (int i = 0; i < _slots.Length; i++) if (_slots[i] != null) return _slots[i]; return null; }
        }

        public bool HasItem => HeldItemId != null;
        public bool IsFull { get { for (int i = 0; i < _slots.Length; i++) if (_slots[i] == null) return false; return true; } }
        public int Count { get { int n = 0; for (int i = 0; i < _slots.Length; i++) if (_slots[i] != null) n++; return n; } }

        /// <summary>True if any filled slot is ready.</summary>
        public bool IsReadyAny { get { for (int i = 0; i < _slots.Length; i++) if (IsReady(i)) return true; return false; } }

        /// <summary>Resolves the slot a UseItem press refers to: explicit slot, or the first ready slot. -1 if none.</summary>
        public int ResolveSlot(int requested)
        {
            if (requested >= 0 && requested < _slots.Length) return IsReady(requested) ? requested : -1;
            for (int i = 0; i < _slots.Length; i++) if (IsReady(i)) return i;
            return -1;
        }

        /// <summary>Puts the item in the first empty slot. Returns the slot index or -1 when full.</summary>
        public int Grant(string itemId, float rollSeconds, float cooldownSeconds, float now)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null) continue;
                _slots[i] = itemId;
                _rollRemaining[i] = rollSeconds;
                if (cooldownSeconds > 0f) _cooldownUntil[itemId] = now + cooldownSeconds;
                return i;
            }
            return -1;
        }

        public bool IsOnCooldown(string itemId, float now) =>
            _cooldownUntil.TryGetValue(itemId, out float until) && now < until;

        public string Consume(int slot)
        {
            if (slot < 0 || slot >= _slots.Length) return null;
            string id = _slots[slot];
            _slots[slot] = null;
            _rollRemaining[slot] = 0f;
            return id;
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _rollRemaining.Length; i++)
                if (_rollRemaining[i] > 0f) _rollRemaining[i] = System.Math.Max(0f, _rollRemaining[i] - dt);
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++) { _slots[i] = null; _rollRemaining[i] = 0f; }
        }

        public string[] SnapshotIds() { var a = new string[_slots.Length]; _slots.CopyTo(a, 0); return a; }
        public bool[] SnapshotReady() { var a = new bool[_slots.Length]; for (int i = 0; i < a.Length; i++) a[i] = IsReady(i); return a; }
    }
}
