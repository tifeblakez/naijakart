using System.Text;
using NaijaKart.Core.Net;
using NaijaKart.Core.Simulation;
using NaijaKart.Unity.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NaijaKart.Unity.UI
{
    /// <summary>Results screen (PRD §77): position, time, XP, Coins, rank change, Wahala highlights, RUN AM BACK.</summary>
    public sealed class ResultsPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _headline;
        [SerializeField] private TMP_Text _position;
        [SerializeField] private TMP_Text _time;
        [SerializeField] private TMP_Text _rewards;
        [SerializeField] private TMP_Text _rankChange;
        [SerializeField] private TMP_Text _wahala;
        [SerializeField] private TMP_Text _standings;
        [SerializeField] private Button _rematchButton;
        [SerializeField] private Button _homeButton;
        [SerializeField] private Button _shareButton;
        [SerializeField] private string _homeScene = "Home";

        private NetworkClient _net;
        private RaceResults _results;

        private void Start()
        {
            _net = NetworkClient.Instance;
            _net.ResultsReceived += OnResults;
            if (_rematchButton != null) _rematchButton.onClick.AddListener(() => { _net.VoteRematch(); _rematchButton.interactable = false; });
            if (_homeButton != null) _homeButton.onClick.AddListener(() => { _net.LeaveRoom(); UnityEngine.SceneManagement.SceneManager.LoadScene(_homeScene); });
            if (_shareButton != null) _shareButton.onClick.AddListener(Share);
            if (_panel != null) _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_net != null) _net.ResultsReceived -= OnResults;
        }

        private void OnResults(RaceResults results, SettledRewardDto[] rewards)
        {
            _results = results;
            var me = results.For(_net.PlayerId);
            if (me == null) return;
            if (_panel != null) _panel.SetActive(true);
            if (_rematchButton != null) _rematchButton.interactable = true;
            if (_headline != null) _headline.text = me.FinishPosition == 1 && me.Finished ? NaijaCopy.Victory : me.Finished ? NaijaCopy.Ordinal(me.FinishPosition) : me.Status == Core.Race.ParticipantStatus.Eliminated ? NaijaCopy.Arrested : NaijaCopy.Defeat;
            if (_position != null) _position.text = NaijaCopy.Ordinal(me.Finished ? me.FinishPosition : 0);
            if (_time != null) _time.text = NaijaCopy.FormatTime(me.Finished ? me.TotalTime : 0f) + (me.BestLap > 0 ? "   BEST LAP " + NaijaCopy.FormatTime(me.BestLap) : "");
            SettledRewardDto mine = null;
            foreach (var r in rewards ?? System.Array.Empty<SettledRewardDto>()) if (r.PlayerId == _net.PlayerId) mine = r;
            if (_rewards != null) _rewards.text = mine != null ? $"+{mine.Xp} XP    +{mine.Coins:N0} COINS" : "";
            if (_rankChange != null)
            {
                if (mine != null && mine.RankIdAfter != mine.RankIdBefore) _rankChange.text = RankName(mine.RankIdAfter) + (RankIndex(mine.RankIdAfter) > RankIndex(mine.RankIdBefore) ? " ↑" : " ↓");
                else if (mine != null && mine.RatingDelta != 0) _rankChange.text = $"{RankName(mine.RankIdAfter)}  ({(mine.RatingDelta > 0 ? "+" : "")}{mine.RatingDelta})";
                else _rankChange.text = mine != null && mine.LevelAfter > mine.LevelBefore ? $"LEVEL UP! {mine.LevelAfter}" : "";
            }
            if (_wahala != null)
            {
                var sb = new StringBuilder(NaijaCopy.YourWahala).AppendLine();
                foreach (var h in me.Highlights) sb.Append(h.Label).Append(": ").Append(h.Value).AppendLine();
                _wahala.text = sb.ToString();
            }
            if (_standings != null)
            {
                var sb = new StringBuilder();
                foreach (var e in results.Entries) sb.Append(NaijaCopy.Ordinal(e.Finished ? e.FinishPosition : 0).PadRight(5)).Append(e.DisplayName).Append("  ").Append(e.Finished ? NaijaCopy.FormatTime(e.TotalTime) : e.Status.ToString()).AppendLine();
                _standings.text = sb.ToString();
            }
        }

        /// <summary>Share card text (PRD §58). Deep link placeholder until the backend issues real links.</summary>
        private void Share()
        {
            var me = _results?.For(_net.PlayerId);
            if (me == null) return;
            var sb = new StringBuilder("NAIJA KART\n\n");
            sb.AppendLine(NaijaCopy.Ordinal(me.Finished ? me.FinishPosition : 0));
            sb.AppendLine(GameBootstrap.Content.GetTrack(_results.TrackId)?.displayName?.ToUpperInvariant() ?? _results.TrackId);
            sb.AppendLine("Time: " + NaijaCopy.FormatTime(me.TotalTime)).AppendLine();
            foreach (var h in me.Highlights) sb.AppendLine(h.Label + ": " + h.Value);
            sb.AppendLine().AppendLine("RANK: " + RankName(_net.RankId)).AppendLine().AppendLine(NaijaCopy.Rematch + "?");
            GUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log("[NK:share]\n" + sb);
        }

        private static string RankName(string id)
        {
            foreach (var r in GameBootstrap.Content.Game.progression.ranks) if (r.id == id) return r.displayName.ToUpperInvariant();
            return id ?? "";
        }

        private static int RankIndex(string id)
        {
            var ranks = GameBootstrap.Content.Game.progression.ranks;
            for (int i = 0; i < ranks.Length; i++) if (ranks[i].id == id) return i;
            return 0;
        }
    }
}
