using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine.Networking;
using UnityEngine;
using RiqMenu.Core;
using RiqMenu.Songs;

namespace RiqMenu.Audio
{
    /// <summary>
    /// Manages audio preview playback using async TempoSound to prevent main thread blocking
    /// </summary>
    public class AudioManager : MonoBehaviour, IRiqMenuSystem {
        public bool IsActive { get; private set; }

        private GameObject _previewSourceGO;
        private AudioSource _audioSource;   // Streaming path for on-demand preview
        private Coroutine _loadCoroutine;
        private string _currentTempPath;
        private CustomSong _currentPreviewSong;

        public bool IsPreviewPlaying  {
            get  {
                try {
                    if (_audioSource != null && _audioSource.isPlaying) return true;
                }
                catch { }
                return false;
            }
        }
        public CustomSong CurrentPreviewSong => _currentPreviewSong;

        public event System.Action<CustomSong> OnPreviewStarted;
        public event System.Action OnPreviewStopped;

        public void Initialize() {
            IsActive = true;
            // No preview playing at startup; remove any stale files
            DeleteAllStreamFilesExcept(null);
        }

        public void Cleanup() {
            StopPreview();
            if (_previewSourceGO != null) {
                Destroy(_previewSourceGO);
                _previewSourceGO = null;
            }
            IsActive = false;
        }

        public void Update() { }

        /// <summary>
        /// Play preview audio for a song using async TempoSound initialization
        /// </summary>
        public void PlayPreview(CustomSong song, float startTime = 0f) {
            StopPreview();

            // Create container if needed
            if (_previewSourceGO == null) {
                _previewSourceGO = new GameObject("RiqMenu_PreviewSource");
                DontDestroyOnLoad(_previewSourceGO);
            }

            _currentPreviewSong = song;

            // Always use streaming path for previews
            if (_audioSource == null) {
                _audioSource = _previewSourceGO.GetComponent<AudioSource>();
                if (_audioSource == null) _audioSource = _previewSourceGO.AddComponent<AudioSource>();
                _audioSource.loop = false;
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 0f;
            }
            _audioSource.enabled = true;
            _previewSourceGO.SetActive(true);

            float start = startTime > 0f ? startTime : 0f;
            _loadCoroutine = StartCoroutine(LoadAndPlayStreaming(song, start));
        }

        private IEnumerator LoadAndPlayStreaming(CustomSong song, float startTime) {
            // Extract audio from archive to a temp file off the main thread
            string uniq = System.Guid.NewGuid().ToString("N");
            string tempPath = Path.Combine(Application.temporaryCachePath, $"riqmenu_stream_{uniq}.bin");
            AudioType audioType = AudioType.UNKNOWN;
            bool done = false;
            Exception error = null;

            // Record temp path immediately so StopPreview can enqueue deletion if user navigates quickly
            _currentTempPath = tempPath;
            // Proactively remove any leftover streams from previous previews
            DeleteAllStreamFilesExcept(_currentTempPath);

            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                try {
                    using (var fs = File.Open(song.riq, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Read)) {
                        ZipArchiveEntry entry = null;
                        if (song.IsBopFile) {
                            entry = zip.GetEntry("song.bin");
                        }
                        else {
                            foreach (var e in zip.Entries) {
                                if (e.FullName.StartsWith("song", System.StringComparison.OrdinalIgnoreCase)) { entry = e; break; }
                            }
                        }
                        if (entry == null) throw new FileNotFoundException("song.* not found in archive", song.riq);
                        using (var es = entry.Open())
                        using (var outFs = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                            // Peek first bytes to detect type, then copy through
                            byte[] header = new byte[16];
                            int headerRead = es.Read(header, 0, header.Length);
                            if (headerRead > 0) {
                                outFs.Write(header, 0, headerRead);
                            }
                            // Detect based on header
                            if (headerRead >= 4 && Encoding.ASCII.GetString(header, 0, 4) == "OggS") audioType = AudioType.OGGVORBIS;
                            else if (headerRead >= 3 && Encoding.ASCII.GetString(header, 0, 3) == "ID3") audioType = AudioType.MPEG;
                            else if (headerRead >= 2 && header[0] == 255 && (header[1] == 251 || header[1] == 243 || header[1] == 242)) audioType = AudioType.MPEG;
                            else if (headerRead >= 12 && Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WAVE") audioType = AudioType.WAV;
                            else audioType = AudioType.UNKNOWN;

                            // Copy remainder
                            es.CopyTo(outFs);
                            outFs.Flush(true);
                        }
                    }
                    if (audioType == AudioType.UNKNOWN) {
                        throw new InvalidDataException("Unsupported or missing audio data in archive");
                    }
                }
                catch (Exception ex) { error = ex; }
                finally { done = true; }
            });

            while (!done) yield return null;
            if (error != null) {
                Debug.LogWarning($"[AudioManager] Streaming load failed: {error.Message}");
                // No active preview; remove any stream files (including the one we just created)
                DeleteAllStreamFilesExcept(null);
                _currentTempPath = null;
                yield break;
            }

            string uri = "file:///" + tempPath.Replace('\\', '/');
            using (var www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType)) {
                // Prefer streaming to avoid full decode upfront
                if (www.downloadHandler is DownloadHandlerAudioClip dh) {
                    dh.streamAudio = true;
                }
                var op = www.SendWebRequest();
                while (!op.isDone) yield return null;
                if (www.result != UnityWebRequest.Result.Success) {
                    Debug.LogWarning($"[AudioManager] Streaming request failed: {www.error}");
                    DeleteAllStreamFilesExcept(null);
                    _currentTempPath = null;
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null) {
                    Debug.LogWarning("[AudioManager] Streaming returned null clip");
                    DeleteAllStreamFilesExcept(null);
                    _currentTempPath = null;
                    yield break;
                }
                clip.name = Path.GetFileNameWithoutExtension(song.riq) + "_preview";

                _audioSource.clip = clip;
                try { _audioSource.time = Mathf.Clamp(startTime, 0f, _audioSource.clip.length - 0.01f); } catch { }
                _audioSource.Play();
                OnPreviewStarted?.Invoke(song);

                // Now that playback is running, keep only the current stream file
                DeleteAllStreamFilesExcept(_currentTempPath);
            }
        }

        /// <summary>
        /// Stop any currently playing preview
        /// </summary>
        public void StopPreview() {
            if (_loadCoroutine != null) {
                try { StopCoroutine(_loadCoroutine); } catch { }
                _loadCoroutine = null;
            }

            if (_audioSource != null) {
                try { _audioSource.Stop(); } catch { }
                if (_audioSource.clip != null) {
                    try { Destroy(_audioSource.clip); } catch { }
                    _audioSource.clip = null;
                }
            }

            // Nothing should be playing now; delete all stream files
            DeleteAllStreamFilesExcept(null);
            _currentTempPath = null;

            if (_previewSourceGO != null) {
                _previewSourceGO.SetActive(false);
            }

            var previousSong = _currentPreviewSong;
            _currentPreviewSong = null;

            if (previousSong != null) {
                OnPreviewStopped?.Invoke();
            }
        }

        /// <summary>
        /// Get current playback position as normalized value (0-1)
        /// </summary>
        public float GetPlaybackProgress() {
            if (_audioSource != null && _audioSource.clip != null && _audioSource.clip.length > 0f) {
                return Mathf.Clamp01(_audioSource.time / _audioSource.clip.length);
            }

            return 0f;
        }

        /// <summary>
        /// Seek to specific position in current preview
        /// </summary>
        public void SeekPreview(float normalizedPosition) {
            if (_audioSource != null && _audioSource.clip != null) {
                float targetTime = Mathf.Clamp01(normalizedPosition) * _audioSource.clip.length;
                try { _audioSource.time = targetTime; } catch { }
            }
        }

        private void DeleteAllStreamFilesExcept(string exceptFullPath) {
            try {
                var dir = Application.temporaryCachePath;
                if (!Directory.Exists(dir)) return;
                var files = Directory.GetFiles(dir, "riqmenu_stream_*.bin");
                foreach (var f in files) {
                    if (!string.IsNullOrEmpty(exceptFullPath) && string.Equals(f, exceptFullPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try {
                        if (File.Exists(f)) {
                            File.SetAttributes(f, FileAttributes.Normal);
                            File.Delete(f);
                        }
                    } catch { /* ignore per-file issues */ }
                }
            } catch { /* ignore directory issues */ }
        }
    }
}
