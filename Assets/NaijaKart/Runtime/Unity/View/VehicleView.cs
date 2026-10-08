using NaijaKart.Core.Config;
using NaijaKart.Core.Vehicle;
using UnityEngine;

namespace NaijaKart.Unity.View
{
    /// <summary>
    /// Presentation of one kart. Receives authoritative/predicted state and drives transform, body
    /// lean, wheel spin, drift sparks, boost exhaust, shield and status effects (PRD §52). Visuals
    /// are keyed by VehicleDefinition.prefabKey so art can be swapped without touching code.
    /// </summary>
    public sealed class VehicleView : MonoBehaviour
    {
        [SerializeField] private Transform _body;
        [SerializeField] private Transform[] _wheels;
        [SerializeField] private float _wheelRadius = 0.35f;
        [SerializeField] private float _maxLeanDegrees = 8f;
        [SerializeField] private float _driftLeanDegrees = 12f;
        [SerializeField] private ParticleSystem _driftSparksBlue;
        [SerializeField] private ParticleSystem _driftSparksOrange;
        [SerializeField] private ParticleSystem _driftSparksPurple;
        [SerializeField] private ParticleSystem _boostExhaust;
        [SerializeField] private GameObject _shieldVisual;
        [SerializeField] private GameObject _stunVisual;
        [SerializeField] private Renderer[] _tintRenderers;

        private float _wheelAngle;
        private float _lean;
        private DriftLevel _lastDriftLevel;

        public string PlayerId { get; private set; }
        public bool IsLocal { get; private set; }
        public VehicleDefinition Definition { get; private set; }
        public VehicleState LastState { get; private set; }
        /// <summary>True when an art body is assigned; otherwise the generated kart template is used.</summary>
        public bool HasArt => _body != null && _body != transform && _body.childCount > 0;

        public void Bind(string playerId, VehicleDefinition definition, bool isLocal)
        {
            PlayerId = playerId;
            Definition = definition;
            IsLocal = isLocal;
            name = $"Kart_{playerId}_{definition?.id}";
            if (_body == null) _body = transform;
        }

        public void Apply(Vector3 position, float heading, in VehicleState state, float dt)
        {
            LastState = state;
            transform.position = position;
            transform.rotation = CoreConversions.HeadingToRotation(heading);

            // Body lean from lateral slide and drift; wheel spin from speed.
            float targetLean = -Mathf.Clamp(state.LateralVelocity * 0.6f, -1f, 1f) * _maxLeanDegrees;
            if (state.IsDrifting) targetLean += -state.DriftDirection * _driftLeanDegrees;
            _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-10f * dt));
            if (_body != null) _body.localRotation = Quaternion.Euler(0f, 0f, _lean);
            if (_wheels != null && _wheelRadius > 0f)
            {
                _wheelAngle = (_wheelAngle + state.Speed * dt / _wheelRadius * Mathf.Rad2Deg) % 360f;
                foreach (var w in _wheels) if (w != null) w.localRotation = Quaternion.Euler(_wheelAngle, 0f, 0f);
            }

            SetSparks(state.IsDrifting ? state.DriftLevel : DriftLevel.None);
            Toggle(_boostExhaust, state.IsBoosting);
            if (_shieldVisual != null) _shieldVisual.SetActive(state.HasShield);
            if (_stunVisual != null) _stunVisual.SetActive(state.IsStunned);
        }

        private void SetSparks(DriftLevel level)
        {
            if (level == _lastDriftLevel) return;
            _lastDriftLevel = level;
            Toggle(_driftSparksBlue, level == DriftLevel.Blue);
            Toggle(_driftSparksOrange, level == DriftLevel.Orange);
            Toggle(_driftSparksPurple, level == DriftLevel.Purple);
        }

        private static void Toggle(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (on && !ps.isPlaying) ps.Play();
            else if (!on && ps.isPlaying) ps.Stop();
        }

        public void SetTint(Color c)
        {
            if (_tintRenderers == null) return;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", c);
            foreach (var r in _tintRenderers) if (r != null) r.SetPropertyBlock(block);
        }
    }
}
