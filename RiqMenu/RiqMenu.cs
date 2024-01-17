using System.Linq;
using System.IO;
using MelonLoader;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

namespace RiqMenu
{
    public class RiqMenuMain : MelonMod
    {
        private bool showMenu = false;
        private bool autoPlay = false;
        private string[] fileNames;
        private Vector2 scrollPosition;

        private GUIStyle fileStyleNormal;
        private GUIStyle fileStyleHover;

        public override void OnInitializeMelon()
        {
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

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                showMenu = !showMenu;
                Cursor.visible = showMenu;
            }
        }

        public override void OnGUI()
        {
            Texture2D blackTexture = new Texture2D(1, 1);
            blackTexture.SetPixel(0, 0, new Color(0, 0, 0, 1));
            blackTexture.Apply();

            Texture2D whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, new Color(1, 1, 1, 1));
            whiteTexture.Apply();
            
            fileStyleNormal = new GUIStyle(GUI.skin.button)
            {
                normal = { textColor = Color.white, background = blackTexture },
                hover = { textColor = Color.black, background = whiteTexture },
                alignment = TextAnchor.MiddleLeft,
                border = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0)
            };

            if (showMenu)
            {
                GUILayout.Window(0, new Rect(100, 100, 200, 100), RiqShowMenu, "RiqMenu");
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
        {
            if (scene.name == SceneKey.TitleScreen.ToString())
            {
                TitleScript title = GameObject.Find("TitleScript").GetComponent<TitleScript>();
                title.buildTypeText.text += " (<color=#ff0000>R</color><color=#ff7f00>i</color><color=#ffff00>q</color><color=#00ff00>M</color><color=#0000ff>e</color><color=#4b0082>n</color><color=#9400d3>u</color>)";
            }
        }

        void RiqShowMenu(int windowID)
        {
            autoPlay = GUILayout.Toggle(autoPlay, "Autoplay");

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Width(280), GUILayout.Height(250));
            foreach (string fileName in fileNames)
            {
                string shortName = Path.GetFileName(fileName);
                Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent(shortName), fileStyleNormal);
                GUIStyle currentStyle = fileStyleNormal;

                if (GUI.Button(buttonRect, shortName, currentStyle))
                {
                    LoggerInstance.Msg("Loading Riq: " + shortName);
                    MelonCoroutines.Start(this.OnRiqSelected(fileName));
                }
            }

            GUILayout.EndScrollView();

            if (GUILayout.Button("Close Menu"))
            {
                showMenu = false;
                Cursor.visible = false;
            }
        }

        IEnumerator OnRiqSelected(string fileName)
        {
            RiqLoader.path = fileName;
            MixtapeLoader.autoplay = autoPlay;
            showMenu = false;
            Cursor.visible = false;
            CameraScript cameraScript = GameObject.Find("Main Camera").GetComponent<CameraScript>();
            yield return cameraScript.FadeOut(0.1f);
            yield return new WaitForSeconds(0.9f);
            TempoSceneManager.LoadScene(SceneKey.RiqLoader, false);
        }
    }
}
