using NaijaKart.Core.Vehicle;
using NaijaKart.Unity.Input;
using NaijaKart.Unity.Race;
using UnityEngine;

namespace NaijaKart.Unity.CameraRig
{
    /// <summary>
    /// Arcade chase camera (PRD §24, §52): follows the local kart with speed-dependent distance,
    /// FOV widening on boost, drift side offset, shake on impacts, look-back, and a reduced-shake
    /// accessibility option. Pure presentation; reads predicted state only.
    /// </summary>
    public sealed class ChaseCamera : MonoBehaviour
    {
        [SerializeField] private RaceClientController _race;
        [SerializeField] private TiltInputProvider _input;
        [SerializeField] private Camera _camera;
        [SerializeField] private float _distance = 7f;
        [SerializeField] private float _height = 3f;
        [SerializeField] private float _lookAhead = 4f;
        [SerializeField] private float _positionLambda = 8f;
        [SerializeField] private float _rotationLambda = 10f;
        [SerializeField] private float _baseFov = 62f;
        [SerializeField] private float _boostFov = 76f;
        [SerializeField] private float _speedFovPerMetreSecond = 0.25f;
        [SerializeField] private float _driftSideOffset = 1.2f;
        [SerializeField] private float _lookBackSpeedScale = 0.8f;

        private float _shake;
        private float _fov;
        private Vector3 _velocityRef;

        private void Start()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            _fov = _baseFov;
        }

        public void AddShake(float amount)
        {
            if (PlayerSettingsStore.ReducedCameraShake) amount *= 0.25f;
            _shake = Mathf.Min(1f, _shake + amount);
        }

        private void LateUpdate()
        {
            if (_race == null || !_race.HasLocal) return;
            VehicleState s = _race.PredictedLocalState;
            var view = _race.LocalView;
            Vector3 target = view != null ? view.transform.position : s.Position.ToUnity();
            Vector3 forward = s.Forward.ToUnity();
            Vector3 right = s.Right.ToUnity();
            bool lookBack = _input != null && _input.LookBackHeld;
            float dist = _distance + s.Speed * 0.05f;
            Vector3 desired = target - forward * (lookBack ? -dist * _lookBackSpeedScale : dist) + Vector3.up * _height;
            if (s.IsDrifting) desired += right * (-s.DriftDirection * _driftSideOffset);

            float dt = Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-_positionLambda * dt));
            Vector3 lookAt = target + forward * _lookAhead + Vector3.up * 0.8f;
            Quaternion rot = Quaternion.LookRotation((lookAt - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-_rotationLambda * dt));

            float targetFov = _baseFov + s.Speed * _speedFovPerMetreSecond + (s.IsBoosting ? _boostFov - _baseFov : 0f);
            _fov = Mathf.Lerp(_fov, targetFov, 1f - Mathf.Exp(-6f * dt));
            if (_camera != null) _camera.fieldOfView = _fov;

            if (_shake > 0f)
            {
                transform.position += Random.insideUnitSphere * (_shake * 0.3f);
                _shake = Mathf.MoveTowards(_shake, 0f, 3f * dt);
            }
        }
    }
}
