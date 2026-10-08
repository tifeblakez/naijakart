using NaijaKart.Core.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>Garage card: name + status line (Selected / Owned / 5,000 C / Lvl 12), highlighted when focused.</summary>
    public sealed class GarageCard : MonoBehaviour
    {
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private Image _frame;
        [SerializeField] private Button _button;
        [SerializeField] private Color _focused = new Color(1f, 0.78f, 0.1f);
        [SerializeField] private Color _normal = new Color(1f, 1f, 1f, 0.12f);
        [SerializeField] private Color _locked = new Color(0.6f, 0.6f, 0.6f);

        public GarageItemDto Item { get; private set; }

        public void Bind(GarageItemDto item, bool focused, System.Action onTap)
        {
            Item = item;
            if (_name != null) { _name.text = item.DisplayName.ToUpperInvariant(); _name.color = item.Owned || item.LevelReached ? Color.white : _locked; }
            if (_status != null)
            {
                _status.text = item.Selected ? NaijaCopy.Selected : item.Owned ? "Owned"
                    : !item.LevelReached ? string.Format(NaijaCopy.LevelLock, item.UnlockLevel) : string.Format(NaijaCopy.PriceCoins, item.PriceCoins);
            }
            if (_button != null) _button.onClick.AddListener(() => onTap?.Invoke());
            SetFocused(focused);
        }

        public void SetFocused(bool focused)
        {
            if (_frame != null) _frame.color = focused ? _focused : _normal;
        }
    }
}
