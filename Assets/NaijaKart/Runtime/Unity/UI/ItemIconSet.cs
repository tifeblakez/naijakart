using UnityEngine;

namespace NaijaKart.Unity.UI
{
    [CreateAssetMenu(menuName = "Naija Kart/Item Icon Set")]
    public sealed class ItemIconSet : ScriptableObject
    {
        [System.Serializable]
        public struct Entry { public string ItemId; public Sprite Sprite; }
        [SerializeField] private Entry[] _entries;

        public Sprite SpriteFor(string itemId)
        {
            if (_entries == null) return null;
            foreach (var e in _entries) if (e.ItemId == itemId) return e.Sprite;
            return null;
        }
    }
}
