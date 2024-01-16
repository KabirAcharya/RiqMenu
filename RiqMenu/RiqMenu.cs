using System.Linq;
using System.IO;
using MelonLoader;
using UnityEngine;
using System.Collections;

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
            fileStyleNormal = new GUIStyle(GUI.skin.button)
            {
                normal = { textColor = Color.white, background = Texture2D.blackTexture },
                hover = { textColor = Color.black, background = Texture2D.whiteTexture },
                alignment = TextAnchor.MiddleLeft
            };
            fileStyleHover = new GUIStyle(fileStyleNormal)
            {
                normal = { textColor = Color.black, background = Texture2D.whiteTexture }
            };

            if (showMenu)
            {
                GUILayout.Window(0, new Rect(100, 100, 200, 100), RiqShowMenu, "F1 Menu");
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
                GUIStyle currentStyle = buttonRect.Contains(Event.current.mousePosition) ? fileStyleHover : fileStyleNormal;

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
