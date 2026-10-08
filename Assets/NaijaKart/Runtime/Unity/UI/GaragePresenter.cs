using System.Collections.Generic;
using NaijaKart.Core.Net;
using NaijaKart.Core.Simulation;
using NaijaKart.Unity.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>
    /// Garage: KARTS / RACERS / STYLE tabs, a rotating preview, the selected item's name, OWNED badge,
    /// tagline and stat bars, the card strip (Selected / Owned / price / level lock), CUSTOMIZE and
    /// RIDE AM. Ownership, prices and selection come from the server's Garage message; buying sends
    /// PurchaseVehicle/PurchaseCharacter and the server decides.
    /// </summary>
    public sealed class GaragePresenter : MonoBehaviour
    {
        public enum Tab { Karts, Racers, Style }

        [Header("Header")]
        [SerializeField] private Button _tabKarts;
        [SerializeField] private Button _tabRacers;
        [SerializeField] private Button _tabStyle;
        [SerializeField] private TMP_Text _coins;
        [SerializeField] private TMP_Text _premium;
        [Header("Detail")]
        [SerializeField] private TMP_Text _name;
        [SerializeField] private GameObject _ownedBadge;
        [SerializeField] private TMP_Text _tagline;
        [SerializeField] private StatBar[] _stats = new StatBar[6];
        [SerializeField] private Transform _previewPivot;
        [SerializeField] private float _dragRotateSpeed = 0.4f;
        [Header("Cards")]
        [SerializeField] private Transform _cardStrip;
        [SerializeField] private GarageCard _cardPrefab;
        [Header("Actions")]
        [SerializeField] private Button _customize;
        [SerializeField] private Button _rideAm;
        [SerializeField] private Button _buy;
        [SerializeField] private TMP_Text _buyLabel;
        [SerializeField] private TMP_Text _status;

        private NetworkClient _net;
        private Tab _tab = Tab.Karts;
        private GarageItemDto[] _vehicles = System.Array.Empty<GarageItemDto>();
        private GarageItemDto[] _characters = System.Array.Empty<GarageItemDto>();
        private GarageItemDto _focused;
        private readonly List<GarageCard> _cards = new List<GarageCard>();

        private void Start()
        {
            _net = NetworkClient.Instance;
            _net.GarageReceived += OnGarage;
            _net.ServerError += s => { if (_status != null) _status.text = s; };
            if (_tabKarts != null) _tabKarts.onClick.AddListener(() => SetTab(Tab.Karts));
            if (_tabRacers != null) _tabRacers.onClick.AddListener(() => SetTab(Tab.Racers));
            if (_tabStyle != null) _tabStyle.onClick.AddListener(() => SetTab(Tab.Style));
            if (_rideAm != null) _rideAm.onClick.AddListener(() => _net.JoinQueue(RaceMode.QuickRace));
            if (_buy != null) _buy.onClick.AddListener(Buy);
            if (_rideAm != null) { var t = _rideAm.GetComponentInChildren<TMP_Text>(); if (t != null) t.text = NaijaCopy.RideAm; }
            if (_customize != null) { var t = _customize.GetComponentInChildren<TMP_Text>(); if (t != null) t.text = NaijaCopy.Customize; }
            if (_net.IsAuthenticated) _net.GetGarage(); else _net.Authenticated += _net.GetGarage;
        }

        private void OnDestroy()
        {
            if (_net == null) return;
            _net.GarageReceived -= OnGarage;
            _net.Authenticated -= _net.GetGarage;
        }

        private void Update()
        {
            if (_previewPivot == null) return;
            var mouse = UnityEngine.InputSystem.Pointer.current;
            if (mouse != null && mouse.press.isPressed) _previewPivot.Rotate(0f, -mouse.delta.ReadValue().x * _dragRotateSpeed, 0f, Space.World);
        }

        private void OnGarage(ServerEnvelope m)
        {
            _vehicles = m.Vehicles ?? System.Array.Empty<GarageItemDto>();
            _characters = m.Characters ?? System.Array.Empty<GarageItemDto>();
            if (_coins != null) _coins.text = m.Amount.ToString("N0");
            if (_premium != null) _premium.text = m.PremiumBalance.ToString("N0");
            Rebuild();
        }

        private void SetTab(Tab tab)
        {
            _tab = tab;
            Rebuild();
        }

        private GarageItemDto[] Items => _tab == Tab.Racers ? _characters : _tab == Tab.Karts ? _vehicles : System.Array.Empty<GarageItemDto>();

        private void Rebuild()
        {
            foreach (var c in _cards) if (c != null) Destroy(c.gameObject);
            _cards.Clear();
            var items = Items;
            _focused = null;
            foreach (var item in items) if (item.Selected) _focused = item;
            if (_focused == null && items.Length > 0) _focused = items[0];
            if (_cardStrip != null && _cardPrefab != null)
            {
                foreach (var item in items)
                {
                    var card = Instantiate(_cardPrefab, _cardStrip);
                    card.Bind(item, item == _focused, () => Focus(item));
                    _cards.Add(card);
                }
            }
            ShowDetail();
        }

        private void Focus(GarageItemDto item)
        {
            _focused = item;
            foreach (var c in _cards) c.SetFocused(c.Item == item);
            ShowDetail();
            if (item.Owned)
            {
                if (_tab == Tab.Karts) _net.SelectLoadout(item.Id, null);
                else if (_tab == Tab.Racers) _net.SelectLoadout(null, item.Id);
            }
        }

        private void ShowDetail()
        {
            var item = _focused;
            if (item == null) return;
            if (_name != null) _name.text = item.DisplayName.ToUpperInvariant();
            if (_ownedBadge != null) _ownedBadge.SetActive(item.Owned);
            if (_tagline != null) _tagline.text = item.Tagline ?? "";
            int[] values = { item.Speed, item.Acceleration, item.Handling, item.Drift, item.Weight, item.Boost };
            string[] labels = { "SPEED", "ACCEL", "HANDLING", "DRIFT", "WEIGHT", "BOOST" };
            for (int i = 0; i < _stats.Length && i < values.Length; i++)
            {
                if (_stats[i] == null) continue;
                _stats[i].gameObject.SetActive(_tab == Tab.Karts);
                _stats[i].Set(labels[i], values[i]);
            }
            bool canBuy = !item.Owned && item.LevelReached && item.PriceCoins > 0;
            if (_buy != null) _buy.gameObject.SetActive(!item.Owned);
            if (_buy != null) _buy.interactable = canBuy;
            if (_buyLabel != null) _buyLabel.text = item.Owned ? "" : !item.LevelReached ? string.Format(NaijaCopy.LevelLock, item.UnlockLevel) : string.Format(NaijaCopy.PriceCoins, item.PriceCoins);
        }

        private void Buy()
        {
            if (_focused == null) return;
            if (_tab == Tab.Karts) _net.PurchaseVehicle(_focused.Id);
            else if (_tab == Tab.Racers) _net.PurchaseCharacter(_focused.Id);
        }
    }

}
