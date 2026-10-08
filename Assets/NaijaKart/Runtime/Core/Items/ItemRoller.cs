using System.Collections.Generic;
using NaijaKart.Core.Config;
using NaijaKart.Core.Util;

namespace NaijaKart.Core.Items
{
    /// <summary>
    /// Position-aware item roll (PRD §28). Weights come from each ItemDefinition.positionWeights; the
    /// roll never guarantees a comeback because every enabled item keeps a non-zero floor if its table
    /// says so. Optional allow-list supports event rules such as "No Item Race" or "Danfo Madness".
    /// </summary>
    public sealed class ItemRoller
    {
        private readonly ItemDefinition[] _items;
        private readonly float[] _weights;

        public ItemRoller(ItemLibrary library, ICollection<string> allowedIds = null)
        {
            var list = new List<ItemDefinition>();
            foreach (var item in library.items)
            {
                if (item.disabled) continue;
                if (allowedIds != null && !allowedIds.Contains(item.id)) continue;
                list.Add(item);
            }
            _items = list.ToArray();
            _weights = new float[_items.Length];
        }

        public int Count => _items.Length;

        public ItemDefinition Roll(int position1Based, int racerCount, DeterministicRandom rng, ItemInventory inventory, float now)
        {
            if (_items.Length == 0) return null;
            int idx = System.Math.Max(0, position1Based - 1);
            for (int i = 0; i < _items.Length; i++)
            {
                var w = _items[i].positionWeights;
                float weight = w == null || w.Length == 0 ? 1f : w[System.Math.Min(idx, w.Length - 1)];
                if (inventory != null && inventory.IsOnCooldown(_items[i].id, now)) weight = 0f;
                _weights[i] = weight;
            }
            int pick = rng.WeightedIndex(_weights);
            if (pick < 0) pick = rng.Range(0, _items.Length);
            return _items[pick];
        }

        public ItemDefinition Find(string id)
        {
            foreach (var i in _items) if (i.id == id) return i;
            return null;
        }
    }
}
