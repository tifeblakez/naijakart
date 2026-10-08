using System.IO;
using NaijaKart.Core.Config;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace NaijaKart.Editor
{
    /// <summary>
    /// ScriptableObject wrappers so designers edit vehicles/items/characters/tracks in the Inspector,
    /// then export to the JSON that both client and server load. JSON remains the source of truth;
    /// the assets are an authoring convenience (PRD §69, §88 "tools for rapid content creation").
    /// </summary>
    [CreateAssetMenu(menuName = "Naija Kart/Content/Track Definition")]
    public sealed class TrackDefinitionAsset : ScriptableObject
    {
        public TrackDefinition definition = new TrackDefinition();

        [ContextMenu("Export to StreamingAssets JSON")]
        public void Export()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "NaijaKart", "Config", "tracks");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, definition.id + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(definition, Formatting.Indented, Unity.StreamingAssetsConfigSource.JsonSettings));
            AssetDatabase.Refresh();
            Debug.Log("[NK:editor] exported track to " + path);
        }

        [ContextMenu("Import from StreamingAssets JSON")]
        public void Import()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "NaijaKart", "Config", "tracks", definition.id + ".json");
            if (!File.Exists(path)) { Debug.LogError("[NK:editor] not found " + path); return; }
            definition = JsonConvert.DeserializeObject<TrackDefinition>(File.ReadAllText(path), Unity.StreamingAssetsConfigSource.JsonSettings);
            EditorUtility.SetDirty(this);
        }
    }
}
