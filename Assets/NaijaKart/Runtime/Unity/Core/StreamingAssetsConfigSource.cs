using System.Collections.Generic;
using System.IO;
using NaijaKart.Core.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace NaijaKart.Unity
{
    /// <summary>
    /// Loads the same JSON the server uses from StreamingAssets/NaijaKart/Config. On Android,
    /// StreamingAssets live inside the APK, so files are read through UnityWebRequest at boot and
    /// cached; use LoadAsync from GameBootstrap. The editor and iOS read the files directly.
    /// </summary>
    public sealed class StreamingAssetsConfigSource : IConfigSource
    {
        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly Dictionary<string, TrackDefinition> _tracks = new Dictionary<string, TrackDefinition>();

        public GameConfig Game { get; private set; } = new GameConfig();
        public ItemLibrary Items { get; private set; } = new ItemLibrary();
        public VehicleRoster Vehicles { get; private set; } = new VehicleRoster();
        public CharacterRoster Characters { get; private set; } = new CharacterRoster();
        public string[] TrackIds { get { var ids = new string[_tracks.Count]; _tracks.Keys.CopyTo(ids, 0); return ids; } }
        public TrackDefinition GetTrack(string trackId) => _tracks.TryGetValue(trackId ?? "", out var t) ? t : null;

        public static string ConfigRoot => Path.Combine(Application.streamingAssetsPath, "NaijaKart", "Config");

        /// <summary>Populates from already-read JSON text (works on every platform).</summary>
        public void LoadFromText(string gameJson, string itemsJson, string vehiclesJson, string charactersJson, IEnumerable<string> trackJsons)
        {
            if (!string.IsNullOrEmpty(gameJson)) Game = JsonConvert.DeserializeObject<GameConfig>(gameJson, JsonSettings);
            if (!string.IsNullOrEmpty(itemsJson)) Items = JsonConvert.DeserializeObject<ItemLibrary>(itemsJson, JsonSettings);
            if (!string.IsNullOrEmpty(vehiclesJson)) Vehicles = JsonConvert.DeserializeObject<VehicleRoster>(vehiclesJson, JsonSettings);
            if (!string.IsNullOrEmpty(charactersJson)) Characters = JsonConvert.DeserializeObject<CharacterRoster>(charactersJson, JsonSettings);
            foreach (var json in trackJsons)
            {
                var t = JsonConvert.DeserializeObject<TrackDefinition>(json, JsonSettings);
                if (t != null && !string.IsNullOrEmpty(t.id)) _tracks[t.id] = t;
            }
        }

        /// <summary>Synchronous load for editor/desktop/iOS where StreamingAssets is a plain folder.</summary>
        public static StreamingAssetsConfigSource LoadFromDisk()
        {
            var src = new StreamingAssetsConfigSource();
            string root = ConfigRoot;
            string Read(string name) { string p = Path.Combine(root, name); return File.Exists(p) ? File.ReadAllText(p) : null; }
            var tracks = new List<string>();
            string tracksDir = Path.Combine(root, "tracks");
            if (Directory.Exists(tracksDir)) foreach (var f in Directory.GetFiles(tracksDir, "*.json")) tracks.Add(File.ReadAllText(f));
            src.LoadFromText(Read("game-config.json"), Read("items.json"), Read("vehicles.json"), Read("characters.json"), tracks);
            return src;
        }
    }
}
