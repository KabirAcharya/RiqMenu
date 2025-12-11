using System;
using System.IO;
using System.Linq;
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

            if (!Directory.Exists(path)) {
                Directory.CreateDirectory(path);
            }

            _fileNames = Directory.GetFiles(path)
                .Where(file => file.EndsWith(".riq", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".bop", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => Path.GetFileNameWithoutExtension(file))
                .ToArray();

            _songList = new CustomSong[_fileNames.Length];
            for (int i = 0; i < _fileNames.Length; i++) {
                _songList[i] = new CustomSong {
                    riq = _fileNames[i],
                    SongTitle = Path.GetFileNameWithoutExtension(_fileNames[i])
                };
            }
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
    }
}
