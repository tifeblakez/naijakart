using UnityEngine;

namespace NaijaKart.Unity
{
    public enum SteeringMode { Tilt = 0, Touch = 1, Buttons = 2 }

    /// <summary>Local, non-competitive player preferences (PRD §72). Never gameplay-affecting server state.</summary>
    public static class PlayerSettingsStore
    {
        public static SteeringMode SteeringMode = SteeringMode.Tilt;
        public static bool OnboardingComplete = false;
        public static float TiltSensitivity = 1f;
        public static float TiltCalibrationOffset = 0f;
        public static bool TiltInverted = false;
        public static bool UseButtonSteering = false;
        public static bool LeftHandedLayout = false;
        public static bool ReducedCameraShake = false;
        public static bool Vibration = true;
        public static float MusicVolume = 0.8f;
        public static float SfxVolume = 1f;
        public static float VoiceVolume = 1f;
        public static int QualityLevel = -1;
        public static string PlayerId = null;
        public static string DisplayName = null;
        public static string ServerHost = "127.0.0.1";
        public static int ServerPort = 7777;

        public static void Load()
        {
            TiltSensitivity = PlayerPrefs.GetFloat("nk.tilt.sensitivity", 1f);
            TiltCalibrationOffset = PlayerPrefs.GetFloat("nk.tilt.offset", 0f);
            TiltInverted = PlayerPrefs.GetInt("nk.tilt.inverted", 0) == 1;
            UseButtonSteering = PlayerPrefs.GetInt("nk.steer.buttons", 0) == 1;
            SteeringMode = (SteeringMode)PlayerPrefs.GetInt("nk.steer.mode", 0);
            OnboardingComplete = PlayerPrefs.GetInt("nk.onboarding.done", 0) == 1;
            LeftHandedLayout = PlayerPrefs.GetInt("nk.ui.lefthanded", 0) == 1;
            ReducedCameraShake = PlayerPrefs.GetInt("nk.cam.reducedshake", 0) == 1;
            Vibration = PlayerPrefs.GetInt("nk.vibration", 1) == 1;
            MusicVolume = PlayerPrefs.GetFloat("nk.audio.music", 0.8f);
            SfxVolume = PlayerPrefs.GetFloat("nk.audio.sfx", 1f);
            VoiceVolume = PlayerPrefs.GetFloat("nk.audio.voice", 1f);
            QualityLevel = PlayerPrefs.GetInt("nk.quality", -1);
            PlayerId = PlayerPrefs.GetString("nk.player.id", null);
            DisplayName = PlayerPrefs.GetString("nk.player.name", null);
            ServerHost = PlayerPrefs.GetString("nk.server.host", "127.0.0.1");
            ServerPort = PlayerPrefs.GetInt("nk.server.port", 7777);
            if (string.IsNullOrEmpty(PlayerId))
            {
                PlayerId = "guest_" + System.Guid.NewGuid().ToString("N").Substring(0, 12);
                PlayerPrefs.SetString("nk.player.id", PlayerId);
            }
            if (QualityLevel >= 0) QualitySettings.SetQualityLevel(QualityLevel, true);
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat("nk.tilt.sensitivity", TiltSensitivity);
            PlayerPrefs.SetFloat("nk.tilt.offset", TiltCalibrationOffset);
            PlayerPrefs.SetInt("nk.tilt.inverted", TiltInverted ? 1 : 0);
            PlayerPrefs.SetInt("nk.steer.buttons", UseButtonSteering ? 1 : 0);
            PlayerPrefs.SetInt("nk.steer.mode", (int)SteeringMode);
            PlayerPrefs.SetInt("nk.onboarding.done", OnboardingComplete ? 1 : 0);
            PlayerPrefs.SetInt("nk.ui.lefthanded", LeftHandedLayout ? 1 : 0);
            PlayerPrefs.SetInt("nk.cam.reducedshake", ReducedCameraShake ? 1 : 0);
            PlayerPrefs.SetInt("nk.vibration", Vibration ? 1 : 0);
            PlayerPrefs.SetFloat("nk.audio.music", MusicVolume);
            PlayerPrefs.SetFloat("nk.audio.sfx", SfxVolume);
            PlayerPrefs.SetFloat("nk.audio.voice", VoiceVolume);
            PlayerPrefs.SetInt("nk.quality", QualityLevel);
            PlayerPrefs.SetString("nk.player.id", PlayerId ?? "");
            PlayerPrefs.SetString("nk.player.name", DisplayName ?? "");
            PlayerPrefs.SetString("nk.server.host", ServerHost);
            PlayerPrefs.SetInt("nk.server.port", ServerPort);
            PlayerPrefs.Save();
        }
    }
}
