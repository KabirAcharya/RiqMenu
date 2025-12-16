using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using RiqMenu.Core;
using RiqMenu.Songs;
using RiqMenu.Audio;
using RiqMenu.UI;
using RiqMenu.Updater;

namespace RiqMenu
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class RiqMenuMain : BaseUnityPlugin {
        public string riqPath;

        /// <summary>
        /// Tracks whether the current song was launched from RiqMenu.
        /// Used to determine if we should return to TitleScreen instead of StageSelect.
        /// </summary>
        public static bool LaunchedFromRiqMenu { get; set; } = false;

        private static RiqMenuMain _instance;
        public static RiqMenuMain Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<RiqMenuMain>();
                    if (_instance == null) {
                        GameObject singletonObject = new GameObject();
                        _instance = singletonObject.AddComponent<RiqMenuMain>();
                        singletonObject.name = typeof(RiqMenuMain).ToString() + " (Singleton)";
                        DontDestroyOnLoad(singletonObject);
                    }
                }
                return _instance;
            }
        }

        private static TitleScript titleScript;

        private RiqMenuSystemManager systemManager;
        private SongManager songManager => systemManager?.SongManager;
        private AudioManager audioManager => systemManager?.AudioManager;
        private UIManager uiManager => systemManager?.UIManager;

        public Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);

        public static Scene currentScene;

        public void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Check for updates first - will restart if update found
            AutoUpdater.CheckAndUpdate(Logger);

            systemManager = RiqMenuSystemManager.Instance;
            systemManager.InitializeSystems();

            if (songManager != null) {
                songManager.OnSongsLoaded += OnSongsLoaded;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            harmony.PatchAll();

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        private void Update() {
            // Global F1 handler for all scenes
            if (UnityEngine.Input.GetKeyDown(KeyCode.F1)) {
                HandleF1KeyPress();
            }
        }

        private void HandleF1KeyPress() {
            // Only allow F1 toggle in supported scenes
            string sceneName = currentScene.name;
            if (sceneName == SceneKey.TitleScreen.ToString() ||
                sceneName == SceneKey.StageSelect.ToString() ||
                sceneName == "StageSelectDemo") {
                ToggleCustomSongsOverlay();
            }
        }

        private void OnDestroy() {
            systemManager?.OnDestroy();
        }

        private void OnSongsLoaded(CustomSong[] songs) {
            Logger.LogInfo($"Songs loaded: {songs.Length} songs found");
        }

        /// <summary>
        /// Legacy property for compatibility
        /// </summary>
        public CustomSong[] songList => songManager?.SongList ?? new CustomSong[0];

        /// <summary>
        /// Toggle the custom songs overlay.
        /// </summary>
        public void ToggleCustomSongsOverlay() {
            if (uiManager != null && uiManager.Overlay != null) {
                uiManager.Overlay.Toggle();
            }
            else {
                Logger.LogError("UIManager not available");
            }
        }

        /// <summary>
        /// Check if any overlay is currently visible
        /// </summary>
        public bool IsOverlayVisible() {
            return uiManager?.Overlay?.IsVisible ?? false;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode) {
            if (loadSceneMode == LoadSceneMode.Single) {
                currentScene = scene;
                StopPreview();
            }

            if (scene.name == SceneKey.TitleScreen.ToString()) {
                Instance.riqPath = null;
                LaunchedFromRiqMenu = false; // Reset flag when returning to title
                StopPreview();

                TitleScript title = GameObject.Find("TitleScript").GetComponent<TitleScript>();
                titleScript = title;

                // Ensure RiqMenu tag is present in the title build text (avoid duplicates)
                string tag = "<color=#ff0000>R</color><color=#ff7f00>i</color><color=#ffff00>q</color><color=#00ff00>M</color><color=#0000ff>e</color><color=#4b0082>n</color><color=#9400d3>u</color>";
                if (title != null && title.buildTypeText != null) {
                    string currentText = title.buildTypeText.text ?? "";
                    if (!currentText.Contains(tag)) {
                        // If there's existing text, append with brackets; otherwise just show the tag
                        if (string.IsNullOrEmpty(currentText)) {
                            title.buildTypeText.text = tag;
                        } else {
                            title.buildTypeText.text = currentText + " (" + tag + ")";
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TitleScript), "Awake", [])]
        private static class TitleScriptAwakePatch {
            private static void Postfix(TitleScript __instance) {
                FieldInfo prop = __instance.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null) return;
                var options = (List<string>)prop.GetValue(__instance);
                if (options == null) return;
                // Insert once per TitleScript instance if missing
                if (!options.Contains("Custom Songs")) {
                    int insertIndex = Math.Min(1, options.Count);
                    options.Insert(insertIndex, "Custom Songs");
                    prop.SetValue(__instance, options);
                }
            }
        }

        [HarmonyPatch(typeof(TitleScript), "Update", [])]
        private static class TitleScriptUpdatePatch {
            private static bool Prefix(TitleScript __instance) {
                bool overlayVisible = Instance.IsOverlayVisible();
                if (overlayVisible) {
                    return false;
                }

                FieldInfo prop = __instance.GetType().GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance);
                int selected = (int)prop.GetValue(__instance);

                FieldInfo prop2 = __instance.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                List<string> options = (List<string>)prop2.GetValue(__instance);

                if (TempoInput.GetActionDown<global::Action>(global::Action.Confirm)) {
                    if (selected < options.Count && options[selected] == "Custom Songs") {
                        Instance.ToggleCustomSongsOverlay();
                        return false;
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(StageSelectScript), "Update", [])]
        private static class StageSelectScriptUpdatePatch {
            private static bool Prefix(StageSelectScript __instance) {
                bool overlayVisible = Instance.IsOverlayVisible();
                if (overlayVisible) {
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(LocalisationStatic), "_", [typeof(string)])]
        private static class LocalisationStaticPatch {
            private static bool Prefix(string text, ref LocalisedString __result) {
                if (text == "Custom Songs") {
                    __result = new LocalisedString("Custom Songs");
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(RiqLoader), "Awake", [])]
        private static class RiqLoaderAwakePatch {
            private static void Postfix() {
                RiqLoader.path = null;
            }
        }

        [HarmonyPatch(typeof(TempoSound), "IsPlaying", MethodType.Getter)]
        private static class TempoSoundIsPlayingPatch {
            private static bool Prefix(TempoSound __instance, ref bool __result) {
                // Get the private id field using reflection
                var idField = __instance.GetType().GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                if (idField != null) {
                    uint id = (uint)idField.GetValue(__instance);
                    // Check if the sound ID is invalid (uint.MaxValue = 4294967295)
                    if (id == uint.MaxValue) {
                        __result = false;
                        return false; // Skip original method
                    }
                }
                return true; // Continue with original method
            }
        }

        /// <summary>
        /// Patch Quitter.Quit to redirect to TitleScreen when exiting a RiqMenu-launched song.
        /// This handles the pause menu quit path.
        /// </summary>
        [HarmonyPatch(typeof(Quitter), "Quit")]
        private static class QuitterQuitPatch {
            private static void Prefix(ref SceneKey? quitScene) {
                // Only redirect if song was launched from RiqMenu and target is StageSelect
                if (LaunchedFromRiqMenu && quitScene.HasValue && quitScene.Value == SceneKey.StageSelect) {
                    quitScene = SceneKey.TitleScreen;
                    Debug.Log("[RiqMenu] Redirecting quit from StageSelect to TitleScreen");
                }
            }
        }

        /// <summary>
        /// Patch TempoSceneManager.LoadSceneAsync to redirect to TitleScreen when exiting a RiqMenu-launched song.
        /// This handles the postcard/results screen exit path.
        /// </summary>
        [HarmonyPatch(typeof(TempoSceneManager), "LoadSceneAsync", new Type[] { typeof(SceneKey), typeof(float) })]
        private static class TempoSceneManagerLoadSceneAsyncPatch {
            private static void Prefix(ref SceneKey scene) {
                // Only redirect if song was launched from RiqMenu and target is StageSelect
                if (LaunchedFromRiqMenu && scene == SceneKey.StageSelect) {
                    scene = SceneKey.TitleScreen;
                    Debug.Log("[RiqMenu] Redirecting LoadSceneAsync from StageSelect to TitleScreen");
                }
            }
        }

        /// <summary>
        /// Stop preview audio (delegates to AudioManager)
        /// </summary>
        static void StopPreview() {
            Instance.audioManager?.StopPreview();
        }
    }
}
