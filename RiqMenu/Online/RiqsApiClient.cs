using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace RiqMenu.Online
{
    /// <summary>
    /// Client for the Riqs & Mods API
    /// </summary>
    public class RiqsApiClient
    {
        private const string BaseUrl = "https://riqs.kabir.au";
        private const string UserAgent = "RiqMenu/1.0";

        static RiqsApiClient()
        {
            // Enable TLS 1.2 for HTTPS connections
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        }

        public delegate void SongsCallback(List<OnlineSong> songs, string error);
        public delegate void DownloadCallback(string filePath, string error);
        public delegate void DownloadProgressCallback(float progress);

        /// <summary>
        /// Search for songs asynchronously
        /// </summary>
        public void SearchSongs(string query, SongsCallback callback)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string url = $"{BaseUrl}/api/search?q={Uri.EscapeDataString(query)}&limit=50";
                    string json = FetchJson(url);
                    var songs = ParseSongsResponse(json);
                    callback(songs, null);
                }
                catch (Exception ex)
                {
                    callback(null, ex.Message);
                }
            });
        }

        /// <summary>
        /// Get latest/popular songs asynchronously
        /// </summary>
        public void GetSongs(string sort, int page, SongsCallback callback)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string url = $"{BaseUrl}/api/songs?sort={sort}&page={page}&limit=20";
                    string json = FetchJson(url);
                    var songs = ParseSongsResponse(json);
                    callback(songs, null);
                }
                catch (Exception ex)
                {
                    callback(null, ex.Message);
                }
            });
        }

        /// <summary>
        /// Download a song to the RiqMenu folder asynchronously
        /// </summary>
        public void DownloadSong(OnlineSong song, string destinationFolder, DownloadCallback callback, DownloadProgressCallback progressCallback = null)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Check if song already exists by hash
                    if (!string.IsNullOrEmpty(song.FileHash))
                    {
                        string existingFile = FindExistingByHash(destinationFolder, song.FileHash);
                        if (existingFile != null)
                        {
                            string existingMetaPath = existingFile + ".meta.json";
                            if (File.Exists(existingMetaPath))
                            {
                                // Already have file and metadata - just update download count
                                SaveSongMetadata(existingFile, song);
                                callback(existingFile, "Song already downloaded (metadata updated)");
                                return;
                            }
                            else
                            {
                                // Have file but no metadata - save it now
                                SaveSongMetadata(existingFile, song);
                                callback(existingFile, null);
                                return;
                            }
                        }
                    }

                    string url = $"{BaseUrl}/api/songs/{song.Id}/download";
                    string fileName = SanitizeFileName(song.DownloadFileName);
                    string filePath = Path.Combine(destinationFolder, fileName);

                    // Avoid overwriting - add number if exists
                    int counter = 1;
                    string baseFileName = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    while (File.Exists(filePath))
                    {
                        fileName = $"{baseFileName} ({counter}){ext}";
                        filePath = Path.Combine(destinationFolder, fileName);
                        counter++;
                    }

                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", UserAgent);

                        if (progressCallback != null)
                        {
                            client.DownloadProgressChanged += (s, e) =>
                            {
                                progressCallback(e.ProgressPercentage / 100f);
                            };
                        }

                        client.DownloadFile(url, filePath);
                    }

                    // Save metadata JSON alongside the song file
                    SaveSongMetadata(filePath, song);

                    callback(filePath, null);
                }
                catch (Exception ex)
                {
                    callback(null, ex.Message);
                }
            });
        }

        /// <summary>
        /// Save song metadata as a JSON file alongside the song
        /// </summary>
        private void SaveSongMetadata(string songFilePath, OnlineSong song)
        {
            try
            {
                string metaPath = songFilePath + ".meta.json";
                string json = $@"{{
  ""title"": ""{EscapeJsonString(song.Title)}"",
  ""artist"": ""{EscapeJsonString(song.Artist)}"",
  ""creator"": ""{EscapeJsonString(song.Creator)}"",
  ""uploaderName"": ""{EscapeJsonString(song.UploaderName)}"",
  ""bpm"": {(song.Bpm.HasValue ? song.Bpm.Value.ToString("F1") : "null")},
  ""duration"": {(song.Duration.HasValue ? song.Duration.Value.ToString("F1") : "null")},
  ""difficulty"": ""{EscapeJsonString(song.Difficulty)}"",
  ""downloadCount"": {song.DownloadCount},
  ""fileType"": ""{EscapeJsonString(song.FileType)}""
}}";
                File.WriteAllText(metaPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RiqsApiClient] Failed to save metadata: {ex.Message}");
            }
        }

        private string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        /// <summary>
        /// Check if a song with the given hash already exists in the folder
        /// </summary>
        private string FindExistingByHash(string folder, string hash)
        {
            if (!Directory.Exists(folder)) return null;

            foreach (var file in Directory.GetFiles(folder, "*.riq").Concat(Directory.GetFiles(folder, "*.bop")))
            {
                try
                {
                    // Check if filename contains the hash (stored files use hash as filename)
                    if (Path.GetFileNameWithoutExtension(file).Contains(hash))
                    {
                        return file;
                    }

                    // Also compute hash of existing files to check
                    using (var stream = File.OpenRead(file))
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                    {
                        var fileHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLower();
                        if (fileHash == hash.ToLower())
                        {
                            return file;
                        }
                    }
                }
                catch
                {
                    // Skip files we can't read
                }
            }
            return null;
        }

        private string FetchJson(string url)
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", UserAgent);
                    return client.DownloadString(url);
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse response)
                {
                    throw new Exception($"HTTP {(int)response.StatusCode}: {response.StatusDescription}");
                }
                throw;
            }
        }

        private List<OnlineSong> ParseSongsResponse(string json)
        {
            var songs = new List<OnlineSong>();

            // Parse the songs array from JSON
            // Looking for "songs":[...]
            var songsMatch = Regex.Match(json, @"""songs""\s*:\s*\[(.*?)\](?=\s*[,}])", RegexOptions.Singleline);
            if (!songsMatch.Success) return songs;

            string songsArray = songsMatch.Groups[1].Value;

            // Split into individual song objects
            var songMatches = Regex.Matches(songsArray, @"\{[^{}]*\}", RegexOptions.Singleline);

            foreach (Match songMatch in songMatches)
            {
                var song = ParseSongObject(songMatch.Value);
                if (song != null) songs.Add(song);
            }

            return songs;
        }

        private OnlineSong ParseSongObject(string json)
        {
            try
            {
                var song = new OnlineSong
                {
                    Id = ParseInt(json, "id"),
                    Title = ParseString(json, "title") ?? "Unknown",
                    Artist = ParseString(json, "artist"),
                    Creator = ParseString(json, "creator"),
                    Description = ParseString(json, "description"),
                    Filename = ParseString(json, "filename"),
                    FileType = ParseString(json, "fileType") ?? "riq",
                    FileSize = ParseInt(json, "fileSize"),
                    FileHash = ParseString(json, "fileHash"),
                    Bpm = ParseFloat(json, "bpm"),
                    Duration = ParseFloat(json, "duration"),
                    Difficulty = ParseString(json, "difficulty"),
                    GameTypes = ParseString(json, "gameTypes"),
                    DownloadCount = ParseInt(json, "downloadCount"),
                    UploaderName = ParseString(json, "uploaderName")
                };
                return song;
            }
            catch
            {
                return null;
            }
        }

        private string ParseString(string json, string key)
        {
            var match = Regex.Match(json, $@"""{key}""\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""");
            if (match.Success)
            {
                return Regex.Unescape(match.Groups[1].Value);
            }
            return null;
        }

        private int ParseInt(string json, string key)
        {
            var match = Regex.Match(json, $@"""{key}""\s*:\s*(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
            {
                return value;
            }
            return 0;
        }

        private float? ParseFloat(string json, string key)
        {
            var match = Regex.Match(json, $@"""{key}""\s*:\s*([\d.]+)");
            if (match.Success && float.TryParse(match.Groups[1].Value, out float value))
            {
                return value;
            }
            return null;
        }

        private string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            // Limit length
            if (name.Length > 100) name = name.Substring(0, 100);
            return name;
        }
    }
}
