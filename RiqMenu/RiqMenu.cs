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
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace RiqMenu {
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class RiqMenuMain : BaseUnityPlugin
    {
        private bool showMenu = false;
        private bool loadCustomSongs = false;
        public string[] fileNames;
        public string riqPath;
        private int pages = 0;
        private int currentPage = 0;

        private static RiqMenuMain instance;
        private static TitleScript titleScript;
        private static StageSelectScript stageSelectScript;

        private static CustomSong[] songList = [];
        private List<CustomSong> downloadableSongList = new List<CustomSong>();

        private static GameObject previewSourceGO;
        private static TempoSound previewSource;

        public static List<string> newOptions = new List<string>() {
            "Play",
            "Custom Songs",
            //"Get Custom Songs",
            "Settings",
            "Credits",
            "Keep Updated!",
            "Quit"
        };
        public Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);

        public static Scene currentScene;

        public void Awake() {
            if (instance == null) {
                instance = this;
            }

            LoadLocalSongs();
            SceneManager.sceneLoaded += OnSceneLoaded;
            harmony.PatchAll();
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        public void LoadLocalSongs() {
            string path = Path.Combine(Application.dataPath, "StreamingAssets");
            Logger.LogInfo("Detected Asset Path: " + path);
            string[] excludeFiles = {
                "flipper_snapper.riq",
                "hammer_time.riq",
                "bits_and_bops.riq",
                "meet_and_tweet.riq"
            };
            fileNames = Directory.GetFiles(path)
                .Where(file => file.EndsWith(".riq") && !excludeFiles.Contains(Path.GetFileName(file)))
                .ToArray();

            pages = (int)Math.Ceiling((double)fileNames.Length / 20) - 1;

            songList = new CustomSong[fileNames.Length];
            for (int i = 0; i < fileNames.Length; i++) {
                songList[i] = new CustomSong();
                songList[i].riq = fileNames[i];
                songList[i].SongTitle = Path.GetFileName(fileNames[i]);


                Logger.LogInfo($"Loading {songList[i].SongTitle}");
            }
        }

        public void Update() {
            if (Input.GetKeyDown(KeyCode.F1)) {
                showMenu = !showMenu;
                Cursor.visible = showMenu;
            }

            if (Input.GetKeyDown(KeyCode.R)) {
                if (instance.riqPath != null) {
                    RiqLoader.path = instance.riqPath;
                }
                if (RiqLoader.path != null) {
                    TempoSceneManager.LoadScene(SceneKey.RiqLoader, false);
                }
            }

            if (currentScene.name == SceneKey.TitleScreen.ToString()) {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) {
                    FieldInfo prop = titleScript.GetType().GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance);
                    int selected = (int)prop.GetValue(titleScript);

                    Logger.LogInfo($"Selected index {newOptions[selected]}");
                    if (newOptions[selected] == "Play") {
                        loadCustomSongs = false;
                    }

                    if (newOptions[selected] == "Custom Songs") {
                        loadCustomSongs = true;
                        StartCoroutine(titleScript.Play(SceneKey.StageSelect));
                    }
                }
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode) {
            if (loadSceneMode == LoadSceneMode.Single) {
                currentScene = scene;
            }

            if ((currentScene.name == "StageSelectDemoSteam") && instance.loadCustomSongs) {
                instance.riqPath = null;
                stageSelectScript = GameObject.Find("StageSelect").GetComponent<StageSelectScript>();
                instance.SetupCustomStageSelect(instance.currentPage, true);
            }

            if (scene.name == SceneKey.TitleScreen.ToString()) {
                instance.riqPath = null;
                TitleScript title = GameObject.Find("TitleScript").GetComponent<TitleScript>();
                titleScript = title;
                title.buildTypeText.text += " (<color=#ff0000>R</color><color=#ff7f00>i</color><color=#ffff00>q</color><color=#00ff00>M</color><color=#0000ff>e</color><color=#4b0082>n</color><color=#9400d3>u</color>)";

                // Add custom option
                FieldInfo prop = title.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                prop.SetValue(title, newOptions);
            }
        }

        public void SetupCustomStageSelect(int page, bool reset) {
            Logger.LogInfo("Setting up custom stage select");
            if (stageSelectScript != null) {
                foreach (GameObject gameobject in GameObject.FindObjectsOfType<GameObject>()) {
                    if (gameobject.name == "level_menu_cabinet_cover") {
                        GameObject.Destroy(gameobject);
                    }
                }

                if (!reset) {
                    for (int i = page * 20; i < (page + 1) * 20; i++) {
                        TextMeshProUGUI TMPUGUI = stageSelectScript.levelCards[i % 20].gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
                        stageSelectScript.levelCards[i % 20].Deselect();
                        if (i >= songList.Length) {
                            stageSelectScript.levelCards[i % 20].gameObject.SetActive(false);
                        } else {
                            stageSelectScript.levelCards[i % 20].gameObject.SetActive(true);
                            stageSelectScript.levelCards[i % 20].Unlock();
                            TMPUGUI.text = Path.GetFileNameWithoutExtension(fileNames[i]);
                        }

                    }
                    return;
                }

                LevelCardScript baseCard = stageSelectScript.levelCards[3];
                baseCard.gameObject.SetActive(true);
                SpriteRenderer baseSpriteRenderer = baseCard.GetComponent<SpriteRenderer>();
                Sprite baseSprite = baseSpriteRenderer.sprite;
                TMPro.TextMeshProUGUI style = baseCard.gameObject.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                style.fontSize = 30;

                for (int i = page * 20; i < (page + 1) * 20; i++) {
                    stageSelectScript.levelCards[i % 20].gameObject.SetActive(true);
                    stageSelectScript.levelCards[i % 20].Unlock();
                    stageSelectScript.levelCards[i % 20].scene = SceneKey.RiqLoader;
                    stageSelectScript.levelCards[i % 20].promoLink = PromoLink.None;
                    stageSelectScript.levelCards[i % 20].activatesPromoLinks = false;
                    stageSelectScript.levelCards[i % 20].gameObject.GetComponent<SpriteRenderer>().sprite = baseSprite;
                    foreach (Transform child in stageSelectScript.levelCards[i % 20].gameObject.transform) {
                        if (child.name != "Canvas") {
                            GameObject.Destroy(child.gameObject);
                            continue;
                        }
                        TMPro.TextMeshProUGUI TMPUGUI = child.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                        TMPUGUI.fontSize = style.fontSize;
                        TMPUGUI.font = style.font;
                        TMPUGUI.fontMaterial = style.fontMaterial;
                        TMPUGUI.color = style.color;
                        if (i < songList.Length) {
                            TMPUGUI.text = Path.GetFileNameWithoutExtension(fileNames[i]);
                        }
                    }

                    if (i >= songList.Length) {
                        stageSelectScript.levelCards[i % 20].gameObject.SetActive(false);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(LevelCardScript), "Confirm", [])]
        private static class CardConfirmPatch {
            private static void Postfix(LevelCardScript __instance) {
                MixtapeLoader.autoplay = false;
                if (Input.GetKey(KeyCode.P)) {
                    MixtapeLoader.autoplay = true;
                }
                if (instance.loadCustomSongs) {
                    int thisIndex = Array.IndexOf(stageSelectScript.levelCards, __instance);
                    instance.riqPath = instance.fileNames[instance.currentPage * 20 + thisIndex];
                    RiqLoader.path = instance.riqPath;
                }
            }
        }

        [HarmonyPatch(typeof(LevelCardScript), "Select", [])]
        private static class CardSelectPatch {
            private static void Postfix(LevelCardScript __instance) {
                if (instance.loadCustomSongs) {
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

        IEnumerator DownArrowCheck(FieldInfo propX, FieldInfo propY, int currentX, int currentY) {
            yield return null;
            yield return null;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) {
                stageSelectScript.levelCards[currentY * 5 + currentX].Deselect();
                instance.SetupCustomStageSelect(++instance.currentPage, false);
                yield return null;
                stageSelectScript.levelCards[0].Select();
                propX.SetValue(stageSelectScript, 0);
                propY.SetValue(stageSelectScript, 0);
            }
        }

        IEnumerator UpArrowCheck(FieldInfo propX, FieldInfo propY, int currentX, int currentY) {
            yield return null;
            yield return null;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) {
                stageSelectScript.levelCards[currentY * 5 + currentX].Deselect();
                instance.SetupCustomStageSelect(--instance.currentPage, false);
                yield return null;
                stageSelectScript.levelCards[15].Select();
                propX.SetValue(stageSelectScript, 0);
                propY.SetValue(stageSelectScript, 3);
            }
        }

        [HarmonyPatch(typeof(StageSelectScript), "OnDirection", [typeof(int), typeof(int)])]
        private static class StageSelectOnDirectionPatch {
            private static bool Prefix(StageSelectScript __instance, int x, int y) {
                if (!instance.loadCustomSongs) return true;
                FieldInfo propX = __instance.GetType().GetField("currentX", BindingFlags.NonPublic | BindingFlags.Instance);
                int currentX = (int)propX.GetValue(__instance);
                FieldInfo propY = __instance.GetType().GetField("currentY", BindingFlags.NonPublic | BindingFlags.Instance);
                int currentY = (int)propY.GetValue(__instance);
                if (x == 0 && y == 1 && currentY == 3 && instance.currentPage < instance.pages) {
                    instance.StartCoroutine(instance.DownArrowCheck(propX, propY, currentX, currentY));
                    return true;
                }
                if (x == 0 && y == -1 && currentY == 0 && instance.currentPage > 0) {
                    instance.StartCoroutine(instance.UpArrowCheck(propX, propY, currentX, currentY));
                    return true;
                }

                return true;
            }
        }

        static void TryPlayPreview(int currentX, int currentY) {
            int idx = currentY * 5 + currentX;
            if (idx < 0 || idx >= songList.Length) {
                instance.Logger.LogWarning($"Selected card outside bounds at {idx}/{songList.Length}");
                return;
            }

            CustomSong song = songList[idx];
            if (song == null) return;

            if (song.audioClip == null) {
                instance.LoadArchive(song.riq, song, () => {
                    if (song.audioClip != null) {
                        instance.Logger.LogInfo($"Trying to play {song.SongTitle}");
                        PlayTempoAudio(song.audioClip, song.audioClip.length / 2f);
                        RiqMenuMain.instance.Logger.LogInfo("Playing preview");
                    } else {
                        RiqMenuMain.instance.Logger.LogInfo("Failed to play preview");
                    }
                });
            } else {
                PlayTempoAudio(song.audioClip, song.audioClip.length / 2f);
            }
        }

            static void PlayTempoAudio(AudioClip clip, float from = 0) {
                if (previewSourceGO == null) {
                    previewSourceGO = new GameObject("Preview Source");
                }
                if (previewSource != null) {
                    previewSource.Stop();
                    Destroy(previewSource);
                    previewSource = null;
                }
                previewSourceGO.SetActive(false);
                previewSource = previewSourceGO.AddComponent<TempoSound>();
                previewSource.bus = Bus.Music;

                previewSource.audioClip = clip;
                previewSourceGO.SetActive(true);
                previewSource.PlayFrom(from);
            }

        public void LoadArchive(string path, CustomSong song, System.Action onComplete = null) {
            Action<AudioClip> callbackClip = (AudioClip c) => {
                Logger.LogInfo($"Loaded audio for {song.SongTitle}");
                song.audioClip = c;
                onComplete?.Invoke();
            };

            using (FileStream fileStream = File.Open(path, FileMode.Open)) {
                using (ZipArchive zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read)) {

                    ZipArchiveEntry entry = FindSong(zipArchive, out ZipArchiveEntry e) ? e : zipArchive.GetEntry("song.bin");

                    if (entry == null) {
                        throw new Exception("Song not found in riq");
                    }
                    using (Stream stream2 = entry.Open()) {
                        using (MemoryStream memoryStream = new MemoryStream()) {
                            stream2.CopyTo(memoryStream);
                            byte[] array = memoryStream.ToArray();
                            AudioType audioType;
                            if (Encoding.ASCII.GetString(Helpers.GetSubArray<byte>(array, 0, 4)) == "OggS") {
                                audioType = AudioType.OGGVORBIS;
                            } else if (Encoding.ASCII.GetString(Helpers.GetSubArray<byte>(array, 0, 3)) == "ID3") {
                                audioType = AudioType.MPEG;
                            } else if (array[0] == 255 && (array[1] == 251 || array[1] == 243 || array[1] == 242)) {
                                audioType = AudioType.MPEG;
                            } else {
                                if (!(Encoding.ASCII.GetString(Helpers.GetSubArray<byte>(array, 0, 4)) == "RIFF") || !(Encoding.ASCII.GetString(Helpers.GetSubArray<byte>(array, 8, 12)) == "WAVE")) {
                                    throw new Exception($"{entry.Name} is not an ogg, mp3 or wav file");
                                }
                                audioType = AudioType.WAV;
                            }
                            StartCoroutine(this.GetAudioClip(array, audioType, callbackClip));
                        }
                    }
                }
            }
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
            string path = Application.temporaryCachePath + "/song.bin";
            File.WriteAllBytes(path, bytes);
            yield return StartCoroutine(this.GetAudioClip(path, audioType, callbackClip));
            File.Delete(path);
            yield break;
        }

        // Token: 0x060002E1 RID: 737 RVA: 0x0001468D File Offset: 0x0001288D
        private IEnumerator GetAudioClip(string path, AudioType audioType, Action<AudioClip> callbackClip = null) {
            if (path.StartsWith("/")) {
                string text = path;
                int length = text.Length;
                int num = 1;
                int length2 = length - num;
                path = text.Substring(num, length2);
            }
            string uri = "file://localhost/" + path;
            string cleanUrl = Utils.CleanPath(uri);
            Debug.Log("Loading audio from URL " + cleanUrl);
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType)) {
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success) {
                    Debug.LogError(string.Format("Web Request failed with result {0}: {1}", www.result, www.error));
                    TempoSceneManager.LoadScene(SceneKey.GenericError, false);
                    yield break;
                }
                AudioClip audioContent = DownloadHandlerAudioClip.GetContent(www);
                audioContent.name = cleanUrl;
                base.gameObject.SetActive(false);
                TempoSound tempoSound = base.gameObject.AddComponent<TempoSound>();
                tempoSound.audioClip = audioContent;
                tempoSound.bus = Bus.Music;
                base.gameObject.SetActive(true);
                callbackClip?.Invoke(audioContent);
            }
            Utils.CleanPath(path);
            yield break;
        }
    }
}
