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
    /// Manages audio caching and RAM preloading with progress tracking
    /// </summary>
    public class CacheManager : MonoBehaviour, IRiqMenuSystem
    {
        public bool IsActive { get; private set; }
        
        private string _audioCacheDir;
        private bool _isCaching = false;
        
        public bool IsCaching => _isCaching;
        public int TotalFilesToCache { get; private set; }
        public int FilesProcessed { get; private set; }
        public string CurrentProcessingFile { get; private set; }
        
        public event System.Action<int, int, string> OnCacheProgress;
        public event System.Action OnCacheComplete;

        public void Initialize()
        {
            _audioCacheDir = Path.Combine(Application.temporaryCachePath, "RiqMenu_AudioCache");
            if (!Directory.Exists(_audioCacheDir))
            {
                Directory.CreateDirectory(_audioCacheDir);
            }
            IsActive = true;
            Debug.Log($"[CacheManager] Initialized with cache dir: {_audioCacheDir}");
        }

        public void Cleanup()
        {
            CleanupOldCacheFiles();
            IsActive = false;
        }

        public void Update()
        {
        }

        /// <summary>
        /// Start the full preload process: extract to cache, then load all songs into RAM
        /// </summary>
        public void CheckAndStartCaching(CustomSong[] songs)
        {
            if (_isCaching) return;

            Debug.Log($"[CacheManager] Starting preload process for {songs.Length} songs");

            TotalFilesToCache = songs.Length;
            FilesProcessed = 0;
            CurrentProcessingFile = "Initializing...";
            
            OnCacheProgress?.Invoke(0, songs.Length, "Initializing...");
            
            var uiManager = RiqMenuSystemManager.Instance?.UIManager;
            uiManager?.ShowLoadingProgress();
            
            StartCoroutine(FullPreloadProcess(songs));
        }

        /// <summary>
        /// Check if song audio is loaded in RAM (individual loading disabled)
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

            Debug.LogWarning($"[CacheManager] Song {song.SongTitle} not preloaded in RAM");
            onComplete?.Invoke(false);
        }

        /// <summary>
        /// Full preload process: extract audio to cache, then load all songs into RAM
        /// </summary>
        private IEnumerator FullPreloadProcess(CustomSong[] songs)
        {
            _isCaching = true;
            yield return new WaitForSeconds(0.1f);
            
            List<CustomSong> filesToCache = new List<CustomSong>();
            
            CurrentProcessingFile = "Checking cache...";
            OnCacheProgress?.Invoke(0, songs.Length, "Checking cache...");
            yield return null;
            
            foreach (var song in songs)
            {
                string cacheFileName = Path.GetFileNameWithoutExtension(song.riq) + ".audio";
                string cachedPath = Path.Combine(_audioCacheDir, cacheFileName);
                
                if (!File.Exists(cachedPath))
                {
                    filesToCache.Add(song);
                }
            }
            
            if (filesToCache.Count > 0)
            {
                Debug.Log($"[CacheManager] Phase 1: Extracting {filesToCache.Count} audio files");
                TotalFilesToCache = filesToCache.Count;
                FilesProcessed = 0;
                CurrentProcessingFile = "Extracting audio...";
                OnCacheProgress?.Invoke(0, filesToCache.Count, "Extracting audio...");
                yield return StartCoroutine(ExtractAudioFilesWithProgress(filesToCache.ToArray()));
            }
            else
            {
                Debug.Log("[CacheManager] Phase 1: All audio files already cached");
            }
            
            Debug.Log($"[CacheManager] Phase 2: Loading {songs.Length} songs into RAM");
            TotalFilesToCache = songs.Length;
            FilesProcessed = 0;
            CurrentProcessingFile = "Loading into memory...";
            OnCacheProgress?.Invoke(0, songs.Length, "Loading into memory...");
            yield return null;
            
            for (int i = 0; i < songs.Length; i++)
            {
                var song = songs[i];
                CurrentProcessingFile = song.SongTitle;
                OnCacheProgress?.Invoke(i, songs.Length, song.SongTitle);
                
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
                OnCacheProgress?.Invoke(i + 1, songs.Length, song.SongTitle);
                
                if (i % 2 == 0)
                {
                    yield return null;
                }
            }
            
            _isCaching = false;
            CurrentProcessingFile = "Complete!";
            OnCacheProgress?.Invoke(songs.Length, songs.Length, "Complete!");
            yield return new WaitForSeconds(0.5f);
            
            Debug.Log($"[CacheManager] ✅ Preload complete! All {songs.Length} songs loaded into RAM");
            OnCacheComplete?.Invoke();
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

            string cacheFileName = Path.GetFileNameWithoutExtension(song.riq) + ".audio";
            string cachedPath = Path.Combine(_audioCacheDir, cacheFileName);

            if (File.Exists(cachedPath))
            {
                yield return StartCoroutine(LoadFromCacheAsync(cachedPath, song, onComplete));
            }
            else
            {
                yield return StartCoroutine(LoadFromArchiveAsync(song.riq, song, onComplete));
            }
        }

        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(_audioCacheDir))
                {
                    var files = Directory.GetFiles(_audioCacheDir);
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CacheManager] Failed to clear cache: {ex.Message}");
            }
        }

        private IEnumerator ExtractAudioFilesWithProgress(CustomSong[] filesToCache)
        {
            bool extractionComplete = false;
            Exception extractionError = null;
            
            ThreadPool.QueueUserWorkItem((_) => {
                try {
                    for (int i = 0; i < filesToCache.Length; i++)
                    {
                        CustomSong song = filesToCache[i];
                        CurrentProcessingFile = song.SongTitle;
                        OnCacheProgress?.Invoke(FilesProcessed, TotalFilesToCache, CurrentProcessingFile);
                        
                        string cacheFileName = Path.GetFileNameWithoutExtension(song.riq) + ".audio";
                        string cachedPath = Path.Combine(_audioCacheDir, cacheFileName);
                        
                        if (File.Exists(cachedPath))
                        {
                            FilesProcessed++;
                            continue;
                        }
                        
                        using (FileStream fileStream = File.Open(song.riq, FileMode.Open))
                        {
                            using (ZipArchive zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                            {
                                ZipArchiveEntry entry = FindSongEntry(zipArchive) ?? zipArchive.GetEntry("song.bin");
                                
                                if (entry != null)
                                {
                                    using (Stream stream = entry.Open())
                                    {
                                        using (FileStream output = File.Create(cachedPath))
                                        {
                                            stream.CopyTo(output);
                                        }
                                    }
                                }
                            }
                        }
                        
                        FilesProcessed++;
                        OnCacheProgress?.Invoke(FilesProcessed, TotalFilesToCache, CurrentProcessingFile);
                    }
                }
                catch (Exception ex)
                {
                    extractionError = ex;
                }
                finally
                {
                    extractionComplete = true;
                }
            });
            
            while (!extractionComplete)
            {
                yield return new WaitForSeconds(0.1f);
            }
            
            if (extractionError != null)
            {
                Debug.LogError($"[CacheManager] Extraction error: {extractionError.Message}");
            }
        }

        private IEnumerator LoadFromCacheAsync(string cachedPath, CustomSong song, System.Action<bool> onComplete)
        {
            AudioType audioType = AudioType.UNKNOWN;
            byte[] headerBytes = new byte[12];
            
            try
            {
                using (FileStream fs = File.OpenRead(cachedPath))
                {
                    fs.Read(headerBytes, 0, 12);
                }
                audioType = DetectAudioType(headerBytes);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CacheManager] Failed to read cached audio: {ex.Message}");
                onComplete?.Invoke(false);
                yield break;
            }
            
            if (audioType == AudioType.UNKNOWN)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            
            yield return StartCoroutine(LoadAudioClipFromFile(cachedPath, audioType, (clip) => {
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
                            ZipArchiveEntry entry = FindSongEntry(zipArchive) ?? zipArchive.GetEntry("song.bin");
                            
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
                Debug.LogError($"[CacheManager] Failed to load from archive: {loadError.Message}");
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

        private IEnumerator LoadAudioClipFromFile(string filePath, AudioType audioType, System.Action<AudioClip> callback)
        {
            yield return StartCoroutine(LoadAudioClipNonBlocking(filePath, audioType, callback));
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
                    Debug.LogWarning($"[CacheManager] Failed to delete temp file: {ex.Message}");
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
                    Debug.LogError($"[CacheManager] Failed to load audio: {www.error}");
                    callback?.Invoke(null);
                }
            }
        }

        private ZipArchiveEntry FindSongEntry(ZipArchive archive)
        {
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

        private void CleanupOldCacheFiles()
        {
            if (!Directory.Exists(_audioCacheDir)) return;
            
            try
            {
                var files = Directory.GetFiles(_audioCacheDir);
                var cutoffDate = DateTime.Now.AddDays(-7);
                
                foreach (var file in files)
                {
                    if (File.GetCreationTime(file) < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CacheManager] Failed to clean cache: {ex.Message}");
            }
        }
    }
}