using NaijaKart.Unity;
using NaijaKart.Unity.View;
using UnityEditor;
using UnityEngine;

namespace NaijaKart.Editor
{
    public static class GreyboxTools
    {
        [MenuItem("Naija Kart/Greybox/Build Third Mainland Rush in Current Scene")]
        public static void BuildThirdMainland() => Build("third_mainland_rush");

        private static void Build(string trackId)
        {
            var content = StreamingAssetsConfigSource.LoadFromDisk();
            var def = content.GetTrack(trackId);
            if (def == null) { Debug.LogError("[NK:greybox] track not found: " + trackId); return; }
            var builder = Object.FindFirstObjectByType<TrackGreyboxBuilder>();
            if (builder == null) builder = new GameObject("TrackGreybox", typeof(TrackGreyboxBuilder)).GetComponent<TrackGreyboxBuilder>();
            builder.Build(def);
            Selection.activeObject = builder.gameObject;
            Debug.Log($"[NK:greybox] built {def.displayName}: {def.checkpoints.Length} checkpoints, {def.itemBoxes.Length} item boxes, {def.shortcutRoads.Length} shortcut roads");
        }

        [MenuItem("Naija Kart/Greybox/Clear Greybox in Current Scene")]
        public static void ClearGreybox()
        {
            var builder = Object.FindFirstObjectByType<TrackGreyboxBuilder>();
            if (builder != null) builder.Clear();
        }
    }
}
