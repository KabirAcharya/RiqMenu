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

namespace RiqMenu
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class RiqMenuMain : BaseUnityPlugin {
        public string riqPath;

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
        private static bool riqMenuTextAdded = false;
        private static bool customSongsButtonAdded = false;

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
            if (uiManager?.SongsOverlay != null) {
                uiManager.SongsOverlay.Toggle();
            }
            else {
                Logger.LogError("UIManager or SongsOverlay not available");
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode) {
            if (loadSceneMode == LoadSceneMode.Single) {
                currentScene = scene;
                StopPreview();
            }

            if (scene.name == SceneKey.TitleScreen.ToString()) {
                Instance.riqPath = null;
                StopPreview();

                TitleScript title = GameObject.Find("TitleScript").GetComponent<TitleScript>();
                titleScript = title;

                // Only add RiqMenu text if it hasn't been added already
                if (!riqMenuTextAdded) {
                    title.buildTypeText.text += " (<color=#ff0000>R</color><color=#ff7f00>i</color><color=#ffff00>q</color><color=#00ff00>M</color><color=#0000ff>e</color><color=#4b0082>n</color><color=#9400d3>u</color>)";
                    riqMenuTextAdded = true;
                }
            }
        }

        [HarmonyPatch(typeof(TitleScript), "Awake", [])]
        private static class TitleScriptAwakePatch {
            private static void Postfix(TitleScript __instance) {
                if (!customSongsButtonAdded) {
                    FieldInfo prop = __instance.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                    List<string> options = (List<string>)prop.GetValue(__instance);

                    options.Insert(1, "Custom Songs");
                    prop.SetValue(__instance, options);
                    customSongsButtonAdded = true;
                }
            }
        }

        [HarmonyPatch(typeof(TitleScript), "Update", [])]
        private static class TitleScriptUpdatePatch {
            private static bool Prefix(TitleScript __instance) {
                bool overlayVisible = Instance.uiManager?.SongsOverlay?.IsVisible ?? false;
                if (overlayVisible) {
                    return false;
                }

                FieldInfo prop = __instance.GetType().GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance);
                int selected = (int)prop.GetValue(__instance);

                FieldInfo prop2 = __instance.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                List<string> options = (List<string>)prop2.GetValue(__instance);

                if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.Space)) {
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
                bool overlayVisible = Instance.uiManager?.SongsOverlay?.IsVisible ?? false;
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
        /// Stop preview audio (delegates to AudioManager)
        /// </summary>
        static void StopPreview() {
            Instance.audioManager?.StopPreview();
        }
    }
}
