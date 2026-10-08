using NaijaKart.Unity;
using NaijaKart.Unity.Audio;
using NaijaKart.Unity.CameraRig;
using NaijaKart.Unity.Input;
using NaijaKart.Unity.Net;
using NaijaKart.Unity.Race;
using NaijaKart.Unity.UI;
using NaijaKart.Unity.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace NaijaKart.Editor
{
    /// <summary>
    /// Creates the project's scenes programmatically so the structure is reproducible and reviewable
    /// in git (no hand-built binary scene state). Run once after cloning: Naija Kart ▸ Scaffold Scenes.
    /// Art and UI layout are then iterated on top of these scenes.
    /// </summary>
    public static class SceneScaffolder
    {
        private const string ScenesDir = "Assets/NaijaKart/Scenes";

        [MenuItem("Naija Kart/Scaffold Scenes (Bootstrap, Home, Lobby, Race)")]
        public static void ScaffoldAll()
        {
            System.IO.Directory.CreateDirectory(ScenesDir);
            Bootstrap();
            Home();
            Lobby();
            Race();
            var scenes = new[]
            {
                new EditorBuildSettingsScene(ScenesDir + "/Bootstrap.unity", true),
                new EditorBuildSettingsScene(ScenesDir + "/Home.unity", true),
                new EditorBuildSettingsScene(ScenesDir + "/Lobby.unity", true),
                new EditorBuildSettingsScene(ScenesDir + "/Race.unity", true),
            };
            EditorBuildSettings.scenes = scenes;
            Debug.Log("[NK:editor] scenes scaffolded and added to build settings");
        }

        private static Scene NewScene(string name)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            scene.name = name;
            return scene;
        }

        private static void Save(Scene scene, string name)
        {
            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{name}.unity");
        }

        private static GameObject Canvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            }
            return go;
        }

        private static void Bootstrap()
        {
            var scene = NewScene("Bootstrap");
            var boot = new GameObject("GameBootstrap", typeof(GameBootstrap));
            new GameObject("NetworkClient", typeof(NetworkClient));
            Save(scene, "Bootstrap");
        }

        private static void Home()
        {
            var scene = NewScene("Home");
            var canvas = Canvas("HomeCanvas");
            var home = canvas.AddComponent<HomePresenter>();
            var garage = new GameObject("Garage", typeof(RectTransform), typeof(GaragePresenter));
            garage.transform.SetParent(canvas.transform, false);
            var onboarding = new GameObject("Onboarding_ControlChoice", typeof(RectTransform), typeof(ControlChoicePresenter));
            onboarding.transform.SetParent(canvas.transform, false);
            onboarding.SetActive(!PlayerSettingsStore.OnboardingComplete);
            var practice = new GameObject("LocalPracticeHost", typeof(LocalPracticeHost));
            var hso = new SerializedObject(home);
            hso.FindProperty("_practiceHost").objectReferenceValue = practice.GetComponent<LocalPracticeHost>();
            hso.ApplyModifiedPropertiesWithoutUndo();
            Save(scene, "Home");
        }

        private static void Lobby()
        {
            var scene = NewScene("Lobby");
            var canvas = Canvas("LobbyCanvas");
            canvas.AddComponent<LobbyPresenter>();
            Save(scene, "Lobby");
        }

        private static void Race()
        {
            var scene = NewScene("Race");
            var world = new GameObject("World");
            var trackGo = new GameObject("TrackGreybox", typeof(TrackGreyboxBuilder));
            trackGo.transform.SetParent(world.transform);
            var input = new GameObject("TiltInput", typeof(TiltInputProvider)).GetComponent<TiltInputProvider>();
            var race = new GameObject("RaceClient", typeof(RaceClientController)).GetComponent<RaceClientController>();
            var so = new SerializedObject(race);
            so.FindProperty("_input").objectReferenceValue = input;
            so.FindProperty("_trackBuilder").objectReferenceValue = trackGo.GetComponent<TrackGreyboxBuilder>();
            so.FindProperty("_worldRoot").objectReferenceValue = world.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            var cam = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            var chase = cam.AddComponent<ChaseCamera>();
            var cso = new SerializedObject(chase);
            cso.FindProperty("_race").objectReferenceValue = race;
            cso.FindProperty("_input").objectReferenceValue = input;
            cso.FindProperty("_camera").objectReferenceValue = cam.GetComponent<Camera>();
            cso.ApplyModifiedPropertiesWithoutUndo();

            var canvas = Canvas("RaceCanvas");
            var hud = canvas.AddComponent<RaceHudPresenter>();
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("_race").objectReferenceValue = race;
            hudSo.FindProperty("_input").objectReferenceValue = input;
            hudSo.ApplyModifiedPropertiesWithoutUndo();
            canvas.AddComponent<LastmaPromptPresenter>();
            canvas.AddComponent<ResultsPresenter>();
            canvas.AddComponent<TouchControlsPresenter>();
            var minimap = new GameObject("Minimap", typeof(RectTransform), typeof(MinimapPresenter));
            minimap.transform.SetParent(canvas.transform, false);
            var mso = new SerializedObject(minimap.GetComponent<MinimapPresenter>());
            mso.FindProperty("_race").objectReferenceValue = race;
            mso.ApplyModifiedPropertiesWithoutUndo();
            var steerZone = new GameObject("TouchSteerZone", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(TouchSteerZone));
            steerZone.transform.SetParent(canvas.transform, false);
            var img = steerZone.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            var zrt = steerZone.GetComponent<RectTransform>();
            zrt.anchorMin = new Vector2(0f, 0f); zrt.anchorMax = new Vector2(0.45f, 0.8f); zrt.offsetMin = Vector2.zero; zrt.offsetMax = Vector2.zero;
            var audio = new GameObject("RaceAudio", typeof(RaceAudioPresenter));
            var audioSo = new SerializedObject(audio.GetComponent<RaceAudioPresenter>());
            audioSo.FindProperty("_race").objectReferenceValue = race;
            audioSo.ApplyModifiedPropertiesWithoutUndo();
            Save(scene, "Race");
        }
    }
}
