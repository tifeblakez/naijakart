using UnityEngine;

namespace NaijaKart.Unity.Audio
{
    [CreateAssetMenu(menuName = "Naija Kart/Audio Bank")]
    public sealed class AudioBank : ScriptableObject
    {
        [System.Serializable]
        public struct Entry { public string Key; public AudioClip[] Variants; }
        [SerializeField] private Entry[] _entries;

        public AudioClip ClipFor(string key)
        {
            if (_entries == null) return null;
            foreach (var e in _entries)
            {
                if (e.Key != key || e.Variants == null || e.Variants.Length == 0) continue;
                return e.Variants[Random.Range(0, e.Variants.Length)];
            }
            return null;
        }
    }
}
