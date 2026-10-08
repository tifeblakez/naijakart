using NaijaKart.Unity.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>
    /// Onboarding step "HOW YOU WAN DRIVE?": Tilt (default, highlighted) or Touch. Shows the motion
    /// sensor check line; if there is no accelerometer the Touch card is pre-selected.
    /// </summary>
    public sealed class ControlChoicePresenter : MonoBehaviour
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _subtitle;
        [SerializeField] private TMP_Text _stepLabel;
        [SerializeField] private Button _tiltCard;
        [SerializeField] private Button _touchCard;
        [SerializeField] private Graphic _tiltHighlight;
        [SerializeField] private Graphic _touchHighlight;
        [SerializeField] private TMP_Text _sensorStatus;
        [SerializeField] private Button _continue;
        [SerializeField] private int _step = 3;
        [SerializeField] private int _totalSteps = 5;
        [SerializeField] private Color _selected = new Color(1f, 0.78f, 0.1f);
        [SerializeField] private Color _unselected = new Color(1f, 1f, 1f, 0.15f);

        public System.Action Continued;
        private SteeringMode _choice = SteeringMode.Tilt;

        private void Start()
        {
            if (_title != null) _title.text = NaijaCopy.HowYouWanDrive;
            if (_subtitle != null) _subtitle.text = NaijaCopy.PickHowYouSteer;
            if (_stepLabel != null) _stepLabel.text = $"STEP {_step} OF {_totalSteps}";
            bool sensor = Accelerometer.current != null;
            if (_sensorStatus != null) _sensorStatus.text = sensor ? "✓  " + NaijaCopy.SensorOk : NaijaCopy.SensorMissing;
            _choice = sensor ? SteeringMode.Tilt : SteeringMode.Touch;
            if (_tiltCard != null) { _tiltCard.onClick.AddListener(() => Select(SteeringMode.Tilt)); _tiltCard.interactable = sensor; }
            if (_touchCard != null) _touchCard.onClick.AddListener(() => Select(SteeringMode.Touch));
            if (_continue != null) _continue.onClick.AddListener(Confirm);
            Refresh();
        }

        private void Select(SteeringMode mode)
        {
            _choice = mode;
            Refresh();
        }

        private void Refresh()
        {
            if (_tiltHighlight != null) _tiltHighlight.color = _choice == SteeringMode.Tilt ? _selected : _unselected;
            if (_touchHighlight != null) _touchHighlight.color = _choice == SteeringMode.Touch ? _selected : _unselected;
        }

        private void Confirm()
        {
            PlayerSettingsStore.SteeringMode = _choice;
            PlayerSettingsStore.UseButtonSteering = false;
            PlayerSettingsStore.Save();
            Continued?.Invoke();
        }
    }
}
