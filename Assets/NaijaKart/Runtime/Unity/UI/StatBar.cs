using NaijaKart.Core.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>Segmented stat bar (ten pips) as in the garage design.</summary>
    public sealed class StatBar : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Image[] _pips;
        [SerializeField] private Color _on = Color.white;
        [SerializeField] private Color _off = new Color(1f, 1f, 1f, 0.15f);

        public void Set(string label, int value0to100)
        {
            if (_label != null) _label.text = label;
            if (_pips == null) return;
            int lit = Mathf.RoundToInt(value0to100 / 100f * _pips.Length);
            for (int i = 0; i < _pips.Length; i++) if (_pips[i] != null) _pips[i].color = i < lit ? _on : _off;
        }
    }
}
