using System.IO;
using NaijaKart.Core.Config;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace NaijaKart.Editor
{
    public static class ContentExport
    {
        [MenuItem("Naija Kart/Content/Export All Definition Assets to JSON")]
        public static void ExportAll()
        {
            string root = Path.Combine(Application.streamingAssetsPath, "NaijaKart", "Config");
            Directory.CreateDirectory(root);
            var vehicles = Load<VehicleDefinitionAsset>();
            if (vehicles.Length > 0)
            {
                var roster = new VehicleRoster { vehicles = System.Array.ConvertAll(vehicles, a => a.definition) };
                File.WriteAllText(Path.Combine(root, "vehicles.json"), JsonConvert.SerializeObject(roster, Formatting.Indented, Unity.StreamingAssetsConfigSource.JsonSettings));
            }
            var items = Load<ItemDefinitionAsset>();
            if (items.Length > 0)
            {
                var lib = new ItemLibrary { items = System.Array.ConvertAll(items, a => a.definition) };
                File.WriteAllText(Path.Combine(root, "items.json"), JsonConvert.SerializeObject(lib, Formatting.Indented, Unity.StreamingAssetsConfigSource.JsonSettings));
            }
            var chars = Load<CharacterDefinitionAsset>();
            if (chars.Length > 0)
            {
                var roster = new CharacterRoster { characters = System.Array.ConvertAll(chars, a => a.definition) };
                File.WriteAllText(Path.Combine(root, "characters.json"), JsonConvert.SerializeObject(roster, Formatting.Indented, Unity.StreamingAssetsConfigSource.JsonSettings));
            }
            foreach (var t in Load<TrackDefinitionAsset>()) t.Export();
            AssetDatabase.Refresh();
            Debug.Log($"[NK:editor] exported {vehicles.Length} vehicles, {items.Length} items, {chars.Length} characters");
        }

        [MenuItem("Naija Kart/Content/Validate StreamingAssets Config")]
        public static void Validate()
        {
            var src = Unity.StreamingAssetsConfigSource.LoadFromDisk();
            var errors = ConfigValidator.Validate(src);
            if (errors.Count == 0) Debug.Log($"[NK:config] OK: {src.Items.items.Length} items, {src.Vehicles.vehicles.Length} vehicles, {src.Characters.characters.Length} characters, {src.TrackIds.Length} tracks");
            foreach (var e in errors) Debug.LogError("[NK:config] " + e);
        }

        private static T[] Load<T>() where T : ScriptableObject
        {
            var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            var list = new System.Collections.Generic.List<T>();
            foreach (var g in guids) list.Add(AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)));
            return list.ToArray();
        }
    }
}
