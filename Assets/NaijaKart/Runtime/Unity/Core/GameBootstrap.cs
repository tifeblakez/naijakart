using System.Collections;
using System.Collections.Generic;
using NaijaKart.Core.Config;
using UnityEngine;
using UnityEngine.Networking;

namespace NaijaKart.Unity
{
    /// <summary>
    /// First object in the Bootstrap scene. Loads configuration, validates it, applies quality and
    /// orientation settings, then hands over to the Home flow. Persists across scenes.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }
        public static IConfigSource Content { get; private set; }
        public static bool IsReady => Content != null;

        [Tooltip("Track files to load on platforms where StreamingAssets must be fetched (Android).")]
        [SerializeField] private string[] _trackFiles = { "third-mainland-rush.json" };
        [SerializeField] private string _nextScene = "Home";
        [SerializeField] private int _targetFrameRate = 60;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = _targetFrameRate;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            PlayerSettingsStore.Load();
            StartCoroutine(Boot());
        }

        private IEnumerator Boot()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                var src = new StreamingAssetsConfigSource();
                string root = StreamingAssetsConfigSource.ConfigRoot;
                var texts = new Dictionary<string, string>();
                var files = new List<string> { "game-config.json", "items.json", "vehicles.json", "characters.json" };
                foreach (var t in _trackFiles) files.Add("tracks/" + t);
                foreach (var f in files)
                {
                    using var req = UnityWebRequest.Get(System.IO.Path.Combine(root, f));
                    yield return req.SendWebRequest();
                    texts[f] = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null;
                    if (texts[f] == null) Debug.LogError("[NK:boot] failed to read " + f + ": " + req.error);
                }
                var tracks = new List<string>();
                foreach (var t in _trackFiles) if (texts.TryGetValue("tracks/" + t, out var json) && json != null) tracks.Add(json);
                src.LoadFromText(texts["game-config.json"], texts["items.json"], texts["vehicles.json"], texts["characters.json"], tracks);
                Content = src;
            }
            else
            {
                Content = StreamingAssetsConfigSource.LoadFromDisk();
            }

            var errors = ConfigValidator.Validate(Content);
            foreach (var e in errors) Debug.LogError("[NK:config] " + e);
            if (errors.Count > 0)
            {
                Debug.LogError("[NK:boot] configuration invalid; staying on bootstrap scene");
                yield break;
            }
            Debug.Log($"[NK:boot] config loaded: {Content.Items.items.Length} items, {Content.Vehicles.vehicles.Length} vehicles, {Content.TrackIds.Length} tracks");
            if (!string.IsNullOrEmpty(_nextScene) && Application.CanStreamedLevelBeLoaded(_nextScene))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(_nextScene);
            }
        }
    }
}
