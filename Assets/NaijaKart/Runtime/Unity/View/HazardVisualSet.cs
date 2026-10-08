using NaijaKart.Core.Chaos;
using UnityEngine;

namespace NaijaKart.Unity.View
{
    /// <summary>Maps hazard kinds to prefabs. Lives in the project as a ScriptableObject asset.</summary>
    [CreateAssetMenu(menuName = "Naija Kart/Hazard Visual Set")]
    public sealed class HazardVisualSet : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public HazardKind Kind;
            public GameObject Prefab;
        }

        [SerializeField] private Entry[] _entries;

        public GameObject PrefabFor(HazardKind kind)
        {
            if (_entries == null) return null;
            foreach (var e in _entries) if (e.Kind == kind) return e.Prefab;
            return null;
        }
    }
}
