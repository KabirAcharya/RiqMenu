using System.Linq;
using System.IO;
using MelonLoader;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Reflection;

namespace RiqMenu {
    public class RiqMenuMain : MelonMod {
        private bool showMenu = false;
        private bool autoPlay = false;
        public string[] fileNames;

        private static RiqMenuMain instance;
        private static TitleScript titleScript;

        public static List<string> newOptions = new List<string>() {
            "Play",
            "Custom Songs",
            "Settings",
            "Credits",
            "Keep Updated!",
            "Quit"
        };

        public static Scene currentScene;

        private Vector2 scrollPosition;
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;
        private GUIStyle toggleStyle;
        private GUIStyle toggleLabelStyle;

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

                if (Input.GetKeyDown(KeyCode.Return)) {
                    FieldInfo prop = titleScript.GetType().GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance);
                    int selected = (int)prop.GetValue(titleScript);

                    LoggerInstance.Msg($"Selected index {newOptions[selected]}");
                    if (newOptions[selected] == "Custom Songs") {
                        // Show UI here
                        showMenu = !showMenu;
                        Cursor.visible = showMenu;
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

                // Add custom option
                FieldInfo prop = title.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                prop.SetValue(title, newOptions);
            }
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

        public override void OnGUI() {
            Texture2D blackTexture = new Texture2D(1, 1);
            blackTexture.SetPixel(0, 0, Color.black);
            blackTexture.Apply();

            Texture2D whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();

            Texture2D blackSquareInWhite = new Texture2D(15, 15);
            for (int x = 0; x < 15; x++) {
                for (int y = 0; y < 15; y++) {
                    if (x < 3 || y < 4 || x > 10 || y > 11) {
                        blackSquareInWhite.SetPixel(x, y, Color.white);
                    } else {
                        blackSquareInWhite.SetPixel(x, y, Color.black);
                    }
                }
            }
            blackSquareInWhite.Apply();

            buttonStyle = new GUIStyle(GUI.skin.button) {
                normal = { textColor = Color.white, background = blackTexture },
                hover = { textColor = Color.black, background = whiteTexture },
                active = { textColor = Color.black, background = whiteTexture },
                onHover = { textColor = Color.black, background = whiteTexture },
                onActive = { textColor = Color.black, background = whiteTexture },
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0)
            };

            windowStyle = new GUIStyle(GUI.skin.window) {
                focused = { textColor = Color.white, background = blackTexture },
                normal = { textColor = Color.white, background = blackTexture },
                hover = { textColor = Color.white, background = blackTexture },
                active = { textColor = Color.white, background = blackTexture },
                onFocused = { textColor = Color.white, background = blackTexture },
                onNormal = { textColor = Color.white, background = blackTexture },
                onHover = { textColor = Color.white, background = blackTexture },
                onActive = { textColor = Color.white, background = blackTexture },
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };

            labelStyle = new GUIStyle(GUI.skin.label) {
                normal = { textColor = Color.cyan, background = blackTexture },
                alignment = TextAnchor.MiddleCenter
            };

            toggleStyle = new GUIStyle(GUI.skin.toggle) {
                normal = { background = whiteTexture },
                hover = { background = blackSquareInWhite },
                focused = { background = blackSquareInWhite },
                active = { background = blackSquareInWhite },
                onNormal = { background = blackSquareInWhite },
                onHover = { background = blackSquareInWhite },
                onFocused = { background = blackSquareInWhite },
                onActive = { background = blackSquareInWhite },
                fixedHeight = 15,
                fixedWidth = 15,

                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };

            toggleLabelStyle = new GUIStyle(GUI.skin.label) {
                normal = { textColor = Color.white, background = blackTexture },
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(4, 0, 2, 2)
            };

            if (showMenu) {
                GUILayout.Window(0, new Rect(100, 100, 200, 100), RiqShowMenu, "", windowStyle);
            }
        }

        void RiqShowMenu(int windowID) {
            GUILayout.Label("RiqMenu", labelStyle);

            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            autoPlay = GUILayout.Toggle(autoPlay, "", toggleStyle, GUILayout.Width(20), GUILayout.Height(20));
            GUILayout.Label("Autoplay", toggleLabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Width(280), GUILayout.Height(250));
            foreach (string fileName in fileNames) {
                string shortName = Path.GetFileName(fileName);
                Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent(shortName), buttonStyle);

                if (GUI.Button(buttonRect, shortName, buttonStyle)) {
                    LoggerInstance.Msg("Loading Riq: " + shortName);
                    MelonCoroutines.Start(this.OnRiqSelected(fileName));
                }
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("Close Menu", buttonStyle)) {
                showMenu = false;
                Cursor.visible = false;
            }
        }
    }
}
