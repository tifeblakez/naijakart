using UnityEngine;

namespace NaijaKart.Unity.View
{
    public sealed class ItemBoxView : MonoBehaviour
    {
        [SerializeField] private GameObject _visual;
        [SerializeField] private float _spinDegreesPerSecond = 90f;
        [SerializeField] private float _bobAmplitude = 0.2f;

        public string Id;
        private bool _available = true;
        private Vector3 _basePos;

        private void Awake() => _basePos = transform.position;

        public void SetAvailable(bool available)
        {
            if (_available == available) return;
            _available = available;
            if (_visual != null) _visual.SetActive(available);
        }

        private void Update()
        {
            if (!_available || _visual == null) return;
            _visual.transform.Rotate(0f, _spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
            _visual.transform.position = _basePos + Vector3.up * (Mathf.Sin(Time.time * 2f) * _bobAmplitude + 0.8f);
        }
    }
}
