using NaijaKart.Core.Chaos;
using NaijaKart.Core.Simulation;
using UnityEngine;

namespace NaijaKart.Unity.View
{
    /// <summary>
    /// Presentation of a road hazard or item hazard. One prefab per HazardKind is assigned in the
    /// HazardVisualSet; while telegraphed (not yet armed) the view plays its warning (horn, siren,
    /// flashing) so chaos stays readable (PRD §98).
    /// </summary>
    public sealed class HazardView : MonoBehaviour
    {
        [SerializeField] private HazardVisualSet _visuals;
        [SerializeField] private GameObject _telegraphIndicator;

        private GameObject _instance;
        private HazardKind _kind;
        private bool _armed;

        public string Id { get; private set; }

        public void Bind(HazardSnapshot h)
        {
            Id = h.Id;
            _kind = h.Kind;
            name = "Hazard_" + h.Kind + "_" + h.Id;
            var prefab = _visuals != null ? _visuals.PrefabFor(h.Kind) : null;
            if (prefab != null) _instance = Instantiate(prefab, transform);
            if (h.ZoneHalfLength > 0f && _instance != null)
            {
                _instance.transform.localScale = new Vector3(h.Radius * 2f, 1f, h.ZoneHalfLength * 2f);
            }
            else if (_instance != null && h.Kind == HazardKind.OilPatch)
            {
                _instance.transform.localScale = Vector3.one * (h.Radius * 2f);
            }
        }

        public void UpdateFrom(HazardSnapshot h)
        {
            transform.position = h.Position.ToUnity();
            Vector3 dir = h.ZoneHalfLength > 0f ? h.ZoneDirection.ToUnity() : h.Velocity.ToUnity();
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            if (h.Armed != _armed)
            {
                _armed = h.Armed;
                if (_telegraphIndicator != null) _telegraphIndicator.SetActive(!_armed);
            }
        }

        public void Expire()
        {
            Destroy(gameObject);
        }
    }
}
