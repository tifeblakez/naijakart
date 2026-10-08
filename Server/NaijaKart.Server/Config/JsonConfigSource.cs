using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NaijaKart.Core.Config;

namespace NaijaKart.Server.Config
{
    /// <summary>Loads the shared JSON configuration (the same files Unity ships in StreamingAssets).</summary>
    public sealed class JsonConfigSource : IConfigSource
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            IgnoreReadOnlyProperties = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly Dictionary<string, TrackDefinition> _tracks = new Dictionary<string, TrackDefinition>();

        public GameConfig Game { get; private set; }
        public ItemLibrary Items { get; private set; }
        public VehicleRoster Vehicles { get; private set; }
        public CharacterRoster Characters { get; private set; }
        public NaijaKart.Core.Challenges.ChallengeLibrary Challenges { get; private set; }
        public string[] TrackIds => new List<string>(_tracks.Keys).ToArray();
        public string RootDirectory { get; }

        public JsonConfigSource(string configDirectory)
        {
            RootDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
            Game = Load<GameConfig>(Path.Combine(configDirectory, "game-config.json")) ?? new GameConfig();
            Items = Load<ItemLibrary>(Path.Combine(configDirectory, "items.json")) ?? new ItemLibrary();
            Vehicles = Load<VehicleRoster>(Path.Combine(configDirectory, "vehicles.json")) ?? new VehicleRoster();
            Characters = Load<CharacterRoster>(Path.Combine(configDirectory, "characters.json")) ?? new CharacterRoster();
            Challenges = Load<NaijaKart.Core.Challenges.ChallengeLibrary>(Path.Combine(configDirectory, "challenges.json")) ?? new NaijaKart.Core.Challenges.ChallengeLibrary();
            string tracksDir = Path.Combine(configDirectory, "tracks");
            if (Directory.Exists(tracksDir))
            {
                foreach (var file in Directory.GetFiles(tracksDir, "*.json"))
                {
                    var t = Load<TrackDefinition>(file);
                    if (t != null && !string.IsNullOrEmpty(t.id)) _tracks[t.id] = t;
                }
            }
        }

        public TrackDefinition GetTrack(string trackId) => _tracks.TryGetValue(trackId ?? string.Empty, out var t) ? t : null;

        private static T Load<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }

        /// <summary>Locates the config directory relative to the executable or the repository root.</summary>
        public static string ResolveDefaultDirectory()
        {
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "Config"),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets", "NaijaKart", "Config"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Assets", "StreamingAssets", "NaijaKart", "Config"),
            };
            foreach (var c in candidates)
            {
                if (File.Exists(Path.Combine(c, "items.json"))) return Path.GetFullPath(c);
            }
            return candidates[0];
        }

        public static void WriteDefaults(string configDirectory)
        {
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, "game-config.json"), JsonSerializer.Serialize(new GameConfig(), Options) + Environment.NewLine);
        }
    }
}
