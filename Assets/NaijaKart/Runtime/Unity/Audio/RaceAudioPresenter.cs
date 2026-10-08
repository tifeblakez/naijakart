using System.Collections.Generic;
using NaijaKart.Core.Race;
using NaijaKart.Unity.Net;
using NaijaKart.Unity.Race;
using UnityEngine;

namespace NaijaKart.Unity.Audio
{
    /// <summary>
    /// Maps race events and local kart state to audio (PRD §52, §53): engine pitch from speed, drift
    /// and boost loops, item/LASTMA stingers, Nigerian voice lines, final-lap music escalation.
    /// Clips are resolved by key from an AudioBank asset so sound design never touches code.
    /// </summary>
    public sealed class RaceAudioPresenter : MonoBehaviour
    {
        [SerializeField] private RaceClientController _race;
        [SerializeField] private AudioBank _bank;
        [SerializeField] private AudioSource _engine;
        [SerializeField] private AudioSource _drift;
        [SerializeField] private AudioSource _music;
        [SerializeField] private AudioSource _oneShots;
        [SerializeField] private AudioSource _voice;
        [SerializeField] private float _enginePitchMin = 0.7f;
        [SerializeField] private float _enginePitchMax = 1.6f;

        private NetworkClient _net;
        private static readonly Dictionary<RaceEventType, string> SfxKeys = new Dictionary<RaceEventType, string>
        {
            { RaceEventType.CountdownTick, "sfx_countdown" },
            { RaceEventType.RaceStarted, "sfx_go" },
            { RaceEventType.ItemGranted, "sfx_item_get" },
            { RaceEventType.Collision, "sfx_collision" },
            { RaceEventType.HazardHit, "sfx_hazard_hit" },
            { RaceEventType.ShieldBlocked, "sfx_shield_block" },
            { RaceEventType.LastmaWarning, "sfx_lastma_siren" },
            { RaceEventType.LastmaCaught, "sfx_lastma_caught" },
            { RaceEventType.LastmaEscaped, "sfx_lastma_escaped" },
            { RaceEventType.LapCompleted, "sfx_lap" },
            { RaceEventType.PlayerFinished, "sfx_finish" },
            { RaceEventType.RoadEventTelegraph, "sfx_horn_warning" },
        };
        private static readonly Dictionary<RaceEventType, string> VoiceKeys = new Dictionary<RaceEventType, string>
        {
            { RaceEventType.ItemHit, "vo_omo" },
            { RaceEventType.LastmaWarning, "vo_wahala" },
            { RaceEventType.LastmaEscaped, "vo_run_am" },
            { RaceEventType.LastmaCaught, "vo_dem_don_catch_you" },
            { RaceEventType.Overtake, "vo_move" },
            { RaceEventType.FinalLapStarted, "vo_last_lap" },
        };

        private void Start()
        {
            _net = NetworkClient.Instance;
            _net.RaceEventReceived += OnEvent;
            ApplyVolumes();
        }

        private void OnDestroy()
        {
            if (_net != null) _net.RaceEventReceived -= OnEvent;
        }

        public void ApplyVolumes()
        {
            if (_music != null) _music.volume = PlayerSettingsStore.MusicVolume;
            if (_voice != null) _voice.volume = PlayerSettingsStore.VoiceVolume;
            float sfx = PlayerSettingsStore.SfxVolume;
            if (_engine != null) _engine.volume = sfx * 0.6f;
            if (_drift != null) _drift.volume = sfx * 0.5f;
            if (_oneShots != null) _oneShots.volume = sfx;
        }

        private void Update()
        {
            if (_race == null || !_race.HasLocal) return;
            var s = _race.PredictedLocalState;
            float top = GameBootstrap.Content.Game.driving.maxTopSpeed;
            if (_engine != null)
            {
                _engine.pitch = Mathf.Lerp(_enginePitchMin, _enginePitchMax, Mathf.Clamp01(s.Speed / top)) * (s.IsBoosting ? 1.1f : 1f);
                if (!_engine.isPlaying && _engine.clip != null) _engine.Play();
            }
            if (_drift != null)
            {
                if (s.IsDrifting && !_drift.isPlaying) _drift.Play();
                else if (!s.IsDrifting && _drift.isPlaying) _drift.Stop();
            }
        }

        private void OnEvent(RaceEvent e)
        {
            bool mine = e.PlayerId == _net.PlayerId || e.TargetPlayerId == _net.PlayerId;
            bool global = e.Type == RaceEventType.CountdownTick || e.Type == RaceEventType.RaceStarted || e.Type == RaceEventType.FinalLapStarted;
            if (!mine && !global) return;
            if (SfxKeys.TryGetValue(e.Type, out var sfxKey)) PlayOneShot(_oneShots, sfxKey);
            if (VoiceKeys.TryGetValue(e.Type, out var voKey) && (e.PlayerId == _net.PlayerId || e.TargetPlayerId == _net.PlayerId || global)) PlayOneShot(_voice, voKey);
            if (e.Type == RaceEventType.ItemUsed && e.PlayerId == _net.PlayerId) PlayOneShot(_oneShots, ItemSfx(e.Payload));
            if (e.Type == RaceEventType.FinalLapStarted && _music != null)
            {
                var clip = _bank != null ? _bank.ClipFor("music_final_lap") : null;
                if (clip != null) { _music.clip = clip; _music.Play(); }
                else _music.pitch = 1.06f;
            }
        }

        private string ItemSfx(string itemId)
        {
            foreach (var i in GameBootstrap.Content.Items.items) if (i.id == itemId) return i.sfxKey;
            return null;
        }

        private void PlayOneShot(AudioSource source, string key)
        {
            if (source == null || _bank == null || string.IsNullOrEmpty(key)) return;
            var clip = _bank.ClipFor(key);
            if (clip != null) source.PlayOneShot(clip);
        }
    }
}
