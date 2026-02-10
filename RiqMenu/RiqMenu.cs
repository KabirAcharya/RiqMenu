using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Concurrent;
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

            // Don't apply patches in the editor - they can cause issues
            if (!Application.isEditor) {
                harmony.PatchAll();
                Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            } else {
                Logger.LogWarning($"Plugin {PluginInfo.PLUGIN_GUID} loaded in editor - patches disabled");
            }
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

                // Reset transition state when scene fully loads
                _isTransitioning = false;

                // Reset gameplay state to prevent stale data
                _gameplayReady = false;
                _gameplayGraceTimer = 0f;
                JudgeHandleMissesPatch.ResetState();

                // Flush stale input events
                PendingHitEvent _discard;
                while (_pendingHits.TryDequeue(out _discard)) { }
                _pendingAutoRestartJudgement = NO_PENDING_JUDGEMENT;

                // Hide gameplay UI and stop music when in a menu scene
                // Use scene name directly since TempoSceneManager may not be updated yet
                bool isMenuScene = scene.name == SceneKey.TitleScreen.ToString() ||
                                   scene.name == SceneKey.StageSelect.ToString() ||
                                   scene.name == "StageSelectDemo" ||
                                   scene.name == "Postcard" ||
                                   scene.name == "Credits";
                if (isMenuScene) {
                    _accuracyBar?.Hide();
                    _progressBar?.Hide();
                    // Stop any playing music to prevent overlap
                    StopGameMusic();
                }
            }

            if (scene.name == SceneKey.TitleScreen.ToString()) {
                Instance.riqPath = null;
                LaunchedFromRiqMenu = false; // Reset flag when returning to title
                _lastLoadedSongPath = null; // Clear stored path
                StopPreview();

                TitleScript title = GameObject.Find("TitleScript").GetComponent<TitleScript>();
                titleScript = title;

                // Set RiqMenu version in the title build text (avoid duplicates)
                string tag = "<color=#ff0000>R</color><color=#ff7f00>i</color><color=#ffff00>q</color><color=#00ff00>M</color><color=#0000ff>e</color><color=#4b0082>n</color><color=#9400d3>u</color> v" + PluginInfo.PLUGIN_VERSION;
                if (title != null && title.buildTypeText != null) {
                    string currentText = title.buildTypeText.text ?? "";
                    // Check for our color-coded tag to avoid duplicates
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
                        // Only open if not already open - prevents input bleed-through
                        if (!Instance.IsOverlayVisible()) {
                            Instance.ToggleCustomSongsOverlay();
                        }
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

        // Store the last loaded song path for restart support
        private static string _lastLoadedSongPath = null;

        [HarmonyPatch(typeof(RiqLoader), "Awake", [])]
        private static class RiqLoaderAwakePatch {
            private static void Postfix() {
                // If we have a stored path (from restart), restore it
                if (!string.IsNullOrEmpty(_lastLoadedSongPath) && string.IsNullOrEmpty(RiqLoader.path)) {
                    RiqLoader.path = _lastLoadedSongPath;
                    Debug.Log($"[RiqMenu] Restored song path for restart: {_lastLoadedSongPath}");
                }
                // Store the current path for potential restart
                else if (!string.IsNullOrEmpty(RiqLoader.path)) {
                    _lastLoadedSongPath = RiqLoader.path;
                    Debug.Log($"[RiqMenu] Stored song path: {_lastLoadedSongPath}");
                }
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
        /// Patch Quitter.Quit to hide UI and handle redirects for RiqMenu songs
        /// </summary>
        [HarmonyPatch(typeof(Quitter), "Quit")]
        private static class QuitterQuitPatch {
            private static void Prefix(Quitter __instance, ref SceneKey? quitScene) {
                if (__instance.PreventQuit)
                    return;

                var activeSceneKey = TempoSceneManager.GetActiveSceneKey();
                Debug.Log($"[RiqMenu] Quitter.Quit called: quitScene={quitScene}, activeSceneKey={activeSceneKey}, LaunchedFromRiqMenu={LaunchedFromRiqMenu}, currentScene={currentScene.name}");

                // Never interfere with restart (going to RiqLoader)
                if (quitScene.HasValue && quitScene.Value == SceneKey.RiqLoader) {
                    Debug.Log("[RiqMenu] Going to RiqLoader - not redirecting (restart)");
                    return;
                }

                // Hide gameplay UI when quitting to menu scenes
                if (quitScene.HasValue &&
                    (quitScene.Value == SceneKey.StageSelect || quitScene.Value == SceneKey.TitleScreen)) {
                    _accuracyBar?.Hide();
                    _progressBar?.Hide();
                }

                // If launched from RiqMenu and going to StageSelect, redirect to TitleScreen
                if (LaunchedFromRiqMenu && quitScene.HasValue && quitScene.Value == SceneKey.StageSelect) {
                    quitScene = SceneKey.TitleScreen;
                    Debug.Log("[RiqMenu] Redirecting to TitleScreen");
                }
            }
        }

        /// <summary>
        /// Patch TempoSceneManager.LoadSceneAsync for debugging and redirect
        /// </summary>
        [HarmonyPatch(typeof(TempoSceneManager), "LoadSceneAsync", new Type[] { typeof(SceneKey), typeof(float) })]
        private static class TempoSceneManagerLoadSceneAsyncPatch {
            private static void Prefix(ref SceneKey scene) {
                Debug.Log($"[RiqMenu] LoadSceneAsync called: scene={scene}, LaunchedFromRiqMenu={LaunchedFromRiqMenu}, currentScene={currentScene.name}");

                // Never interfere with RiqLoader (restart)
                if (scene == SceneKey.RiqLoader) {
                    Debug.Log("[RiqMenu] Going to RiqLoader - not redirecting");
                    return;
                }

                // If launched from RiqMenu and going to StageSelect, redirect to TitleScreen
                if (LaunchedFromRiqMenu && scene == SceneKey.StageSelect) {
                    scene = SceneKey.TitleScreen;
                    Debug.Log("[RiqMenu] Redirecting LoadSceneAsync to TitleScreen");
                }
            }
        }

        /// <summary>
        /// Stop preview audio (delegates to AudioManager)
        /// </summary>
        static void StopPreview() {
            Instance.audioManager?.StopPreview();
        }

        /// <summary>
        /// Stop game music to prevent audio overlap when transitioning scenes
        /// </summary>
        static void StopGameMusic() {
            try {
                var jukebox = FindObjectOfType<JukeboxScript>();
                if (jukebox != null && jukebox.IsPlaying) {
                    jukebox.Stop();
                    Debug.Log("[RiqMenu] Stopped game music on scene transition");
                }
            } catch (Exception ex) {
                Debug.LogWarning($"[RiqMenu] Failed to stop game music: {ex.Message}");
            }
        }

        #region Gameplay UI
        private static AccuracyBar _accuracyBar;
        private static ProgressBar _progressBar;

        private struct PendingHitEvent {
            public float delta;
            public Judgement judgement;
        }
        private static readonly ConcurrentQueue<PendingHitEvent> _pendingHits = new ConcurrentQueue<PendingHitEvent>();
        private static volatile Judgement _pendingAutoRestartJudgement = (Judgement)(-99);
        private const Judgement NO_PENDING_JUDGEMENT = (Judgement)(-99);

        private static void InitializeAccuracyBar() {
            Debug.Log("[AccuracyBar] InitializeAccuracyBar called");
            if (_accuracyBar == null) {
                Debug.Log("[AccuracyBar] Creating AccuracyBar instance");
                var go = new GameObject("RiqMenu_AccuracyBar");
                _accuracyBar = go.AddComponent<AccuracyBar>();
                DontDestroyOnLoad(go);
            }
        }

        private static void InitializeProgressBar() {
            if (_progressBar == null) {
                Debug.Log("[ProgressBar] Creating ProgressBar instance");
                var go = new GameObject("RiqMenu_ProgressBar");
                _progressBar = go.AddComponent<ProgressBar>();
                DontDestroyOnLoad(go);
            }
        }

        [HarmonyPatch(typeof(Judge), "JudgeInput")]
        private static class JudgeInputPatch {
            private static void Postfix(ValueTuple<float, Judgement, float, bool> __result) {
                if (_isQuitting || _isTransitioning || IsInGameEditor()) return;
                if (!_gameplayReady) return; // Wait for grace period
                try {
                    float target = __result.Item1;
                    Judgement judgement = __result.Item2;
                    float delta = __result.Item3;
                    bool taken = __result.Item4;

                    // Check if there was actually a beat being judged
                    // target is NegativeInfinity or PositiveInfinity if no beat was in queue
                    bool hasBeat = !float.IsInfinity(target);

                    if (taken) {
                        _pendingHits.Enqueue(new PendingHitEvent { delta = delta, judgement = judgement });
                        _pendingAutoRestartJudgement = judgement;
                    }
                    else if (hasBeat && judgement == Judgement.Miss) {
                        _pendingHits.Enqueue(new PendingHitEvent { delta = delta, judgement = Judgement.Miss });
                        _pendingAutoRestartJudgement = Judgement.Miss;
                    }
                    // else: random button press with no beat nearby - ignore
                } catch (Exception) {
                    // Silently ignore errors - don't let RiqMenu crash the game
                }
            }
        }


        /// <summary>
        /// Patch HandleMisses to detect implicit misses (notes that pass without any input)
        /// </summary>
        [HarmonyPatch(typeof(Judge), "HandleMisses", new Type[] { typeof(BeatQueue), typeof(double), typeof(global::Action), typeof(Judge.OnMiss) })]
        private static class JudgeHandleMissesPatch {
            private static int _lastMissCount = -1;
            private const float MISS_DISPLAY_DELTA = 0.2f;

            public static void ResetState() {
                _lastMissCount = -1;
            }

            private static void Prefix(Judge __instance) {
                if (_isQuitting || _isTransitioning || IsInGameEditor()) return;
                try {
                    if (__instance == null || __instance.implicitJudgements == null) {
                        _lastMissCount = -1;
                        return;
                    }
                    _lastMissCount = __instance.implicitJudgements[Judgement.Miss];
                } catch (Exception) {
                    _lastMissCount = -1;
                }
            }

            // Runs on native input thread - no Unity calls allowed
            private static void Postfix(Judge __instance) {
                if (_isQuitting || _isTransitioning || IsInGameEditor()) return;
                try {
                    // Skip if not ready for gameplay yet (grace period)
                    if (!_gameplayReady) return;
                    if (__instance == null || __instance.implicitJudgements == null) return;
                    if (_lastMissCount < 0) return; // Prefix failed, skip this frame

                    int currentCount = __instance.implicitJudgements[Judgement.Miss];
                    int newMisses = currentCount - _lastMissCount;

                    if (newMisses > 0) {
                        for (int i = 0; i < newMisses; i++) {
                            _pendingHits.Enqueue(new PendingHitEvent { delta = MISS_DISPLAY_DELTA, judgement = Judgement.Miss });
                        }
                        _pendingAutoRestartJudgement = Judgement.Miss;
                    }
                } catch (Exception) {
                    // Silently ignore errors - don't let RiqMenu crash the game
                }
            }
        }

        // Auto-restart state
        private static bool _pendingRestart = false;
        private static float _restartDelay = 0f;
        private static bool _gameplayReady = false;
        private static float _gameplayGraceTimer = 0f;
        private const float GAMEPLAY_GRACE_PERIOD = 1.0f; // Wait 1 second after song starts
        private static bool _isQuitting = false; // Flag to disable patches during shutdown
        private static bool _isTransitioning = false; // Flag to disable patches during scene transitions

        /// <summary>
        /// Check if we're in the Bits & Bops level editor - disable gameplay patches there
        /// </summary>
        private static bool IsInGameEditor() {
            try {
                string sceneName = currentScene.name;
                if (string.IsNullOrEmpty(sceneName)) return false;
                // Check for editor scenes (case insensitive)
                return sceneName.Contains("Editor") || sceneName.Contains("editor");
            } catch {
                return false;
            }
        }

        private void OnApplicationQuit() {
            _isQuitting = true;
            _isTransitioning = true;
            _pendingRestart = false;
            _gameplayReady = false;
        }

        private static void CheckAutoRestart(Judgement judgement) {
            if (_isQuitting || _isTransitioning || IsInGameEditor()) return;

            try {
                var mode = RiqMenuSettings.AutoRestartMode;
                if (mode == AutoRestartMode.Off) return;

                // Don't auto-restart if already pending
                if (_pendingRestart) return;

                // Check scene - skip tutorials
                var sceneKey = TempoSceneManager.GetActiveSceneKey();
                if (TempoSceneManager.IsTutorialScene(sceneKey) || currentScene.name.Contains("Tutorial")) {
                    return;
                }

                // Check if this judgement triggers restart
                bool shouldRestart = false;
                if (mode == AutoRestartMode.OnMiss && (judgement == Judgement.Miss || judgement == Judgement.Bad)) {
                    // Only actual misses/bads trigger restart, not "Almost" judgements
                    shouldRestart = true;
                }
                else if (mode == AutoRestartMode.OnNonPerfect && judgement != Judgement.Perfect) {
                    // Any non-perfect judgement triggers restart (Hit, Almost, Bad, Miss)
                    shouldRestart = true;
                }

                if (shouldRestart) {
                    // Set transitioning flag IMMEDIATELY to prevent further UI updates
                    _isTransitioning = true;
                    _gameplayReady = false;
                    _pendingRestart = true;
                    _restartDelay = 0.3f; // Small delay before restart

                    // Hide gameplay UI immediately to prevent crashes during transition
                    _accuracyBar?.Hide();
                    _progressBar?.Hide();

                    Debug.Log($"[RiqMenu] Auto-restart triggered: {mode}, judgement={judgement}");
                }
            } catch (Exception) {
                // Silently ignore errors - don't let RiqMenu crash the game
            }
        }

        private void LateUpdate() {
            if (_isQuitting) return;
            try {
                // Drain hit events queued from the input thread
                PendingHitEvent hitEvent;
                while (_pendingHits.TryDequeue(out hitEvent)) {
                    if (_isTransitioning || !_gameplayReady) continue;
                    _accuracyBar?.RegisterHit(hitEvent.delta, hitEvent.judgement);
                }

                Judgement restartJudgement = _pendingAutoRestartJudgement;
                if (restartJudgement != NO_PENDING_JUDGEMENT) {
                    _pendingAutoRestartJudgement = NO_PENDING_JUDGEMENT;
                    CheckAutoRestart(restartJudgement);
                }

                // Handle gameplay grace period (only when not transitioning)
                if (!_isTransitioning && !_gameplayReady && _gameplayGraceTimer > 0) {
                    _gameplayGraceTimer -= Time.deltaTime;
                    if (_gameplayGraceTimer <= 0) {
                        _gameplayReady = true;
                        Debug.Log("[RiqMenu] Gameplay ready - auto-restart now active");
                    }
                }

                // Handle pending restart (must still run during transition to complete the restart)
                if (_pendingRestart && _restartDelay > 0) {
                    _restartDelay -= Time.deltaTime;
                    if (_restartDelay <= 0) {
                        _pendingRestart = false;
                        ExecuteRestart();
                    }
                }
            } catch {
                // Silently ignore errors during shutdown
            }
        }

        private void ExecuteRestart() {
            // Disable all patches immediately to prevent crashes during transition
            _isTransitioning = true;
            _gameplayReady = false;

            try {
                var sceneKey = TempoSceneManager.GetActiveSceneKey();

                // Skip if not in a valid gameplay scene
                if (!TempoSceneManager.IsGameScene(sceneKey) &&
                    sceneKey != SceneKey.RiqLoader &&
                    sceneKey != SceneKey.MixtapeCustom &&
                    !currentScene.name.Contains("Mixtape")) {
                    return;
                }

                // Hide accuracy bar
                _accuracyBar?.Hide();

                // Determine restart scene (same logic as PauseScript)
                SceneKey restartScene = sceneKey;
                if (sceneKey == SceneKey.MixtapeCustom) {
                    restartScene = SceneKey.RiqLoader;
                }

                // Call RestartStage - always safe to call, handles internal state
                JudgementScript.RestartStage();

                // Find Quitter in scene to use proper transition system
                var quitter = FindObjectOfType<Quitter>();
                if (quitter != null) {
                    // Temporarily allow quit and use the game's transition system
                    quitter.PreventQuit = false;
                    Debug.Log($"[RiqMenu] Restarting via Quitter: {restartScene}");
                    quitter.Quit(0.1f, 0.25f, 0.75f, restartScene);
                } else {
                    // Fallback to direct scene load if no Quitter found
                    Debug.Log($"[RiqMenu] Restarting via LoadSceneAsync (no Quitter): {restartScene}");
                    TempoSceneManager.LoadSceneAsync(restartScene, 0.1f);
                }
            } catch (Exception ex) {
                Debug.LogWarning($"[RiqMenu] ExecuteRestart failed: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(JukeboxScript), "Play")]
        private static class JukeboxScriptPlayPatch {
            private static void Postfix() {
                if (_isQuitting || IsInGameEditor()) return;

                // Reset transition state and pending restart when new song starts
                _isTransitioning = false;
                _pendingRestart = false;
                _restartDelay = 0f;

                // Only show UI during gameplay, not menu music
                var sceneKey = TempoSceneManager.GetActiveSceneKey();
                bool isGameplay = TempoSceneManager.IsGameScene(sceneKey) ||
                                  TempoSceneManager.IsTutorialScene(sceneKey) ||
                                  sceneKey == SceneKey.RiqLoader ||
                                  sceneKey == SceneKey.MixtapeCustom ||
                                  currentScene.name.Contains("Mixtape");

                // Only start grace period during gameplay scenes
                if (isGameplay) {
                    _gameplayReady = false;
                    _gameplayGraceTimer = GAMEPLAY_GRACE_PERIOD;
                } else {
                    // Not gameplay - disable auto-restart checking
                    _gameplayReady = false;
                    _gameplayGraceTimer = 0f;
                }

                if (!isGameplay)
                    return;

                // Show accuracy bar if enabled
                if (RiqMenuSettings.AccuracyBarEnabled) {
                    InitializeAccuracyBar();
                    _accuracyBar?.Show();
                    _accuracyBar?.ClearIndicators();
                }

                // Show progress bar if enabled
                if (RiqMenuSettings.ProgressBarEnabled) {
                    InitializeProgressBar();
                    _progressBar?.Show();
                }
            }
        }

        [HarmonyPatch(typeof(JudgementScript), "Play")]
        private static class JudgementScriptPlayPatch {
            private static void Prefix() {
                Debug.Log("[RiqMenu] JudgementScript.Play - hiding gameplay UI");
                _accuracyBar?.Hide();
                _progressBar?.Hide();
            }
        }

        #endregion
    }
}
