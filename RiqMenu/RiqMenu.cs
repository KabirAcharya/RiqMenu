using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace RiqMenu {
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class RiqMenuMain : BaseUnityPlugin
    {
        private bool loadCustomSongs = false;
        public string[] fileNames;
        public string riqPath;
        private int totalRows = 0;
        private int currentStartRow = 0;
        private bool isScrolling = false;
        private const int ROWS_PER_VIEW = 4;
        private const int SONGS_PER_ROW = 4; // Only use first 4 columns, leave rightmost for unlockables

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
        private static StageSelectScript stageSelectScript;

        private static CustomSong[] songList = [];
        private List<CustomSong> downloadableSongList = new List<CustomSong>();

        private static GameObject previewSourceGO;
        private static TempoSound previewSource;
        private static int currentlyLoadingPreview = -1;
        private static int targetPreviewIndex = -1;
        private static readonly object loadingLock = new object();
        private static volatile bool cancelCurrentLoading = false;
        private static string audioCacheDir;
        private static bool cacheInitialized = false;
        private static bool showCacheProgress = false;
        private static int totalFilesToCache = 0;
        private static int filesProcessed = 0;
        private static string currentProcessingFile = "";

        public Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);

        public static Scene currentScene;
        
        private void OnGUI() {
            if (showCacheProgress) {
                Logger.LogInfo($"OnGUI: Drawing cache progress screen - showCacheProgress: {showCacheProgress}");
                DrawCacheProgressScreen();
            }
        }
        
        private void DrawCacheProgressScreen() {
            // Full screen black overlay
            GUI.color = Color.black;
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.color = Color.white;
            
            // Calculate center position
            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;
            
            // Main container
            float boxWidth = 400f;
            float boxHeight = 150f;
            Rect boxRect = new Rect(centerX - boxWidth/2, centerY - boxHeight/2, boxWidth, boxHeight);
            
            GUI.color = Color.gray;
            GUI.Box(boxRect, "");
            GUI.color = Color.white;
            
            // Title
            GUI.Label(new Rect(boxRect.x + 10, boxRect.y + 10, boxRect.width - 20, 30), 
                     $"<size=16><color=white><b>RiqMenu - Building Audio Cache</b></color></size>");
            
            // Progress bar background
            float progressBarY = boxRect.y + 50;
            float progressBarWidth = boxRect.width - 20;
            float progressBarHeight = 20;
            Rect progressBgRect = new Rect(boxRect.x + 10, progressBarY, progressBarWidth, progressBarHeight);
            GUI.color = Color.black;
            GUI.Box(progressBgRect, "");
            
            // Progress bar fill
            if (totalFilesToCache > 0) {
                float progress = (float)filesProcessed / totalFilesToCache;
                float fillWidth = progressBarWidth * progress;
                Rect progressFillRect = new Rect(boxRect.x + 10, progressBarY, fillWidth, progressBarHeight);
                GUI.color = Color.green;
                GUI.Box(progressFillRect, "");
            }
            
            GUI.color = Color.white;
            
            // Progress text
            string progressText = totalFilesToCache > 0 ? 
                $"Processing: {filesProcessed}/{totalFilesToCache}" : 
                "Initializing...";
            GUI.Label(new Rect(boxRect.x + 10, progressBarY + 25, boxRect.width - 20, 20), 
                     $"<color=white>{progressText}</color>");
            
            // Current file
            if (!string.IsNullOrEmpty(currentProcessingFile)) {
                string displayName = currentProcessingFile.Length > 40 ? 
                    currentProcessingFile.Substring(0, 37) + "..." : 
                    currentProcessingFile;
                GUI.Label(new Rect(boxRect.x + 10, progressBarY + 50, boxRect.width - 20, 20), 
                         $"<color=yellow>{displayName}</color>");
            }
            
            // Info text
            GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 30, boxRect.width - 20, 20), 
                     "<color=gray><size=12>This will only happen once for new songs</size></color>");
        }

        public static T[] GetSubArray<T>(T[] data, int index, int length) {
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        public void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            LoadLocalSongs();
            SceneManager.sceneLoaded += OnSceneLoaded;
            harmony.PatchAll();
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        private void OnDestroy() {
            // Clean up cache on shutdown
            CleanupAudioCache();
        }

        private void CleanupAudioCache() {
            if (Directory.Exists(audioCacheDir)) {
                try {
                    // Delete cache files older than 7 days
                    var files = Directory.GetFiles(audioCacheDir);
                    var cutoffDate = DateTime.Now.AddDays(-7);
                    
                    foreach (var file in files) {
                        if (File.GetCreationTime(file) < cutoffDate) {
                            File.Delete(file);
                        }
                    }
                    
                    Logger.LogInfo($"Cleaned up old audio cache files");
                } catch (System.Exception ex) {
                    Logger.LogWarning($"Failed to clean audio cache: {ex.Message}");
                }
            }
        }

        public void LoadLocalSongs() {
            string path = Path.Combine(Application.dataPath, "StreamingAssets");
            Logger.LogInfo($"Scanning for custom songs in: {path}");
            
            // Initialize audio cache directory
            audioCacheDir = Path.Combine(Application.temporaryCachePath, "RiqMenu_AudioCache");
            if (!Directory.Exists(audioCacheDir)) {
                Directory.CreateDirectory(audioCacheDir);
            }
            
            if (!Directory.Exists(path)) {
                Logger.LogWarning($"StreamingAssets directory not found: {path}");
                fileNames = new string[0];
                songList = new CustomSong[0];
                cacheInitialized = true;
                return;
            }

            string[] excludeFiles = {
                "flipper_snapper.riq",
                "hammer_time.riq", 
                "bits_and_bops.riq",
                "meet_and_tweet.riq"
            };
            
            fileNames = Directory.GetFiles(path)
                .Where(file => file.EndsWith(".riq", StringComparison.OrdinalIgnoreCase) && 
                              !excludeFiles.Contains(Path.GetFileName(file)))
                .OrderBy(file => Path.GetFileNameWithoutExtension(file))
                .ToArray();

            totalRows = (int)Math.Ceiling((double)fileNames.Length / SONGS_PER_ROW);

            songList = new CustomSong[fileNames.Length];
            for (int i = 0; i < fileNames.Length; i++) {
                songList[i] = new CustomSong();
                songList[i].riq = fileNames[i];
                songList[i].SongTitle = Path.GetFileNameWithoutExtension(fileNames[i]);
                Logger.LogInfo($"Found custom song: {songList[i].SongTitle}");
            }
            
            Logger.LogInfo($"Loaded {fileNames.Length} custom songs across {totalRows} rows");
            
            // Check which files need to be cached
            CheckCacheStatus();
        }
        
        private void CheckCacheStatus() {
            List<CustomSong> filesToCache = new List<CustomSong>();
            
            foreach (var song in songList) {
                string cacheFileName = Path.GetFileNameWithoutExtension(song.riq) + ".audio";
                string cachedPath = Path.Combine(audioCacheDir, cacheFileName);
                
                if (!File.Exists(cachedPath)) {
                    filesToCache.Add(song);
                }
            }
            
            totalFilesToCache = filesToCache.Count;
            Logger.LogInfo($"CheckCacheStatus: Found {totalFilesToCache} files to cache out of {songList.Length} total songs");
            
            if (totalFilesToCache > 0) {
                Logger.LogInfo($"Found {totalFilesToCache} new songs to cache - showing progress screen");
                filesProcessed = 0;
                showCacheProgress = true;
                Logger.LogInfo($"showCacheProgress set to: {showCacheProgress}");
                
                // Force a small delay to ensure the loading screen appears
                StartCoroutine(DelayedExtractAudioFiles(filesToCache));
            } else {
                Logger.LogInfo("All songs already cached - showing loading screen for memory preload");
                // Show loading screen even when no extraction is needed
                showCacheProgress = true;
                totalFilesToCache = songList.Length; // Set for memory loading progress
                filesProcessed = 0;
                currentProcessingFile = "Loading audio into memory...";
                
                // Force a small delay to ensure the loading screen appears
                StartCoroutine(DelayedMemoryPreload());
            }
        }
        
        private IEnumerator WaitForCacheAndSetup() {
            Logger.LogInfo("Waiting for cache initialization to complete...");
            
            // Wait for cache to be initialized
            while (!cacheInitialized) {
                yield return new WaitForSeconds(0.1f);
            }
            
            Logger.LogInfo("Cache initialization complete, setting up custom stage select");
            SetupCustomStageSelect(currentStartRow, true);
        }
        
        private IEnumerator DelayedExtractAudioFiles(List<CustomSong> filesToCache) {
            Logger.LogInfo($"DelayedExtractAudioFiles: Starting with {filesToCache.Count} files");
            // Small delay to ensure loading screen appears
            yield return new WaitForSeconds(0.1f);
            Logger.LogInfo($"DelayedExtractAudioFiles: After delay, calling ExtractAudioFilesWithProgress");
            yield return ExtractAudioFilesWithProgress(filesToCache);
        }
        
        private IEnumerator DelayedMemoryPreload() {
            Logger.LogInfo("DelayedMemoryPreload: Starting memory preload");
            // Small delay to ensure loading screen appears
            yield return new WaitForSeconds(0.1f);
            Logger.LogInfo("DelayedMemoryPreload: After delay, calling PreloadAllAudioIntoMemoryWithProgress");
            yield return StartCoroutine(PreloadAllAudioIntoMemoryWithProgress());
            
            // Complete loading screen
            showCacheProgress = false;
            cacheInitialized = true;
            Logger.LogInfo("Memory preloading completed");
        }
        
        private IEnumerator ExtractAudioFilesWithProgress(List<CustomSong> filesToCache) {
            Logger.LogInfo($"Starting audio extraction for {filesToCache.Count} new songs");
            
            bool extractionComplete = false;
            System.Exception extractionError = null;
            
            // Extract audio files on background thread with progress tracking
            ThreadPool.QueueUserWorkItem((_) => {
                try {
                    for (int i = 0; i < filesToCache.Count; i++) {
                        try {
                            CustomSong song = filesToCache[i];
                            currentProcessingFile = song.SongTitle;
                            
                            string cacheFileName = Path.GetFileNameWithoutExtension(song.riq) + ".audio";
                            string cachedPath = Path.Combine(audioCacheDir, cacheFileName);
                            
                            // Skip if file already exists (safety check)
                            if (File.Exists(cachedPath)) {
                                Logger.LogInfo($"Skipping already cached: {song.SongTitle}");
                                filesProcessed++;
                                continue;
                            }
                            
                            // Extract audio from ZIP with timeout handling
                            using (FileStream fileStream = File.Open(song.riq, FileMode.Open)) {
                                using (ZipArchive zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read)) {
                                    ZipArchiveEntry entry = FindSong(zipArchive, out ZipArchiveEntry e) ? e : zipArchive.GetEntry("song.bin");
                                    
                                    if (entry != null) {
                                        using (Stream stream = entry.Open()) {
                                            using (FileStream output = File.Create(cachedPath)) {
                                                stream.CopyTo(output);
                                            }
                                        }
                                        Logger.LogInfo($"Cached audio for {song.SongTitle}");
                                    } else {
                                        Logger.LogWarning($"No audio entry found for {song.SongTitle}");
                                    }
                                }
                            }
                            
                            filesProcessed++;
                            
                        } catch (System.Exception ex) {
                            Logger.LogError($"Failed to cache audio for {filesToCache[i].SongTitle}: {ex.Message}");
                            filesProcessed++;
                        }
                    }
                } catch (System.Exception ex) {
                    extractionError = ex;
                    Logger.LogError($"Critical error in audio extraction: {ex.Message}");
                } finally {
                    extractionComplete = true;
                    Logger.LogInfo("Audio extraction thread completed");
                }
            });
            
            // Wait for extraction to complete with timeout
            float timeoutSeconds = 60f; // 60 second timeout
            float startTime = Time.time;
            
            while (!extractionComplete) {
                if (Time.time - startTime > timeoutSeconds) {
                    Logger.LogError("Audio extraction timed out - forcing completion");
                    break;
                }
                yield return new WaitForSeconds(0.1f);
            }
            
            if (extractionError != null) {
                Logger.LogError($"Cache extraction completed with errors: {extractionError.Message}");
            } else {
                Logger.LogInfo("Cache extraction completed successfully - now preloading all audio into memory");
            }
            
            // Now preload ALL audio (both existing and newly cached) into memory during the loading screen
            currentProcessingFile = "Loading audio into memory...";
            totalFilesToCache = songList.Length; // Reset counter for memory loading phase
            filesProcessed = 0;
            
            yield return StartCoroutine(PreloadAllAudioIntoMemoryWithProgress());
            
            // Complete loading screen
            showCacheProgress = false;
            cacheInitialized = true;
            Logger.LogInfo("Cache initialization and memory preloading completed");
        }

        

        private static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode) {
            if (loadSceneMode == LoadSceneMode.Single) {
                currentScene = scene;
                
                // Always stop preview audio when loading a new scene
                StopPreview();
                Instance.Logger.LogInfo($"Stopped preview audio for scene transition to: {scene.name}");
            }

            if (currentScene.name == "StageSelectDemo") {
                Instance.Logger.LogInfo($"StageSelectDemo scene loaded, loadCustomSongs: {Instance.loadCustomSongs}");
                if (Instance.loadCustomSongs) {
                    Instance.riqPath = null;
                    stageSelectScript = GameObject.Find("StageSelect").GetComponent<StageSelectScript>();
                    
                    // Wait for cache initialization if needed
                    if (!cacheInitialized) {
                        Instance.StartCoroutine(Instance.WaitForCacheAndSetup());
                    } else {
                        Instance.SetupCustomStageSelect(Instance.currentStartRow, true);
                    }
                } else {
                    // Stop any playing preview audio when leaving custom songs
                    StopPreview();
                    
                    // Aggressively destroy the preview GameObject when leaving custom mode
                    if (previewSourceGO != null) {
                        Destroy(previewSourceGO);
                        previewSourceGO = null;
                        Instance.Logger.LogInfo("Destroyed preview GameObject when leaving custom songs");
                    }
                    
                    // Also restart the stage select music since we're leaving custom mode
                    if (stageSelectScript != null && stageSelectScript.jukebox != null && stageSelectScript.music != null) {
                        stageSelectScript.jukebox.Stop();
                        stageSelectScript.jukebox.Schedule(stageSelectScript.music, 16f);
                        stageSelectScript.jukebox.Play();
                        Instance.Logger.LogInfo("Restarted stage select music after leaving custom songs");
                    }
                }
            }

            if (scene.name == SceneKey.TitleScreen.ToString()) {
                Instance.riqPath = null;
                
                // Stop any preview audio when going to title screen
                StopPreview();
                
                TitleScript title = GameObject.Find("TitleScript").GetComponent<TitleScript>();
                titleScript = title;
                title.buildTypeText.text += " (<color=#ff0000>R</color><color=#ff7f00>i</color><color=#ffff00>q</color><color=#00ff00>M</color><color=#0000ff>e</color><color=#4b0082>n</color><color=#9400d3>u</color>)";
            }
        }

        public void SetupCustomStageSelect(int startRow, bool reset) {
            Logger.LogInfo($"Setting up custom stage select from row {startRow}");
            if (stageSelectScript != null) {
                foreach (GameObject gameobject in GameObject.FindObjectsOfType<GameObject>()) {
                    if (gameobject.name == "level_menu_cabinet_cover") {
                        GameObject.Destroy(gameobject);
                    }
                }

                if (!reset) {
                    // This method should only be called for initial setup with reset=true
                    // Scrolling animations handle content updates directly
                    Logger.LogWarning("SetupCustomStageSelect called with reset=false - this should not happen during scrolling");
                    return;
                }

                LevelCardScript baseCard = stageSelectScript.levelCards[3];
                baseCard.gameObject.SetActive(true);
                SpriteRenderer baseSpriteRenderer = baseCard.GetComponent<SpriteRenderer>();
                Sprite baseSprite = baseSpriteRenderer.sprite;
                TMPro.TextMeshProUGUI style = baseCard.gameObject.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                style.fontSize = 30;

                int resetStartIndex = startRow * SONGS_PER_ROW;
                
                // Set up only the first 4 columns of each row (skip rightmost column)
                for (int row = 0; row < ROWS_PER_VIEW; row++) {
                    for (int col = 0; col < SONGS_PER_ROW; col++) {
                        int cardIndex = row * 5 + col; // Unity uses 5 cards per row
                        int songIndex = resetStartIndex + row * SONGS_PER_ROW + col;
                        
                        stageSelectScript.levelCards[cardIndex].gameObject.SetActive(true);
                        stageSelectScript.levelCards[cardIndex].Unlock();
                        stageSelectScript.levelCards[cardIndex].scene = SceneKey.RiqLoader;
                        stageSelectScript.levelCards[cardIndex].promoLink = PromoLink.None;
                        stageSelectScript.levelCards[cardIndex].activatesPromoLinks = false;
                        stageSelectScript.levelCards[cardIndex].gameObject.GetComponent<SpriteRenderer>().sprite = baseSprite;
                        
                        foreach (Transform child in stageSelectScript.levelCards[cardIndex].gameObject.transform) {
                            if (child.name != "Canvas") {
                                GameObject.Destroy(child.gameObject);
                                continue;
                            }
                            TMPro.TextMeshProUGUI TMPUGUI = child.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                            TMPUGUI.fontSize = style.fontSize;
                            TMPUGUI.font = style.font;
                            TMPUGUI.fontMaterial = style.fontMaterial;
                            TMPUGUI.color = style.color;
                            if (songIndex < songList.Length) {
                                TMPUGUI.text = Path.GetFileNameWithoutExtension(fileNames[songIndex]);
                            }
                        }

                        if (songIndex >= songList.Length) {
                            stageSelectScript.levelCards[cardIndex].gameObject.SetActive(false);
                        }
                    }
                    // Skip position 4 in each row (positions 4, 9, 14, 19) - leave them for unlockables
                }
                
                // Trigger initial preview for the first visible song
                if (songList.Length > 0) {
                    StartCoroutine(TriggerInitialPreview(startRow));
                }
            }
        }
        
        private IEnumerator TriggerInitialPreview(int startRow) {
            // Wait a bit for the setup to complete
            yield return new WaitForSeconds(0.5f);
            
            // Try to trigger preview for the currently selected song
            try {
                // Get the current selection from stageSelectScript using reflection
                var currentXField = typeof(StageSelectScript).GetField("currentX", BindingFlags.NonPublic | BindingFlags.Instance);
                var currentYField = typeof(StageSelectScript).GetField("currentY", BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (currentXField != null && currentYField != null && stageSelectScript != null) {
                    int currentX = (int)currentXField.GetValue(stageSelectScript);
                    int currentY = (int)currentYField.GetValue(stageSelectScript);
                    
                    Logger.LogInfo($"Triggering initial preview for position ({currentX}, {currentY}) at row {startRow}");
                    TryPlayPreview(currentX, currentY);
                }
            } catch (System.Exception ex) {
                Logger.LogWarning($"Failed to trigger initial preview: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(TitleScript), "Awake", [])]
        private static class TitleScriptAwakePatch {
            private static void Postfix(TitleScript __instance) {
                FieldInfo prop = __instance.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                List<string> options = (List<string>)prop.GetValue(__instance);
                
                // Only add "Custom Songs" if it doesn't already exist
                if (!options.Contains("Custom Songs")) {
                    options.Insert(1, "Custom Songs");
                    prop.SetValue(__instance, options);
                }
            }
        }

        [HarmonyPatch(typeof(TitleScript), "Update", [])]
        private static class TitleScriptUpdatePatch {
            private static bool Prefix(TitleScript __instance) {
                // Block all input during cache initialization
                if (showCacheProgress) {
                    return false; // Skip original method entirely
                }
                
                FieldInfo prop = __instance.GetType().GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance);
                int selected = (int)prop.GetValue(__instance);

                FieldInfo prop2 = __instance.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                List<string> options = (List<string>)prop2.GetValue(__instance);

                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) {
                    if (selected < options.Count && options[selected] == "Custom Songs") {
                        Instance.loadCustomSongs = true;
                        __instance.StartCoroutine(__instance.Play(SceneKey.StageSelect));
                        return false; // Skip original method
                    } else if (selected < options.Count && options[selected] == "Play") {
                        // Reset the flag when normal Play is selected
                        Instance.loadCustomSongs = false;
                        // Let the original method handle normal play
                    }
                }
                return true; // Continue with original method
            }
        }

        

        [HarmonyPatch(typeof(LocalisationStatic), "_", [typeof(string)])]
        private static class LocalisationStaticPatch {
            private static bool Prefix(string text, ref LocalisedString __result) {
                if (text == "Custom Songs") {
                    __result = new LocalisedString("Custom Songs");
                    return false; // Skip original method
                }
                return true; // Continue with original method
            }
        }

        [HarmonyPatch(typeof(LevelCardScript), "Confirm", [])]
        private static class CardConfirmPatch {
            private static void Postfix(LevelCardScript __instance) {
                MixtapeLoaderCustom.autoplay = false;
                if (Input.GetKey(KeyCode.P)) {
                    MixtapeLoaderCustom.autoplay = true;
                }
                if (Instance.loadCustomSongs) {
                    if (stageSelectScript == null) {
                        stageSelectScript = GameObject.Find("StageSelect").GetComponent<StageSelectScript>();
                        if (stageSelectScript == null) {
                            Instance.Logger.LogError("StageSelectScript is null in CardConfirmPatch!");
                            return;
                        }
                    }
                    int thisIndex = Array.IndexOf(stageSelectScript.levelCards, __instance);
                    Instance.riqPath = Instance.fileNames[Instance.currentStartRow * SONGS_PER_ROW + thisIndex];
                    RiqLoader.path = Instance.riqPath;
                }
            }
        }

        [HarmonyPatch(typeof(LevelCardScript), "Select", [])]
        private static class CardSelectPatch {
            private static void Postfix(LevelCardScript __instance) {
                if (Instance.loadCustomSongs) {
                    if (stageSelectScript == null) {
                        stageSelectScript = GameObject.Find("StageSelect").GetComponent<StageSelectScript>();
                        if (stageSelectScript == null) {
                            Instance.Logger.LogError("StageSelectScript is null in CardSelectPatch!");
                            return;
                        }
                    }
                    int thisIndex = Array.IndexOf(stageSelectScript.levelCards, __instance);
                    TryPlayPreview(thisIndex % 5, thisIndex / 5);
                }
            }
        }

        [HarmonyPatch(typeof(RiqLoader), "Awake", [])]
        private static class RiqLoaderAwakePatch {
            private static void Postfix() {
                RiqLoader.path = null;
            }
        }

        [HarmonyPatch(typeof(LevelCardScript), "Selectable", MethodType.Getter)]
        private static class LevelCardSelectablePatch {
            private static void Postfix(LevelCardScript __instance, ref bool __result) {
                __result &= __instance.gameObject.activeSelf;
            }
        }

        [HarmonyPatch(typeof(LevelCardScript), "GetJudgement", [])]
        private static class LevelCardGetJudgementPatch {
            private static bool Prefix(LevelCardScript __instance, ref LocalisedString? __result) {
                // Check if this is a custom song card (has RiqLoader scene)
                if (__instance.scene == SceneKey.RiqLoader) {
                    // For custom songs, return null (no judgement)
                    __result = null;
                    return false; // Skip original method
                }
                // Check if sticker or stickers are null to prevent NullReferenceException
                if (__instance.sticker == null || __instance.stickers == null) {
                    __result = null;
                    return false; // Skip original method
                }
                return true; // Continue with original method
            }
        }

        [HarmonyPatch(typeof(StageSelectScript), "Awake", [])]
        private static class StageSelectScriptAwakePatch {
            private static bool Prefix(StageSelectScript __instance) {
                if (Instance.loadCustomSongs) {
                    // Completely skip the music scheduling part of Awake when in custom songs mode
                    Instance.Logger.LogInfo("Skipping StageSelectScript music scheduling for custom songs mode");
                    
                    // We still need to do the non-music parts of Awake, so let's do them manually using reflection
                    if (__instance.board != null) {
                        __instance.board.gameObject.SetActive(true);
                    }
                    if (__instance.wishlistBoard != null) {
                        __instance.wishlistBoard.gameObject.SetActive(true);
                    }
                    
                    // Use reflection to access private LoadPosition method and fields
                    try {
                        var loadPositionMethod = typeof(StageSelectScript).GetMethod("LoadPosition", BindingFlags.NonPublic | BindingFlags.Static);
                        var currentXField = typeof(StageSelectScript).GetField("currentX", BindingFlags.NonPublic | BindingFlags.Instance);
                        var currentYField = typeof(StageSelectScript).GetField("currentY", BindingFlags.NonPublic | BindingFlags.Instance);
                        
                        if (loadPositionMethod != null && currentXField != null && currentYField != null) {
                            var position = loadPositionMethod.Invoke(null, null);
                            var positionTuple = (ValueTuple<int, int>)position;
                            currentXField.SetValue(__instance, positionTuple.Item1);
                            currentYField.SetValue(__instance, positionTuple.Item2);
                            Instance.Logger.LogInfo("Set stage select position using reflection");
                        }
                    } catch (System.Exception ex) {
                        Instance.Logger.LogWarning($"Failed to set stage select position: {ex.Message}");
                    }
                    
                    // Skip the music scheduling line: this.jukebox.Schedule(this.music, 16f);
                    
                    return false; // Skip the original Awake method
                }
                return true; // Continue with original method for normal mode
            }
        }

        [HarmonyPatch(typeof(JukeboxScript), "Schedule", [typeof(TempoSound), typeof(float)])]
        private static class JukeboxSchedulePatch {
            private static bool Prefix(JukeboxScript __instance, TempoSound sound, float beat) {
                if (Instance.loadCustomSongs && stageSelectScript != null && __instance == stageSelectScript.jukebox) {
                    // Block scheduling of stage select music when in custom songs mode
                    Instance.Logger.LogInfo("Blocked JukeboxScript.Schedule in custom songs mode");
                    return false; // Skip the original method
                }
                return true; // Continue with original method
            }
        }

        [HarmonyPatch(typeof(JukeboxScript), "Play", [])]
        private static class JukeboxPlayPatch {
            private static bool Prefix(JukeboxScript __instance) {
                if (Instance.loadCustomSongs && stageSelectScript != null && __instance == stageSelectScript.jukebox) {
                    // Block jukebox play when in custom songs mode
                    Instance.Logger.LogInfo("Blocked JukeboxScript.Play in custom songs mode");
                    return false; // Skip the original method
                }
                return true; // Continue with original method
            }
        }

        IEnumerator DownArrowCheck(FieldInfo propX, FieldInfo propY, int currentX, int currentY) {
            // Scroll down by one row if not at the bottom
            Logger.LogInfo($"DownArrow START: currentStartRow={Instance.currentStartRow}, totalRows={Instance.totalRows}, songListLength={songList.Length}, isScrolling={Instance.isScrolling}");
            
            if (Instance.isScrolling) {
                Logger.LogInfo("Already scrolling - ignoring duplicate scroll request");
                yield break;
            }
            
            Logger.LogInfo($"DownArrow check: {Instance.currentStartRow} + {ROWS_PER_VIEW} = {Instance.currentStartRow + ROWS_PER_VIEW} < {Instance.totalRows} = {Instance.currentStartRow + ROWS_PER_VIEW < Instance.totalRows}");
            
            if (Instance.currentStartRow + ROWS_PER_VIEW < Instance.totalRows) {
                Instance.isScrolling = true;
                Logger.LogInfo($"About to scroll from row {Instance.currentStartRow} to {Instance.currentStartRow + 1}");
                stageSelectScript.levelCards[currentY * 5 + currentX].Deselect();
                Instance.currentStartRow++;
                Logger.LogInfo($"Incremented currentStartRow to {Instance.currentStartRow}");
                yield return Instance.StartCoroutine(Instance.AnimateScrollDown());
                Logger.LogInfo($"Animation complete, selecting top row column {currentX}");
                // Keep same column (currentX), move to top row (0)
                stageSelectScript.levelCards[0 * 5 + currentX].Select();
                propX.SetValue(stageSelectScript, currentX);
                propY.SetValue(stageSelectScript, 0);
                Instance.isScrolling = false;
                Logger.LogInfo($"DownArrow COMPLETE: currentStartRow={Instance.currentStartRow}, selector at (0,{currentX})");
            } else {
                Logger.LogInfo("Cannot scroll down - at bottom");
            }
        }

        IEnumerator UpArrowCheck(FieldInfo propX, FieldInfo propY, int currentX, int currentY) {
            // Scroll up by one row if not at the top
            Logger.LogInfo($"UpArrow START: currentStartRow={Instance.currentStartRow}, can scroll up: {Instance.currentStartRow > 0}, isScrolling={Instance.isScrolling}");
            
            if (Instance.isScrolling) {
                Logger.LogInfo("Already scrolling - ignoring duplicate scroll request");
                yield break;
            }
            
            if (Instance.currentStartRow > 0) {
                Instance.isScrolling = true;
                Logger.LogInfo($"About to scroll from row {Instance.currentStartRow} to {Instance.currentStartRow - 1}");
                stageSelectScript.levelCards[currentY * 5 + currentX].Deselect();
                Instance.currentStartRow--;
                Logger.LogInfo($"Decremented currentStartRow to {Instance.currentStartRow}");
                yield return Instance.StartCoroutine(Instance.AnimateScrollUp());
                Logger.LogInfo($"Animation complete, selecting bottom row column {currentX}");
                // Keep same column (currentX), move to bottom row (3)
                stageSelectScript.levelCards[3 * 5 + currentX].Select();
                propX.SetValue(stageSelectScript, currentX);
                propY.SetValue(stageSelectScript, 3);
                Instance.isScrolling = false;
                Logger.LogInfo($"UpArrow COMPLETE: currentStartRow={Instance.currentStartRow}, selector at (3,{currentX})");
            } else {
                Logger.LogInfo("Cannot scroll up - at top");
            }
        }

        [HarmonyPatch(typeof(StageSelectScript), "OnDirection", [typeof(int), typeof(int)])]
        private static class StageSelectOnDirectionPatch {
            private static bool Prefix(StageSelectScript __instance, int x, int y) {
                if (!Instance.loadCustomSongs) return true;
                
                // Handle keyboard shortcuts
                if (Input.GetKeyDown(KeyCode.Escape)) {
                    StopPreview();
                    cancelCurrentLoading = true;
                    return true;
                }
                
                FieldInfo propX = __instance.GetType().GetField("currentX", BindingFlags.NonPublic | BindingFlags.Instance);
                int currentX = (int)propX.GetValue(__instance);
                FieldInfo propY = __instance.GetType().GetField("currentY", BindingFlags.NonPublic | BindingFlags.Instance);
                int currentY = (int)propY.GetValue(__instance);
                
                Instance.Logger.LogInfo($"OnDirection called: x={x}, y={y}, currentPos=({currentX},{currentY})");
                
                if (y == 1 && currentY == 3) { // At bottom row
                    Instance.Logger.LogInfo("Bottom row detected - calling DownArrowCheck");
                    Instance.StartCoroutine(Instance.DownArrowCheck(propX, propY, currentX, currentY));
                    return false;
                }
                if (y == -1 && currentY == 0) { // At top row
                    Instance.Logger.LogInfo("Top row detected - calling UpArrowCheck");
                    Instance.StartCoroutine(Instance.UpArrowCheck(propX, propY, currentX, currentY));
                    return false;
                }

                Instance.Logger.LogInfo("Normal direction handling - not scrolling");
                return true;
            }
        }

        static void TryPlayPreview(int currentX, int currentY) {
            int idx = Instance.currentStartRow * SONGS_PER_ROW + currentY * SONGS_PER_ROW + currentX;
            if (idx < 0 || idx >= songList.Length) {
                Instance.Logger.LogWarning($"Selected card outside bounds at {idx}/{songList.Length}");
                return;
            }

            CustomSong song = songList[idx];
            if (song == null) return;

            // Stop any currently playing preview
            StopPreview();

            // Only cancel if we're switching to a different song
            if (targetPreviewIndex != idx) {
                cancelCurrentLoading = true;
                targetPreviewIndex = idx;
                
                // Don't start loading if we're already loading this preview
                if (currentlyLoadingPreview == idx) {
                    return;
                }

                // Start loading with a delay to allow animation to complete
                Instance.StartCoroutine(Instance.DelayedPreviewLoad(song, idx));
            }
        }

        private IEnumerator AnimateScrollDown() {
            Logger.LogInfo("Animating scroll down");
            
            // Simple approach: shift existing content up, add new bottom row
            // Row 0 disappears, rows 1-3 become rows 0-2, new row appears at 3
            
            // Shift rows 1-3 up to rows 0-2 (just copy the text content, only first 4 columns)
            for (int row = 0; row < ROWS_PER_VIEW - 1; row++) {
                for (int col = 0; col < SONGS_PER_ROW; col++) {
                    int fromIndex = (row + 1) * 5 + col; // Source position (5 cards per Unity row)
                    int toIndex = row * 5 + col; // Destination position (5 cards per Unity row)
                    
                    // Copy text content only
                    var fromCard = stageSelectScript.levelCards[fromIndex];
                    var toCard = stageSelectScript.levelCards[toIndex];
                    var fromText = fromCard.gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                    var toText = toCard.gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                    
                    if (fromText != null && toText != null) {
                        toText.text = fromText.text;
                    }
                    toCard.gameObject.SetActive(fromCard.gameObject.activeSelf);
                    if (toCard.gameObject.activeSelf) {
                        toCard.Unlock();
                    }
                }
            }
            
            // Add new content to bottom row (row 3, positions 15-18, skip 19)
            int newRowIndex = currentStartRow + ROWS_PER_VIEW - 1;
            for (int i = 0; i < SONGS_PER_ROW; i++) {
                int cardIndex = 15 + i; // positions 15, 16, 17, 18 (skip 19)
                int songIndex = newRowIndex * SONGS_PER_ROW + i;
                
                var card = stageSelectScript.levelCards[cardIndex];
                var text = card.gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                
                if (songIndex >= songList.Length) {
                    card.gameObject.SetActive(false);
                } else {
                    card.gameObject.SetActive(true);
                    card.Unlock();
                    text.text = Path.GetFileNameWithoutExtension(fileNames[songIndex]);
                }
            }
            
            yield return null; // Single frame delay
        }

        private IEnumerator AnimateScrollUp() {
            Logger.LogInfo("Animating scroll up");
            
            // Simple approach: shift existing content down, add new top row
            // Row 3 disappears, rows 0-2 become rows 1-3, new row appears at 0
            
            // Shift rows 0-2 down to rows 1-3 (just copy the text content, only first 4 columns)
            for (int row = ROWS_PER_VIEW - 1; row > 0; row--) {
                for (int col = 0; col < SONGS_PER_ROW; col++) {
                    int fromIndex = (row - 1) * 5 + col; // Source position (5 cards per Unity row)
                    int toIndex = row * 5 + col; // Destination position (5 cards per Unity row)
                    
                    // Copy text content only
                    var fromCard = stageSelectScript.levelCards[fromIndex];
                    var toCard = stageSelectScript.levelCards[toIndex];
                    var fromText = fromCard.gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                    var toText = toCard.gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                    
                    if (fromText != null && toText != null) {
                        toText.text = fromText.text;
                    }
                    toCard.gameObject.SetActive(fromCard.gameObject.activeSelf);
                    if (toCard.gameObject.activeSelf) {
                        toCard.Unlock();
                    }
                }
            }
            
            // Add new content to top row (row 0, positions 0-3, skip 4)
            int newRowIndex = currentStartRow;
            for (int i = 0; i < SONGS_PER_ROW; i++) {
                int cardIndex = i; // positions 0, 1, 2, 3 (skip 4)
                int songIndex = newRowIndex * SONGS_PER_ROW + i;
                
                var card = stageSelectScript.levelCards[cardIndex];
                var text = card.gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                
                if (songIndex >= songList.Length) {
                    card.gameObject.SetActive(false);
                } else {
                    card.gameObject.SetActive(true);
                    card.Unlock();
                    text.text = Path.GetFileNameWithoutExtension(fileNames[songIndex]);
                }
            }
            
            yield return null; // Single frame delay
        }
        
        private IEnumerator FadeCard(LevelCardScript card, float targetAlpha, float duration) {
            if (card == null || card.gameObject == null) yield break;
            
            var renderer = card.gameObject.GetComponent<SpriteRenderer>();
            var text = card.gameObject.GetComponentInChildren<TextMeshProUGUI>();
            
            if (renderer == null) yield break;
            
            float startAlpha = renderer.color.a;
            float elapsed = 0f;
            
            while (elapsed < duration) {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                
                // Update sprite alpha
                Color spriteColor = renderer.color;
                spriteColor.a = currentAlpha;
                renderer.color = spriteColor;
                
                // Update text alpha if text exists
                if (text != null) {
                    Color textColor = text.color;
                    textColor.a = currentAlpha;
                    text.color = textColor;
                }
                
                yield return null;
            }
            
            // Ensure final alpha is set
            Color finalSpriteColor = renderer.color;
            finalSpriteColor.a = targetAlpha;
            renderer.color = finalSpriteColor;
            
            if (text != null) {
                Color finalTextColor = text.color;
                finalTextColor.a = targetAlpha;
                text.color = finalTextColor;
            }
        }

        private void CopyCardContent(int fromIndex, int toIndex) {
            if (fromIndex >= 0 && fromIndex < 20 && toIndex >= 0 && toIndex < 20) {
                var fromCard = stageSelectScript.levelCards[fromIndex];
                var toCard = stageSelectScript.levelCards[toIndex];
                
                // Copy the text content
                TextMeshProUGUI fromText = fromCard.gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                TextMeshProUGUI toText = toCard.gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                
                if (fromText != null && toText != null) {
                    toText.text = fromText.text;
                }
                
                // Copy the active state
                toCard.gameObject.SetActive(fromCard.gameObject.activeSelf);
                
                // Ensure the card is unlocked if it's active
                if (toCard.gameObject.activeSelf) {
                    toCard.Unlock();
                }
            }
        }

        private void UpdateCardContent(int cardIndex, int songIndex) {
            if (cardIndex >= 0 && cardIndex < 20) {
                var card = stageSelectScript.levelCards[cardIndex];
                TextMeshProUGUI TMPUGUI = card.gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                
                // Only deselect if currently selected to avoid unnecessary state changes
                if (card.gameObject.activeSelf) {
                    card.Deselect();
                }
                
                if (songIndex >= songList.Length || songIndex < 0) {
                    // Hide cards that don't have corresponding songs
                    if (card.gameObject.activeSelf) {
                        card.gameObject.SetActive(false);
                    }
                } else {
                    // Show and update cards with valid song data
                    if (!card.gameObject.activeSelf) {
                        card.gameObject.SetActive(true);
                    }
                    card.Unlock();
                    
                    // Only update text if it has changed to avoid unnecessary redraws
                    string newText = Path.GetFileNameWithoutExtension(fileNames[songIndex]);
                    if (TMPUGUI.text != newText) {
                        TMPUGUI.text = newText;
                    }
                }
            }
        }
        
        private IEnumerator DelayedPreviewLoad(CustomSong song, int idx) {
            // Wait for the selection animation to complete before starting audio loading
            // This prevents audio loading from interfering with the smooth selection animation
            yield return new WaitForSeconds(0.3f); // Typical card selection animation duration
            
            // Check if we're still supposed to load this song (user hasn't navigated to a different song)
            if (targetPreviewIndex != idx) {
                Logger.LogInfo($"Preview load cancelled for {song.SongTitle} - user navigated to different song");
                yield break;
            }
            
            if (song.audioClip == null) {
                currentlyLoadingPreview = idx;
                
                // Reset cancel flag for this new loading operation
                cancelCurrentLoading = false;
                
                Logger.LogInfo($"Loading audio for preview: {song.SongTitle}");
                LoadArchive(song.riq, song, () => {
                    currentlyLoadingPreview = -1;
                    if (song.audioClip != null && targetPreviewIndex == idx) {
                        Logger.LogInfo($"Playing preview: {song.SongTitle}");
                        PlayTempoAudio(song.audioClip, song.audioClip.length / 2f);
                    } else {
                        Logger.LogInfo($"Preview load completed but user navigated away from {song.SongTitle}");
                    }
                });
            } else {
                // For cached audio, play immediately since it's already loaded
                cancelCurrentLoading = false;
                Logger.LogInfo($"Playing cached preview: {song.SongTitle}");
                PlayTempoAudio(song.audioClip, song.audioClip.length / 2f);
            }
        }

            static void PlayTempoAudio(AudioClip clip, float from = 0) {
                // Since we've patched the stage select to be silent, we can just play our preview directly
                if (previewSourceGO == null) {
                    previewSourceGO = new GameObject("Preview Source");
                    GameObject.DontDestroyOnLoad(previewSourceGO);
                }
                
                // Stop any existing preview
                StopPreview();
                
                previewSourceGO.SetActive(false);
                previewSource = previewSourceGO.AddComponent<TempoSound>();
                previewSource.Init(clip, Bus.Music);
                previewSourceGO.SetActive(true);
                previewSource.PlayFrom(from);
                
                Instance.Logger.LogInfo($"Playing preview at {from}s");
            }
            
            static void StopPreview() {
                if (previewSource != null) {
                    previewSource.Stop();
                    Destroy(previewSource);
                    previewSource = null;
                    Instance.Logger.LogInfo("Stopped preview audio");
                }
                // Don't destroy the GameObject, just deactivate it for reuse
                if (previewSourceGO != null) {
                    previewSourceGO.SetActive(false);
                    Instance.Logger.LogInfo("Deactivated preview audio GameObject");
                }
                // No need to restore music - the stage select is patched to be silent in custom songs mode
            }

        private IEnumerator PreloadAllAudioIntoMemoryWithProgress() {
            Logger.LogInfo("Starting full audio preload into memory with progress tracking");
            
            int loadedCount = 0;
            for (int i = 0; i < songList.Length; i++) {
                if (songList[i].audioClip == null) {
                    string cacheFileName = Path.GetFileNameWithoutExtension(songList[i].riq) + ".audio";
                    string cachedPath = Path.Combine(audioCacheDir, cacheFileName);
                    
                    if (File.Exists(cachedPath)) {
                        currentProcessingFile = songList[i].SongTitle;
                        Logger.LogInfo($"Loading into memory: {songList[i].SongTitle}");
                        
                        // Load directly from cache without the LoadArchive overhead
                        bool loadComplete = false;
                        yield return StartCoroutine(LoadFromCacheAsyncCoroutine(cachedPath, songList[i], () => {
                            loadComplete = true;
                            loadedCount++;
                            filesProcessed++;
                        }));
                        
                        // Wait for this load to complete before continuing
                        while (!loadComplete) {
                            yield return null;
                        }
                        
                        // Brief pause to prevent overwhelming
                        yield return new WaitForSeconds(0.05f);
                    } else {
                        filesProcessed++;
                    }
                } else {
                    filesProcessed++;
                }
            }
            
            Logger.LogInfo($"Completed full audio preload - {loadedCount} songs loaded into memory");
        }
        
        private IEnumerator PreloadAllAudioIntoMemory() {
            // Legacy method - now just calls the progress version
            yield return StartCoroutine(PreloadAllAudioIntoMemoryWithProgress());
        }
        
        private IEnumerator PreloadAllVisibleAudioClips(int startRow) {
            // No longer needed - all audio is preloaded into memory at startup
            Logger.LogInfo($"Skipping visible preload - all audio already in memory");
            yield break;
        }

        public void LoadArchive(string path, CustomSong song, System.Action onComplete = null) {
            if (Instance == null) {
                Logger.LogError("RiqMenuMain instance is null in LoadArchive!");
                return;
            }
            
            // Skip if already loaded to prevent duplicate loading
            if (song.audioClip != null) {
                Logger.LogInfo($"Audio already loaded for {song.SongTitle}, skipping duplicate load");
                onComplete?.Invoke();
                return;
            }
            
            // Check if we have a cached audio file first
            string cacheFileName = Path.GetFileNameWithoutExtension(song.riq) + ".audio";
            string cachedPath = Path.Combine(audioCacheDir, cacheFileName);
            
            if (File.Exists(cachedPath)) {
                // Use cached file for instant loading
                StartCoroutine(LoadFromCacheAsyncCoroutine(cachedPath, song, onComplete));
            } else {
                // Fall back to ZIP extraction
                StartCoroutine(LoadArchiveAsync(path, song, onComplete));
            }
        }

        private void LoadFromCacheAsync(string cachedPath, CustomSong song, System.Action onComplete = null) {
            StartCoroutine(LoadFromCacheAsyncCoroutine(cachedPath, song, onComplete));
        }

        private IEnumerator LoadFromCacheAsyncCoroutine(string cachedPath, CustomSong song, System.Action onComplete = null) {
            Logger.LogInfo($"Loading {song.SongTitle} from cache for instant preview");
            
            // Reset cancel flag for this operation
            cancelCurrentLoading = false;
            
            Action<AudioClip> callbackClip = (AudioClip c) => {
                if (c != null) {
                    Logger.LogInfo($"Instantly loaded cached audio for {song.SongTitle}");
                    song.audioClip = c;
                    onComplete?.Invoke();
                } else {
                    Logger.LogError($"Failed to load cached audio for {song.SongTitle}");
                    onComplete?.Invoke();
                }
            };
            
            // Move file I/O to background thread to prevent UI freezing
            byte[] headerBytes = null;
            bool fileReadComplete = false;
            System.Exception fileReadException = null;
            
            ThreadPool.QueueUserWorkItem((_) => {
                try {
                    byte[] bytes = new byte[12];
                    using (FileStream fs = File.OpenRead(cachedPath)) {
                        fs.Read(bytes, 0, 12);
                    }
                    headerBytes = bytes;
                } catch (System.Exception ex) {
                    fileReadException = ex;
                } finally {
                    fileReadComplete = true;
                }
            });
            
            // Wait for file read to complete
            while (!fileReadComplete) {
                yield return null;
            }
            
            if (fileReadException != null) {
                Logger.LogError($"Failed to read cached audio header for {song.SongTitle}: {fileReadException.Message}");
                yield break;
            }
            
            AudioType audioType = AudioType.UNKNOWN;
            if (headerBytes.Length >= 4 && Encoding.ASCII.GetString(GetSubArray(headerBytes, 0, 4)) == "OggS") {
                audioType = AudioType.OGGVORBIS;
            } else if (headerBytes.Length >= 3 && Encoding.ASCII.GetString(GetSubArray(headerBytes, 0, 3)) == "ID3") {
                audioType = AudioType.MPEG;
            } else if (headerBytes.Length >= 2 && headerBytes[0] == 255 && (headerBytes[1] == 251 || headerBytes[1] == 243 || headerBytes[1] == 242)) {
                audioType = AudioType.MPEG;
            } else if (headerBytes.Length >= 12 && 
                      Encoding.ASCII.GetString(GetSubArray(headerBytes, 0, 4)) == "RIFF" && 
                      Encoding.ASCII.GetString(GetSubArray(headerBytes, 8, 4)) == "WAVE") {
                audioType = AudioType.WAV;
            } else {
                Logger.LogError($"Unsupported cached audio format for {song.SongTitle}");
                yield break;
            }
            
            // Load directly from cached file - no ZIP extraction needed!
            yield return StartCoroutine(GetAudioClipFromFile(cachedPath, audioType, callbackClip));
        }

        private IEnumerator LoadArchiveAsync(string path, CustomSong song, System.Action onComplete = null) {
            Logger.LogInfo($"Starting background load for {song.SongTitle}");
            
            // Callback for when the audio clip is ready
            Action<AudioClip> callbackClip = (AudioClip c) => {
                if (c != null) {
                    Logger.LogInfo($"Successfully loaded audio for {song.SongTitle}");
                    song.audioClip = c;
                    onComplete?.Invoke();
                } else {
                    Logger.LogError($"Failed to load audio for {song.SongTitle} - AudioClip is null");
                }
            };

            // These will be set by the background thread
            byte[] audioData = null;
            AudioType audioType = AudioType.UNKNOWN;
            string entryName = null;
            bool backgroundComplete = false;
            System.Exception backgroundException = null;
            
            // Signal to cancel any ongoing loading
            cancelCurrentLoading = true;
            
            // Start heavy file operations on background thread
            ThreadPool.QueueUserWorkItem((_) => {
                lock (loadingLock) {
                    try {
                        // Reset cancel flag for this operation
                        cancelCurrentLoading = false;
                        
                        if (cancelCurrentLoading) return;
                        
                        // File I/O operations (heavy)
                        using (FileStream fileStream = File.Open(path, FileMode.Open)) {
                            using (ZipArchive zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read)) {
                                if (cancelCurrentLoading) return;
                                
                                ZipArchiveEntry entry = FindSong(zipArchive, out ZipArchiveEntry e) ? e : zipArchive.GetEntry("song.bin");
                                
                                if (entry == null) {
                                    backgroundException = new System.Exception($"Song not found in riq: {path}");
                                    return;
                                }
                                
                                if (cancelCurrentLoading) return;
                                
                                entryName = entry.Name;
                                using (Stream stream2 = entry.Open()) {
                                    using (MemoryStream memoryStream = new MemoryStream()) {
                                        // This is the heavy operation
                                        stream2.CopyTo(memoryStream);
                                        audioData = memoryStream.ToArray();
                                    }
                                }
                            }
                        }
                        
                        if (cancelCurrentLoading || audioData == null) return;
                        
                        // Audio type detection (fast)
                        if (audioData.Length >= 4 && Encoding.ASCII.GetString(GetSubArray(audioData, 0, 4)) == "OggS") {
                            audioType = AudioType.OGGVORBIS;
                        } else if (audioData.Length >= 3 && Encoding.ASCII.GetString(GetSubArray(audioData, 0, 3)) == "ID3") {
                            audioType = AudioType.MPEG;
                        } else if (audioData.Length >= 2 && audioData[0] == 255 && (audioData[1] == 251 || audioData[1] == 243 || audioData[1] == 242)) {
                            audioType = AudioType.MPEG;
                        } else if (audioData.Length >= 12 && 
                                  Encoding.ASCII.GetString(GetSubArray(audioData, 0, 4)) == "RIFF" && 
                                  Encoding.ASCII.GetString(GetSubArray(audioData, 8, 4)) == "WAVE") {
                            audioType = AudioType.WAV;
                        } else {
                            backgroundException = new System.Exception($"{entryName} is not a supported audio file");
                            return;
                        }
                        
                    } catch (System.Exception ex) {
                        backgroundException = ex;
                    } finally {
                        backgroundComplete = true;
                    }
                }
            });
            
            // Wait for background thread to complete, yielding every frame
            while (!backgroundComplete) {
                yield return null;
                
                // Check if we should cancel
                if (cancelCurrentLoading) {
                    Logger.LogInfo($"Loading cancelled for {song.SongTitle}");
                    yield break;
                }
            }
            
            if (backgroundException != null) {
                Logger.LogError($"Background loading failed for {song.SongTitle}: {backgroundException.Message}");
                yield break;
            }
            
            if (audioData == null || cancelCurrentLoading) {
                Logger.LogWarning($"Audio data is null or cancelled for {song.SongTitle}");
                yield break;
            }
            
            Logger.LogInfo($"Background loading complete for {song.SongTitle}, creating Unity AudioClip");
            
            // Unity audio clip creation (must be on main thread)
            yield return StartCoroutine(GetAudioClip(audioData, audioType, callbackClip));
        }

        public bool FindSong(ZipArchive archive, out ZipArchiveEntry entry) {
            entry = null;
            foreach (ZipArchiveEntry e in archive.Entries) {
                if (e.FullName.StartsWith("song", StringComparison.OrdinalIgnoreCase)) {
                    entry = e;
                    return true;
                }
            }
            return false;
        }

        // Token: 0x060002E0 RID: 736 RVA: 0x00014670 File Offset: 0x00012870
        private IEnumerator GetAudioClip(byte[] bytes, AudioType audioType, Action<AudioClip> callbackClip = null) {
            string path = Path.Combine(Application.temporaryCachePath, $"riqmenu_song_{System.DateTime.Now.Ticks}.bin");
            
            // Write file on background thread to avoid blocking UI
            bool fileWriteComplete = false;
            bool fileWriteSuccess = false;
            
            ThreadPool.QueueUserWorkItem((_) => {
                try {
                    File.WriteAllBytes(path, bytes);
                    fileWriteSuccess = true;
                } catch (System.Exception ex) {
                    Logger.LogError($"Error writing temp audio file: {ex.Message}");
                    fileWriteSuccess = false;
                } finally {
                    fileWriteComplete = true;
                }
            });
            
            // Wait for file write to complete
            while (!fileWriteComplete) {
                yield return null;
            }
            
            if (!fileWriteSuccess) {
                callbackClip?.Invoke(null);
                yield break;
            }
            
            // Load the audio file
            yield return StartCoroutine(this.GetAudioClip(path, audioType, callbackClip));
            
            // Cleanup on background thread
            ThreadPool.QueueUserWorkItem((_) => {
                try {
                    if (File.Exists(path)) {
                        File.Delete(path);
                    }
                } catch (System.Exception ex) {
                    Logger.LogWarning($"Failed to cleanup temp file: {ex.Message}");
                }
            });
        }

        // Load audio directly from file path (for cached files)
        private IEnumerator GetAudioClipFromFile(string filePath, AudioType audioType, Action<AudioClip> callbackClip = null) {
            if (filePath.StartsWith("/")) {
                filePath = filePath.Substring(1);
            }
            
            string uri = "file://localhost/" + filePath.Replace('\\', '/');
            string cleanUrl = Utils.CleanPath(uri);
            Logger.LogInfo($"Loading cached audio from: {cleanUrl}");
            
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType)) {
                www.timeout = 10;
                yield return www.SendWebRequest();
                
                if (www.result != UnityWebRequest.Result.Success) {
                    Logger.LogError($"Cached audio load failed: {www.result} - {www.error}");
                    callbackClip?.Invoke(null);
                    yield break;
                }
                
                AudioClip audioContent = DownloadHandlerAudioClip.GetContent(www);
                if (audioContent == null) {
                    Logger.LogError($"Failed to extract cached audio content from {cleanUrl}");
                    callbackClip?.Invoke(null);
                    yield break;
                }
                
                audioContent.name = Path.GetFileNameWithoutExtension(filePath);
                Logger.LogInfo($"Instantly loaded cached AudioClip: {audioContent.name} (Length: {audioContent.length}s)");
                callbackClip?.Invoke(audioContent);
            }
        }

        // Token: 0x060002E1 RID: 737 RVA: 0x0001468D File Offset: 0x0001288D
        private IEnumerator GetAudioClip(string path, AudioType audioType, Action<AudioClip> callbackClip = null) {
            if (path.StartsWith("/")) {
                path = path.Substring(1);
            }
            
            string uri = "file://localhost/" + path.Replace('\\', '/');
            string cleanUrl = Utils.CleanPath(uri);
            Logger.LogInfo($"Loading audio from URL: {cleanUrl}");
            
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType)) {
                www.timeout = 10; // Set timeout to prevent hanging
                yield return www.SendWebRequest();
                
                if (www.result != UnityWebRequest.Result.Success) {
                    Logger.LogError($"Audio load failed: {www.result} - {www.error}");
                    callbackClip?.Invoke(null);
                    yield break;
                }
                
                AudioClip audioContent = DownloadHandlerAudioClip.GetContent(www);
                if (audioContent == null) {
                    Logger.LogError($"Failed to extract audio content from {cleanUrl}");
                    callbackClip?.Invoke(null);
                    yield break;
                }
                
                // Extract filename from the original path, not the URL
                audioContent.name = Path.GetFileNameWithoutExtension(path);
                Logger.LogInfo($"Successfully created AudioClip: {audioContent.name} (Length: {audioContent.length}s)");
                callbackClip?.Invoke(audioContent);
            }
        }
    }
}
