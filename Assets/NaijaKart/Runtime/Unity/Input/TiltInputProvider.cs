using NaijaKart.Core.Config;
using NaijaKart.Core.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NaijaKart.Unity.Input
{
    /// <summary>
    /// Primary control model (PRD §15): the phone is held in landscape and rolled left/right. Reads
    /// the accelerometer through the Input System, converts gravity to a roll angle and feeds the
    /// engine-agnostic TiltSteeringMapper. Also owns the on-screen buttons' state so the race code
    /// consumes a single PlayerInputFrame per tick. Button steering is an accessibility fallback.
    /// </summary>
    public sealed class TiltInputProvider : MonoBehaviour
    {
        [Header("Accessibility fallback (tilt stays primary)")]
        [SerializeField] private float _buttonSteerRate = 6f;

        private TiltSteeringMapper _mapper;
        private float _buttonSteer;
        private float _rawRollDegrees;
        private bool _sensorAvailable;

        public bool DriftHeld { get; set; }
        public bool ItemPressed { get; set; }
        public bool BoostPressed { get; set; }
        public bool LookBackHeld { get; set; }
        public bool HornPressed { get; set; }
        /// <summary>-1, 0 or 1 while a steering button is held (button mode only).</summary>
        public int ButtonSteerDirection { get; set; }

        public float CurrentSteer { get; private set; }
        public float RawRollDegrees => _rawRollDegrees;
        public bool SensorAvailable => _sensorAvailable;
        public TiltSteeringMapper Mapper => _mapper;

        private void Awake()
        {
            var cfg = GameBootstrap.Content != null ? GameBootstrap.Content.Game.tilt : new TiltConfig();
            _mapper = new TiltSteeringMapper(cfg);
            _mapper.SetSensitivity(PlayerSettingsStore.TiltSensitivity);
            _mapper.Calibrate(PlayerSettingsStore.TiltCalibrationOffset);
            _mapper.Inverted = PlayerSettingsStore.TiltInverted;
        }

        private void OnEnable()
        {
            if (Accelerometer.current != null)
            {
                InputSystem.EnableDevice(Accelerometer.current);
                _sensorAvailable = true;
            }
        }

        private void OnDisable()
        {
            if (Accelerometer.current != null) InputSystem.DisableDevice(Accelerometer.current);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!PlayerSettingsStore.UseButtonSteering && _sensorAvailable)
            {
                _rawRollDegrees = ReadRollDegrees();
                CurrentSteer = _mapper.Map(_rawRollDegrees, dt);
            }
            else
            {
                float target = ButtonSteerDirection;
#if UNITY_EDITOR || UNITY_STANDALONE
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) target = -1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) target = 1f;
                    DriftHeld |= kb.spaceKey.isPressed;
                    if (kb.eKey.wasPressedThisFrame) ItemPressed = true;
                }
#endif
                _buttonSteer = Mathf.MoveTowards(_buttonSteer, target, _buttonSteerRate * dt);
                CurrentSteer = _buttonSteer;
            }
        }

        /// <summary>
        /// Roll angle in degrees from gravity. In landscape the device X axis runs along the long edge;
        /// tilting the right side down gives positive X acceleration → steer right. Using X against the
        /// component perpendicular to the screen keeps calibration stable at any holding pitch.
        /// </summary>
        private static float ReadRollDegrees()
        {
            Vector3 g = Accelerometer.current.acceleration.ReadValue();
            if (g.sqrMagnitude < 0.01f) return 0f;
            return Mathf.Atan2(g.x, -g.z) * Mathf.Rad2Deg;
        }

        /// <summary>Builds the frame to send this tick and clears one-shot presses.</summary>
        public PlayerInputFrame Consume()
        {
            var f = new PlayerInputFrame
            {
                Steer = CurrentSteer,
                Drift = DriftHeld,
                UseItem = ItemPressed,
                Boost = BoostPressed,
                LookBack = LookBackHeld,
                Horn = HornPressed
            };
            ItemPressed = false;
            BoostPressed = false;
            HornPressed = false;
            return f;
        }

        public void Calibrate()
        {
            _mapper.Calibrate(_rawRollDegrees);
            PlayerSettingsStore.TiltCalibrationOffset = _rawRollDegrees;
            PlayerSettingsStore.Save();
        }

        public void SetSensitivity(float value)
        {
            _mapper.SetSensitivity(value);
            PlayerSettingsStore.TiltSensitivity = _mapper.Sensitivity;
            PlayerSettingsStore.Save();
        }
    }
}
