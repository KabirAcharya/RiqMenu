using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System;
using UnityEngine.SceneManagement;
using UnityEngine;
using System.Collections;
using TMPro;
using System.Linq;

namespace RiqMenu
{
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
                instance.riqPath = null;
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
                stageSelectScript = GameObject.Find("StageSelect").GetComponent<StageSelectScript>();
                instance.SetupCustomStageSelect(instance.currentPage, true);
            }

            if (scene.name == SceneKey.TitleScreen.ToString()) {
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

        [HarmonyPatch(typeof(LevelCardScript), "Select", new Type[] { })]
        private static class CardSelectPatch {
            private static void Postfix(LevelCardScript __instance) {
                int thisIndex = Array.IndexOf(stageSelectScript.levelCards, __instance);
                RiqLoader.path = instance.fileNames[instance.currentPage * 20 + thisIndex];
                instance.Logger.LogInfo($"RiqLoader.path {RiqLoader.path}");
            }
        }

        [HarmonyPatch(typeof(LevelCardScript), "Confirm", new Type[] { })]
        private static class CardConfirmPatch {
            private static void Prefix() {
                MixtapeLoader.autoplay = false;
                if (Input.GetKey(KeyCode.P)) {
                    MixtapeLoader.autoplay = true;
                }
            }
        }

        [HarmonyPatch(typeof(RiqLoader), "Awake", new Type[] { })]
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

        [HarmonyPatch(typeof(StageSelectScript), "OnDirection", new Type[] { typeof(int), typeof(int) })]
        private static class StageSelectOnDirectionPatch {
            private static bool Prefix(StageSelectScript __instance, int x, int y) {
                if (!instance.loadCustomSongs) return true;
                FieldInfo propX = __instance.GetType().GetField("currentX", BindingFlags.NonPublic | BindingFlags.Instance);
                int currentX = (int)propX.GetValue(__instance);
                FieldInfo propY = __instance.GetType().GetField("currentY", BindingFlags.NonPublic | BindingFlags.Instance);
                int currentY = (int)propY.GetValue(__instance);
                if (x == 0 && y == 1 && currentY == 3 && instance.currentPage < instance.pages) {
                    instance.StartCoroutine(instance.DownArrowCheck(propX, propY, currentX, currentY));
                }
                if (x == 0 && y == -1 && currentY == 0 && instance.currentPage > 0) {
                    instance.StartCoroutine(instance.UpArrowCheck(propX, propY, currentX, currentY));
                }
                return true;
            }
        }
    }
}
