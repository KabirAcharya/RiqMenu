using System.Linq;
using System.IO;
using MelonLoader;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UI;
using System.Security.Cryptography.X509Certificates;

namespace RiqMenu {
    public class RiqMenuMain : MelonMod {
        private bool showMenu = false;
        private bool autoPlay = false;
        public string[] fileNames;

        private static RiqMenuMain instance;
        private static TitleScript titleScript;

        private static GameObject menuObject;

        private static CustomSongsScene customSongScene;

        public static List<string> newOptions = new List<string>() {
            "Play",
            "Custom Songs",
            "Settings",
            "Credits",
            "Keep Updated!",
            "Quit"
        };

        public static Scene currentScene;

        /*  private Vector2 scrollPosition;
          private GUIStyle windowStyle;
          private GUIStyle buttonStyle;
          private GUIStyle labelStyle;
          private GUIStyle toggleStyle;
          private GUIStyle toggleLabelStyle;*/

        public override void OnInitializeMelon() {
            if (instance == null) {
                instance = this;
            }

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
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public override void OnUpdate() {
            if (Input.GetKeyDown(KeyCode.F1)) {
                showMenu = !showMenu;
                Cursor.visible = showMenu;
            }
            if (Input.GetKeyDown(KeyCode.R)) {
                if (RiqLoader.path != null) {
                    TempoSceneManager.LoadScene(SceneKey.RiqLoader, false);
                }
            }

            if (currentScene.name == SceneKey.TitleScreen.ToString()) {

                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) {
                    FieldInfo prop = titleScript.GetType().GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance);
                    int selected = (int)prop.GetValue(titleScript);

                    LoggerInstance.Msg($"Selected index {newOptions[selected]}");
                    if (newOptions[selected] == "Custom Songs") {
                        // Show UI here
                        showMenu = !showMenu;
                        Cursor.visible = showMenu;
                        SetCustomSongMenuState(showMenu);
                    }
                }
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode) {
            if (loadSceneMode == LoadSceneMode.Single) {
                currentScene = scene;
            }

            if (scene.name == SceneKey.TitleScreen.ToString()) {
                TitleScript title = GameObject.Find("TitleScript").GetComponent<TitleScript>();
                titleScript = title;
                title.buildTypeText.text += " (<color=#ff0000>R</color><color=#ff7f00>i</color><color=#ffff00>q</color><color=#00ff00>M</color><color=#0000ff>e</color><color=#4b0082>n</color><color=#9400d3>u</color>)";

                menuObject = GameObject.Find("Menu");
                customSongScene = new CustomSongsScene(RiqMenuMain.instance.LoggerInstance);
                customSongScene.CustomSongSelected += RiqMenuMain.instance.CustomSongSelected;

                // Add custom option
                FieldInfo prop = title.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                prop.SetValue(title, newOptions);
            }
        }

        public void CustomSongSelected(string song) {
            LoggerInstance.Msg("Loading Riq: " + song);
            MelonCoroutines.Start(this.OnRiqSelected(song));
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

        public void SetCustomSongMenuState(bool state) {
            titleScript.enabled = !showMenu;
            menuObject.SetActive(!state);

            if (customSongScene == null) {
                LoggerInstance.Msg("WTF?");

            } else {
                customSongScene.SetContent(fileNames);
                customSongScene.SetVisible(state);
            }
            LoggerInstance.Msg($"Set SongPanel state to: {state}");
        }
    }
}
