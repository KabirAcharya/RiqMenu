using System.Linq;
using System.IO;
using MelonLoader;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Reflection;
using System.Net;
using MelonLoader.TinyJSON;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Security.Policy;
using System;
using System.Net.Http;

namespace RiqMenu {
    public class RiqMenuMain : MelonMod {
        private bool showMenu = false;
        private bool autoPlay = false;
        public string[] fileNames;

        private static RiqMenuMain instance;
        private static TitleScript titleScript;

        private static GameObject menuObject;

        private static CustomSongsScene customSongScene;
        private static CustomSongsScene getCustomSongScene;

        private static SongDownloadData songDownload;

        private CustomSong[] songList = new CustomSong[0];
        private List<CustomSong> downloadableSongList = new List<CustomSong>();

        public static List<string> newOptions = new List<string>() {
            "Play",
            "Custom Songs",
            "Get Custom Songs",
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

            songList = new CustomSong[fileNames.Length];
            for (int i = 0; i < fileNames.Length; i++) {
                songList[i] = new CustomSong();
                songList[i].riq = fileNames[i];
                songList[i].SongTitle = Path.GetFileName(fileNames[i]);
            }
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
                        ToggleCustomMenu(customSongScene);
                    }

                    if (newOptions[selected] == "Get Custom Songs") {
                        // Show UI here
                        ToggleCustomMenu(getCustomSongScene);
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

                customSongScene = new CustomSongsScene();
                customSongScene.SetContent(RiqMenuMain.instance.songList);
                customSongScene.CustomSongSelected += RiqMenuMain.instance.CustomSongSelected;

                getCustomSongScene = new CustomSongsScene();
                // Load songs from file
                _ = songDownload.RetrieveSongData((List<CustomSong> data) => {
                    RiqMenuMain.instance.LoggerInstance.Msg($"{data.Count} songs found");
                    RiqMenuMain.instance.downloadableSongList = data;
                    
                    getCustomSongScene.SetContent(data.ToArray());
                });
                getCustomSongScene.CustomSongSelected += (CustomSong song) => {
                    if (RiqMenuMain.instance.downloadableSongList != null && RiqMenuMain.instance.downloadableSongList.Count > 0) {
                        RiqMenuMain.instance.LoggerInstance.Msg("Song downloading...");
                        _ = songDownload.DownloadSong(song, (bool b) => {
                            RiqMenuMain.instance.LoggerInstance.Msg("Song download " + (b ? "complete" : "failed") + $". Song {song.SongTitle}");
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
    }
}
