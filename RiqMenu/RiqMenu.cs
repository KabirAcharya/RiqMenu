using BepInEx;
using HarmonyLib;
using System;
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

        /// <summary>
        /// Legacy property for compatibility
        /// </summary>
        public CustomSong[] songList => songManager?.SongList ?? new CustomSong[0];

        public void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            AutoUpdater.CheckAndUpdate(Logger);

            systemManager = RiqMenuSystemManager.Instance;
            systemManager.InitializeSystems();

            if (songManager != null) {
                songManager.OnSongsLoaded += OnSongsLoaded;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;

            if (!Application.isEditor) {
                harmony.PatchAll();
                Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            } else {
                Logger.LogWarning($"Plugin {PluginInfo.PLUGIN_GUID} loaded in editor - patches disabled");
            }
        }

        private void Update() {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F1)) {
                HandleF1KeyPress();
            }
        }

        private void HandleF1KeyPress() {
            string sceneName = RiqMenuState.CurrentScene.name;
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

        public void ToggleCustomSongsOverlay() {
            if (uiManager != null && uiManager.Overlay != null) {
                uiManager.Overlay.Toggle();
            }
            else {
                Logger.LogError("UIManager not available");
            }
        }

        public bool IsOverlayVisible() {
            return uiManager?.Overlay?.IsVisible ?? false;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode) {
            if (loadSceneMode == LoadSceneMode.Single) {
                RiqMenuState.CurrentScene = scene;
                StopPreview();

                // Let gameplay system handle its own reset
                var sm = RiqMenuSystemManager.Instance;
                if (sm != null && sm.GameplaySystem != null) {
                    sm.GameplaySystem.OnSceneChanged(scene);
                }
            }

            if (scene.name == SceneKey.TitleScreen.ToString()) {
                RiqMenuState.LaunchedFromRiqMenu = false;
                RiqMenuState.LastLoadedSongPath = null;
                StopPreview();

                TitleScript title = GameObject.Find("TitleScript").GetComponent<TitleScript>();
                titleScript = title;

                string tag = "<color=#ff0000>R</color><color=#ff7f00>i</color><color=#ffff00>q</color><color=#00ff00>M</color><color=#0000ff>e</color><color=#4b0082>n</color><color=#9400d3>u</color> v" + PluginInfo.PLUGIN_VERSION;
                if (title != null && title.buildTypeText != null) {
                    string currentText = title.buildTypeText.text ?? "";
                    if (!currentText.Contains("<color=#ff0000>R</color>")) {
                        if (string.IsNullOrEmpty(currentText)) {
                            title.buildTypeText.text = tag;
                        } else {
                            title.buildTypeText.text = currentText + " (" + tag + ")";
                        }
                    }
                }
            }
        }

        static void StopPreview() {
            Instance.audioManager?.StopPreview();
        }
    }
}
