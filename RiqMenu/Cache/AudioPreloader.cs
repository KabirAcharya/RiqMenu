using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using RiqMenu.Core;
using RiqMenu.Songs;

namespace RiqMenu.Cache
{
    /// <summary>
    /// Manages audio preloading directly into RAM with progress tracking
    /// </summary>
    public class AudioPreloader : MonoBehaviour, IRiqMenuSystem
    {
        public bool IsActive { get; private set; }
        
        private bool _isPreloading = false;
        
        public bool IsPreloading => _isPreloading;
        public int TotalFilesToPreload { get; private set; }
        public int FilesProcessed { get; private set; }
        public string CurrentProcessingFile { get; private set; }
        
        public event System.Action<int, int, string> OnPreloadProgress;
        public event System.Action OnPreloadComplete;

        public void Initialize()
        {
            IsActive = true;
            Debug.Log($"[AudioPreloader] Initialized");
        }

        public void Cleanup()
        {
            IsActive = false;
        }

        public void Update()
        {
        }

        /// <summary>
        /// Start the full preload process: load all songs directly into RAM
        /// </summary>
        public void CheckAndStartPreloading(CustomSong[] songs)
        {
            if (_isPreloading) return;

            Debug.Log($"[AudioPreloader] Starting preload process for {songs.Length} songs");

            TotalFilesToPreload = songs.Length;
            FilesProcessed = 0;
            CurrentProcessingFile = "Initializing...";
            
            OnPreloadProgress?.Invoke(0, songs.Length, "Initializing...");
            
            var uiManager = RiqMenuSystemManager.Instance?.UIManager;
            uiManager?.ShowLoadingProgress();
            
            StartCoroutine(FullPreloadProcess(songs));
        }

        /// <summary>
        /// Check if song audio is loaded in RAM
        /// </summary>
        public void LoadSongAudio(CustomSong song, System.Action<bool> onComplete = null)
        {
            if (song == null)
            {
                onComplete?.Invoke(false);
                return;
            }

            // Only succeed if already loaded in RAM
            if (song.audioClip != null)
            {
                onComplete?.Invoke(true);
                return;
            }

            Debug.LogWarning($"[AudioPreloader] Song {song.SongTitle} not preloaded in RAM");
            onComplete?.Invoke(false);
        }

        /// <summary>
        /// Full preload process: load all songs directly into RAM
        /// </summary>
        private IEnumerator FullPreloadProcess(CustomSong[] songs)
        {
            _isPreloading = true;
            yield return new WaitForSeconds(0.1f);
            
            Debug.Log($"[AudioPreloader] Loading {songs.Length} songs into RAM");
            TotalFilesToPreload = songs.Length;
            FilesProcessed = 0;
            CurrentProcessingFile = "Loading into memory...";
            OnPreloadProgress?.Invoke(0, songs.Length, "Loading into memory...");
            yield return null;
            
            for (int i = 0; i < songs.Length; i++)
            {
                var song = songs[i];
                CurrentProcessingFile = song.SongTitle;
                OnPreloadProgress?.Invoke(i, songs.Length, song.SongTitle);
                
                if (song.audioClip != null)
                {
                    FilesProcessed++;
                    continue;
                }
                
                bool loadComplete = false;
                StartCoroutine(LoadSongAudioDirect(song, (success) => {
                    loadComplete = true;
                }));
                
                while (!loadComplete)
                {
                    yield return null;
                }
                
                FilesProcessed++;
                OnPreloadProgress?.Invoke(i + 1, songs.Length, song.SongTitle);
                
                if (i % 2 == 0)
                {
                    yield return null;
                }
            }
            
            _isPreloading = false;
            CurrentProcessingFile = "Complete!";
            OnPreloadProgress?.Invoke(songs.Length, songs.Length, "Complete!");
            yield return new WaitForSeconds(0.5f);
            
            Debug.Log($"[AudioPreloader] ✅ Preload complete! All {songs.Length} songs loaded into RAM");
            OnPreloadComplete?.Invoke();
        }

        private IEnumerator LoadSongAudioDirect(CustomSong song, System.Action<bool> onComplete = null)
        {
            if (song == null)
            {
                onComplete?.Invoke(false);
                yield break;
            }

            if (song.audioClip != null)
            {
                onComplete?.Invoke(true);
                yield break;
            }

            yield return StartCoroutine(LoadFromArchiveAsync(song.riq, song, onComplete));
        }


        private IEnumerator LoadFromArchiveAsync(string archivePath, CustomSong song, System.Action<bool> onComplete)
        {
            byte[] audioData = null;
            AudioType audioType = AudioType.UNKNOWN;
            bool loadComplete = false;
            Exception loadError = null;
            
            ThreadPool.QueueUserWorkItem((_) => {
                try
                {
                    if (!File.Exists(archivePath))
                    {
                        loadError = new FileNotFoundException($"Archive not found: {archivePath}");
                        return;
                    }
                    
                    using (FileStream fileStream = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        using (ZipArchive zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                        {
                            ZipArchiveEntry entry = FindSongEntry(zipArchive, song.IsBopFile);
                            
                            if (entry != null)
                            {
                                using (Stream stream = entry.Open())
                                {
                                    using (MemoryStream memoryStream = new MemoryStream())
                                    {
                                        stream.CopyTo(memoryStream);
                                        audioData = memoryStream.ToArray();
                                    }
                                }
                                audioType = DetectAudioType(audioData);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    loadError = ex;
                }
                finally
                {
                    loadComplete = true;
                }
            });
            
            while (!loadComplete)
            {
                yield return null;
            }
            
            if (loadError != null)
            {
                Debug.LogError($"[AudioPreloader] Failed to load from archive: {loadError.Message}");
                onComplete?.Invoke(false);
                yield break;
            }
            
            if (audioData == null || audioType == AudioType.UNKNOWN)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            
            yield return StartCoroutine(CreateAudioClip(audioData, audioType, (clip) => {
                if (clip != null)
                {
                    song.audioClip = clip;
                    onComplete?.Invoke(true);
                }
                else
                {
                    onComplete?.Invoke(false);
                }
            }));
        }


        private IEnumerator CreateAudioClip(byte[] audioData, AudioType audioType, System.Action<AudioClip> callback)
        {
            string tempPath = Path.Combine(Application.temporaryCachePath, $"riqmenu_temp_{System.Threading.Thread.CurrentThread.ManagedThreadId}_{DateTime.Now.Ticks}.bin");
            
            bool writeComplete = false;
            Exception writeError = null;
            
            ThreadPool.QueueUserWorkItem((_) => {
                try
                {
                    File.WriteAllBytes(tempPath, audioData);
                }
                catch (Exception ex)
                {
                    writeError = ex;
                }
                finally
                {
                    writeComplete = true;
                }
            });
            
            while (!writeComplete)
            {
                yield return null;
            }
            
            if (writeError != null)
            {
                callback?.Invoke(null);
                yield break;
            }
            
            bool loadComplete = false;
            AudioClip resultClip = null;
            
            StartCoroutine(LoadAudioClipNonBlocking(tempPath, audioType, (clip) => {
                resultClip = clip;
                loadComplete = true;
            }));
            
            while (!loadComplete)
            {
                yield return null;
            }
            
            ThreadPool.QueueUserWorkItem((_) => {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AudioPreloader] Failed to delete temp file: {ex.Message}");
                }
            });
            
            callback?.Invoke(resultClip);
        }
        
        private IEnumerator LoadAudioClipNonBlocking(string filePath, AudioType audioType, System.Action<AudioClip> callback)
        {
            string uri = "file:///" + filePath.Replace('\\', '/');
            
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                www.timeout = 30;
                var operation = www.SendWebRequest();
                
                while (!operation.isDone)
                {
                    yield return null;
                }
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip != null)
                    {
                        clip.name = Path.GetFileNameWithoutExtension(filePath);
                    }
                    callback?.Invoke(clip);
                }
                else
                {
                    Debug.LogError($"[AudioPreloader] Failed to load audio: {www.error}");
                    callback?.Invoke(null);
                }
            }
        }

        private ZipArchiveEntry FindSongEntry(ZipArchive archive, bool isBopFile = false)
        {
            if (isBopFile)
            {
                // .bop files use "song.bin" for audio
                return archive.GetEntry("song.bin");
            }
            
            // .riq files can have "song.ogg" or "song.bin" depending on version
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("song", StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        private AudioType DetectAudioType(byte[] data)
        {
            if (data.Length >= 4 && Encoding.ASCII.GetString(data, 0, 4) == "OggS")
            {
                return AudioType.OGGVORBIS;
            }
            else if (data.Length >= 3 && Encoding.ASCII.GetString(data, 0, 3) == "ID3")
            {
                return AudioType.MPEG;
            }
            else if (data.Length >= 2 && data[0] == 255 && (data[1] == 251 || data[1] == 243 || data[1] == 242))
            {
                return AudioType.MPEG;
            }
            else if (data.Length >= 12 && 
                Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && 
                Encoding.ASCII.GetString(data, 8, 4) == "WAVE")
            {
                return AudioType.WAV;
            }
            
            return AudioType.UNKNOWN;
        }

    }
}