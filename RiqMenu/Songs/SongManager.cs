using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using RiqMenu.Core;

namespace RiqMenu.Songs
{
    /// <summary>
    /// Manages custom song discovery, loading, and organization
    /// </summary>
    public class SongManager : MonoBehaviour, IRiqMenuSystem {
        public bool IsActive { get; private set; }

        private CustomSong[] _songList = new CustomSong[0];
        private string[] _fileNames = new string[0];

        public CustomSong[] SongList => _songList;
        public string[] FileNames => _fileNames;
        public int TotalSongs => _songList.Length;
        public int TotalRows => (int)Math.Ceiling((double)TotalSongs / 4);

        public event System.Action<CustomSong[]> OnSongsLoaded;

        private const int SONGS_PER_ROW = 4;

        public void Initialize() {
            Debug.Log("[SongManager] Initializing");
            LoadLocalSongs();
            IsActive = true;

            // Delay the event firing to allow subscriptions to be set up
            StartCoroutine(DelayedSongsLoadedEvent());
        }

        private System.Collections.IEnumerator DelayedSongsLoadedEvent() {
            yield return new WaitForEndOfFrame();
            OnSongsLoaded?.Invoke(_songList);
        }

        public void Cleanup() {
            IsActive = false;
            _songList = new CustomSong[0];
            _fileNames = new string[0];
        }

        public void Update() {
            // Song manager doesn't need constant updates
        }

        private void LoadLocalSongs() {
            string path = Path.Combine(Application.dataPath, "StreamingAssets", "RiqMenu");
            Debug.Log($"[SongManager] Loading songs from: {path}");

            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
                Debug.Log($"[SongManager] Created directory: {path}");
            }

            _fileNames = Directory.GetFiles(path)
                .Where(file => file.EndsWith(".riq", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".bop", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => Path.GetFileNameWithoutExtension(file))
                .ToArray();

            Debug.Log($"[SongManager] Found {_fileNames.Length} songs");

            _songList = new CustomSong[_fileNames.Length];
            for (int i = 0; i < _fileNames.Length; i++) {
                var metadata = ExtractMetadata(_fileNames[i]);

                _songList[i] = new CustomSong {
                    riq = _fileNames[i],
                    SongTitle = metadata.title ?? Path.GetFileNameWithoutExtension(_fileNames[i]),
                    Creator = metadata.author,
                    Bpm = metadata.bpm,
                    DownloadCount = metadata.downloadCount,
                    Difficulty = metadata.difficulty
                };
            }
        }

        private (string title, string author, float? bpm, int? downloadCount, string difficulty) ExtractMetadata(string filePath) {
            string title = null;
            string author = null;
            float? bpm = null;
            int? downloadCount = null;
            string difficulty = null;

            // First, check for .meta.json file (saved from online downloads)
            string metaPath = filePath + ".meta.json";
            if (File.Exists(metaPath)) {
                try {
                    string json = File.ReadAllText(metaPath, Encoding.UTF8);
                    title = ParseJsonField(json, "title");
                    // Try creator first (uploader), then artist (original artist)
                    author = ParseJsonField(json, "creator") ?? ParseJsonField(json, "uploaderName") ?? ParseJsonField(json, "artist");
                    bpm = ParseJsonFloat(json, "bpm");
                    downloadCount = ParseJsonInt(json, "downloadCount");
                    difficulty = ParseJsonField(json, "difficulty");
                    if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(author)) {
                        return (title, author, bpm, downloadCount, difficulty);
                    }
                }
                catch (Exception ex) {
                    Debug.LogWarning($"[SongManager] Failed to read meta file: {ex.Message}");
                }
            }

            // Fallback: extract from archive
            try {
                using (var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read)) {
                    // Try to find metadata in data.json (common .riq format)
                    var dataEntry = zip.GetEntry("data.json");
                    if (dataEntry != null) {
                        using (var stream = dataEntry.Open())
                        using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                            string json = reader.ReadToEnd();
                            title = ParseJsonField(json, "title");
                            author = ParseJsonField(json, "author") ?? ParseJsonField(json, "remixer");
                        }
                    }

                    // If no author found, try level.json
                    if (string.IsNullOrEmpty(author)) {
                        var levelEntry = zip.GetEntry("level.json");
                        if (levelEntry != null) {
                            using (var stream = levelEntry.Open())
                            using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                                string json = reader.ReadToEnd();
                                if (string.IsNullOrEmpty(title)) title = ParseJsonField(json, "title");
                                author = ParseJsonField(json, "author") ?? ParseJsonField(json, "remixer");
                            }
                        }
                    }
                }
            }
            catch (Exception ex) {
                Debug.LogWarning($"[SongManager] Failed to extract metadata from {Path.GetFileName(filePath)}: {ex.Message}");
            }

            return (title, author, bpm, downloadCount, difficulty);
        }

        private string ParseJsonField(string json, string fieldName) {
            // Simple regex-based JSON field extraction (avoids dependency on JSON library)
            var pattern = $"\"{fieldName}\"\\s*:\\s*\"([^\"]+)\"";
            var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1) {
                return match.Groups[1].Value;
            }
            return null;
        }

        private float? ParseJsonFloat(string json, string fieldName) {
            var pattern = $"\"{fieldName}\"\\s*:\\s*([\\d.]+)";
            var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (match.Success && float.TryParse(match.Groups[1].Value, out float value)) {
                return value;
            }
            return null;
        }

        private int? ParseJsonInt(string json, string fieldName) {
            var pattern = $"\"{fieldName}\"\\s*:\\s*(\\d+)";
            var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int value)) {
                return value;
            }
            return null;
        }

        /// <summary>
        /// Get song by index
        /// </summary>
        public CustomSong GetSong(int index) {
            if (index >= 0 && index < _songList.Length) {
                return _songList[index];
            }
            return null;
        }

        /// <summary>
        /// Get songs for a specific row (4 songs per row)
        /// </summary>
        public CustomSong[] GetSongsForRow(int row) {
            int startIndex = row * SONGS_PER_ROW;
            int count = Math.Min(SONGS_PER_ROW, _songList.Length - startIndex);

            if (count <= 0) return new CustomSong[0];

            CustomSong[] rowSongs = new CustomSong[count];
            Array.Copy(_songList, startIndex, rowSongs, 0, count);
            return rowSongs;
        }

        /// <summary>
        /// Get song index from row and column position
        /// </summary>
        public int GetSongIndex(int row, int column) {
            return row * SONGS_PER_ROW + column;
        }

        /// <summary>
        /// Reload songs from disk (called after downloading new songs)
        /// </summary>
        public void ReloadSongs() {
            Debug.Log("[SongManager] Reloading songs...");
            LoadLocalSongs();
            OnSongsLoaded?.Invoke(_songList);
        }

        /// <summary>
        /// Save metadata for a song to a .meta.json file
        /// </summary>
        public bool SaveMetadata(CustomSong song, string title, string creator, float? bpm, string difficulty) {
            if (song == null || string.IsNullOrEmpty(song.riq)) return false;

            try {
                string metaPath = song.riq + ".meta.json";

                // Build JSON manually to avoid dependencies
                var sb = new StringBuilder();
                sb.AppendLine("{");

                bool first = true;
                void AddField(string name, string value) {
                    if (string.IsNullOrEmpty(value)) return;
                    if (!first) sb.AppendLine(",");
                    first = false;
                    sb.Append($"  \"{name}\": \"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
                }
                void AddNumericField(string name, float value) {
                    if (!first) sb.AppendLine(",");
                    first = false;
                    sb.Append($"  \"{name}\": {value}");
                }

                AddField("title", title);
                AddField("creator", creator);
                if (bpm.HasValue) AddNumericField("bpm", bpm.Value);
                AddField("difficulty", difficulty);

                sb.AppendLine();
                sb.AppendLine("}");

                File.WriteAllText(metaPath, sb.ToString(), Encoding.UTF8);
                Debug.Log($"[SongManager] Saved metadata to {metaPath}");

                // Update the song object in memory
                song.SongTitle = title ?? song.SongTitle;
                song.Creator = creator ?? song.Creator;
                song.Bpm = bpm ?? song.Bpm;
                song.Difficulty = difficulty ?? song.Difficulty;

                return true;
            }
            catch (Exception ex) {
                Debug.LogError($"[SongManager] Failed to save metadata: {ex.Message}");
                return false;
            }
        }
    }
}
