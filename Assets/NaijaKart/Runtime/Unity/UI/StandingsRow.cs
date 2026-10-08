using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>One row of the HUD standings strip: position number, colour dot, name; highlighted for "You".</summary>
    public sealed class StandingsRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text _position;
        [SerializeField] private Image _dot;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private Image _background;
        [SerializeField] private Color _highlight = new Color(1f, 0.78f, 0.1f);
        [SerializeField] private Color _normal = new Color(0f, 0f, 0f, 0.35f);

        public void Set(int position, string name, bool isLocal, Color dot)
        {
            if (_position != null) _position.text = position.ToString();
            if (_name != null) { _name.text = name; _name.fontStyle = isLocal ? FontStyles.Bold | FontStyles.Italic : FontStyles.Normal; }
            if (_dot != null) _dot.color = dot;
            if (_background != null) _background.color = isLocal ? _highlight : _normal;
        }
    }
}
