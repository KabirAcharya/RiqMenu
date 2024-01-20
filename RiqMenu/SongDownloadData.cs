using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Policy;
using System.Threading.Tasks;
using UnityEngine;

namespace RiqMenu {

    public class SongDownloadData {

        private const string CustomSongURL = "https://raw.githubusercontent.com/ZeppelinGames/bitsnbops-custom-songs/main/songs.json";
        MelonLoader.MelonLogger.Instance logger;

        public SongDownloadData(MelonLoader.MelonLogger.Instance logger = null) {
            this.logger = logger;
        }

        public async Task RetrieveSongData(Action<List<CustomSong>> callback = null) {
            try {
                string jsonContent = await DownloadJsonAsync(CustomSongURL);

                if (!string.IsNullOrEmpty(jsonContent)) {
                    List<CustomSong> result = JsonConvert.DeserializeObject<List<CustomSong>>(jsonContent);
                    callback?.Invoke(result);
                } else {
                    callback?.Invoke(new List<CustomSong>());
                }
            } catch (Exception ex) {
                callback?.Invoke(new List<CustomSong>());
            }
        }

        public async Task DownloadSong(CustomSong song, Action<bool> callback = null) {
            string path = Path.Combine(Application.dataPath, "StreamingAssets", song.SongTitle);
            logger?.Msg($"Trying to download {song.riq}");

            using (HttpClient httpClient = new HttpClient()) {
                try {
                    using (HttpResponseMessage response = await httpClient.GetAsync(song.riq, HttpCompletionOption.ResponseHeadersRead)) {
                        response.EnsureSuccessStatusCode(); // Ensure a successful response

                        using (Stream streamToReadFrom = await response.Content.ReadAsStreamAsync()) {
                            using (Stream streamToWriteTo = File.Open(path, FileMode.Create)) {
                                await streamToReadFrom.CopyToAsync(streamToWriteTo);
                            }
                        }
                    }
                } catch (HttpRequestException ex) {
                    logger?.Msg(ex);
                }
            }
        }

        async Task<string> DownloadJsonAsync(string url) {
            using (HttpClient httpClient = new HttpClient()) {
                HttpResponseMessage response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode) {
                    return await response.Content.ReadAsStringAsync();
                } else {
                    return null;
                }
            }
        }
    }
}
