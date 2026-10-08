using NaijaKart.Core.Net;
using TMPro;
using UnityEngine;

namespace NaijaKart.Unity.UI
{
    public sealed class LobbyMemberRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _vehicle;
        [SerializeField] private TMP_Text _levelRank;
        [SerializeField] private GameObject _readyMark;

        public void Bind(RoomMemberDto m, bool isLocal)
        {
            if (_name != null) _name.text = isLocal ? m.DisplayName + " (YOU)" : m.DisplayName;
            if (_vehicle != null) _vehicle.text = m.VehicleId;
            if (_levelRank != null) _levelRank.text = $"LV {m.Level}  {RankName(m.RankId)}";
            if (_readyMark != null) _readyMark.SetActive(m.Ready);
        }

        private static string RankName(string id)
        {
            foreach (var r in GameBootstrap.Content.Game.progression.ranks) if (r.id == id) return r.displayName.ToUpperInvariant();
            return "";
        }
    }
}
