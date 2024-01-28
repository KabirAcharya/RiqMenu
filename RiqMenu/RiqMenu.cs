using System.Linq;
using System.IO;
using MelonLoader;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Reflection;
using System;
using HarmonyLib;
using TMPro;

namespace RiqMenu {
    public class RiqMenuMain : MelonMod {
        private bool showMenu = false;
        private bool autoPlay = false;
        private bool loadCustomSongs = false;
        public string[] fileNames;
        public string riqPath;
        private int pages = 0;
        private int currentPage = 0;

        private static RiqMenuMain instance;
        private static TitleScript titleScript;
        private static StageSelectScript stageSelectScript;

        private static GameObject menuObject;

        private static CustomSongsScene customSongScene;
        private static CustomSongsScene getCustomSongScene;

        private static SongDownloadData songDownload;

        private CustomSong[] songList = new CustomSong[0];
        private List<CustomSong> downloadableSongList = new List<CustomSong>();

        public static List<string> newOptions = new List<string>() {
            "Play",
            "Custom Songs",
            //"Get Custom Songs",
            "Settings",
            "Credits",
            "Keep Updated!",
            "Quit"
        };

        public static Scene currentScene;

        public override void OnInitializeMelon() {
            if (instance == null) {
                instance = this;
            }

            songDownload = new SongDownloadData(LoggerInstance);
            LoadLocalSongs();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public void LoadLocalSongs() {
            string path = Path.Combine(Application.dataPath, "StreamingAssets");
            LoggerInstance.Msg("Detected Asset Path: " + path);
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
            }
        }

        bool reachedTop = false;

        public override void OnUpdate() {
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
                instance.riqPath = null;
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) {
                    FieldInfo prop = titleScript.GetType().GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance);
                    int selected = (int)prop.GetValue(titleScript);

                    LoggerInstance.Msg($"Selected index {newOptions[selected]}");
                    if (newOptions[selected] == "Play") {
                        loadCustomSongs = false;
                    }

                    if (newOptions[selected] == "Custom Songs") {
                        loadCustomSongs = true;
                        MelonCoroutines.Start(titleScript.Play(SceneKey.StageSelect));
                    }

                    if (newOptions[selected] == "Get Custom Songs") {
                        ToggleCustomMenu(getCustomSongScene);
                    }
                }
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode) {
            if (loadSceneMode == LoadSceneMode.Single) {
                currentScene = scene;
            }

            if ((currentScene.name == "StageSelectDemoSteam") && instance.loadCustomSongs) {
                stageSelectScript = GameObject.Find("StageSelect").GetComponent<StageSelectScript>();
                instance.SetupCustomStageSelect(instance.currentPage, true);
            }

            if (scene.name == SceneKey.TitleScreen.ToString()) {
                TitleScript title = GameObject.Find("TitleScript").GetComponent<TitleScript>();
                titleScript = title;
                title.buildTypeText.text += " (<color=#ff0000>R</color><color=#ff7f00>i</color><color=#ffff00>q</color><color=#00ff00>M</color><color=#0000ff>e</color><color=#4b0082>n</color><color=#9400d3>u</color>)";

                menuObject = GameObject.Find("Menu");

                customSongScene = new CustomSongsScene();
                customSongScene.SetContent(instance.songList);
                customSongScene.CustomSongSelected += instance.CustomSongSelected;

                getCustomSongScene = new CustomSongsScene();
                // Load songs from file
                _ = songDownload.RetrieveSongData((List<CustomSong> data) => {
                    instance.LoggerInstance.Msg($"{data.Count} songs found");
                    instance.downloadableSongList = data;

                    getCustomSongScene.SetContent(data.ToArray());
                });
                getCustomSongScene.CustomSongSelected += (CustomSong song) => {
                    if (instance.downloadableSongList != null && instance.downloadableSongList.Count > 0) {
                        instance.LoggerInstance.Msg("Song downloading...");
                        _ = songDownload.DownloadSong(song, (bool b) => {
                            instance.LoggerInstance.Msg("Song download " + (b ? "complete" : "failed") + $". Song {song.SongTitle}");
                        });
                    }
                };

                // Add custom option
                FieldInfo prop = title.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                prop.SetValue(title, newOptions);
            }
        }

        public void CustomSongSelected(CustomSong song) {
            LoggerInstance.Msg("Loading Riq: " + song.SongTitle);
            MelonCoroutines.Start(this.OnRiqSelected(song.riq));
        }

        IEnumerator OnRiqSelected(string fileName) {
            RiqLoader.path = fileName;
            MixtapeLoader.autoplay = autoPlay;
            showMenu = false;
            Cursor.visible = false;
            CameraScript cameraScript = GameObject.Find("Main Camera").GetComponent<CameraScript>();
            yield return cameraScript.FadeOut(0.1f);
            yield return new WaitForSeconds(0.9f);
            TempoSceneManager.LoadScene(SceneKey.RiqLoader, false);
        }

        public void ToggleCustomMenu(CustomSongsScene menu) {
            bool isActive = menu.ToggleVisibility();

            Cursor.visible = isActive;

            titleScript.enabled = !isActive;
            menuObject.SetActive(!isActive);
        }

        public void SetupCustomStageSelect(int page, bool reset) {
            LoggerInstance.Msg("Setting up custom stage select");
            if (stageSelectScript != null) {
                foreach (GameObject gameobject in GameObject.FindObjectsOfType<GameObject>()) {
                    if (gameobject.name == "level_menu_cabinet_cover") {
                        GameObject.Destroy(gameobject);
                    }
                }

                if (!reset) {
                    for (int i = page * 20; i < (page + 1) * 20; i++) {
                        TextMeshProUGUI TMPUGUI = stageSelectScript.levelCards[i % 20].gameObject.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
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
        
        [HarmonyPatch(typeof(LevelCardScript), "Select", new Type[] { })]
        private static class CardSelectPatch {
            private static void Postfix(LevelCardScript __instance) {
                int thisIndex = Array.IndexOf(stageSelectScript.levelCards, __instance);
                RiqLoader.path = instance.fileNames[thisIndex];
            }
        }

        [HarmonyPatch(typeof(RiqLoader), "Awake", new Type[] { })]
        private static class RiqLoaderAwakePatch {
            private static void Postfix() {
                RiqLoader.path = null;
            }
        }

        [HarmonyPatch(typeof(LevelCardScript), "get_Selectable", new Type[] { })]
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
                stageSelectScript.levelCards[15].Select();
                propX.SetValue(stageSelectScript, 0);
                propY.SetValue(stageSelectScript, 3);
            }
        }

        [HarmonyPatch(typeof(StageSelectScript), "OnDirection", new Type[] { typeof(int), typeof(int) })]
        private static class StageSelectOnDirectionPatch {
            private static bool Prefix(StageSelectScript __instance, int x, int y) {
                if (!instance.loadCustomSongs) return true;
                FieldInfo propX = __instance.GetType().GetField("currentX", BindingFlags.NonPublic | BindingFlags.Instance);
                int currentX = (int)propX.GetValue(__instance);
                FieldInfo propY = __instance.GetType().GetField("currentY", BindingFlags.NonPublic | BindingFlags.Instance);
                int currentY = (int)propY.GetValue(__instance);
                if (x == 0 && y == 1 && currentY == 3 && instance.currentPage < instance.pages) {
                    MelonCoroutines.Start(instance.DownArrowCheck(propX, propY, currentX, currentY));
                    return false;
                }
                if (x == 0 && y == -1 && currentY == 0 && instance.currentPage > 0) {
                    MelonCoroutines.Start(instance.UpArrowCheck(propX, propY, currentX, currentY));
                    return false;
                }
                return true;
            }
        }
    }
}
