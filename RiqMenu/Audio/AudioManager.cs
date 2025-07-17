using System;
using System.Collections;
using UnityEngine;
using RiqMenu.Core;
using RiqMenu.Songs;

namespace RiqMenu.Audio
{
    /// <summary>
    /// Manages audio preview playback using async TempoSound to prevent main thread blocking
    /// </summary>
    public class AudioManager : MonoBehaviour, IRiqMenuSystem
    {
        public bool IsActive { get; private set; }
        
        private GameObject _previewSourceGO;
        private TempoSound _previewSource;
        private CustomSong _currentPreviewSong;
        
        public bool IsPreviewPlaying 
        { 
            get 
            {
                try
                {
                    return _previewSource != null && _previewSource.IsPlaying;
                }
                catch
                {
                    return false;
                }
            }
        }
        public CustomSong CurrentPreviewSong => _currentPreviewSong;
        
        public event System.Action<CustomSong> OnPreviewStarted;
        public event System.Action OnPreviewStopped;

        public void Initialize()
        {
            IsActive = true;
        }

        public void Cleanup()
        {
            StopPreview();
            if (_previewSourceGO != null)
            {
                Destroy(_previewSourceGO);
                _previewSourceGO = null;
            }
            IsActive = false;
        }

        public void Update()
        {
        }

        /// <summary>
        /// Play preview audio for a song using async TempoSound initialization
        /// </summary>
        public void PlayPreview(CustomSong song, float startTime = 0f)
        {
            if (song?.audioClip == null)
            {
                Debug.LogWarning($"[AudioManager] Cannot play preview - no audio clip for {song?.SongTitle}");
                return;
            }

            StopPreview();

            // Create preview source if needed
            if (_previewSourceGO == null)
            {
                _previewSourceGO = new GameObject("RiqMenu_PreviewSource");
                DontDestroyOnLoad(_previewSourceGO);
            }

            _previewSourceGO.SetActive(false);
            _previewSource = _previewSourceGO.AddComponent<TempoSound>();
            
            // Use asyncLoad=true to prevent main thread blocking on large audio files
            _previewSource.Init(song.audioClip, Bus.Music, 1f, false, true);
            _previewSourceGO.SetActive(true);

            // Default to middle of song if no start time specified
            if (startTime <= 0f)
            {
                startTime = song.audioClip.length / 2f;
            }

            _currentPreviewSong = song;
            StartCoroutine(PlayPreviewWhenLoaded(song, startTime));
        }

        /// <summary>
        /// Wait for TempoSound async loading to complete before playing
        /// </summary>
        private System.Collections.IEnumerator PlayPreviewWhenLoaded(CustomSong song, float startTime)
        {
            // Wait for TempoSound to finish async loading
            while (_previewSource != null && !_previewSource.IsLoaded)
            {
                yield return null;
            }
            
            // Check if preview was cancelled while loading
            if (_previewSource == null || _currentPreviewSong != song)
            {
                yield break;
            }
            
            _previewSource.PlayFrom(startTime);
            OnPreviewStarted?.Invoke(song);
        }

        /// <summary>
        /// Stop any currently playing preview
        /// </summary>
        public void StopPreview()
        {
            if (_previewSource != null)
            {
                try
                {
                    if (_previewSource.IsPlaying)
                    {
                        _previewSource.Stop();
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[AudioManager] Error stopping preview: {ex.Message}");
                }
                
                Destroy(_previewSource);
                _previewSource = null;
            }

            if (_previewSourceGO != null)
            {
                _previewSourceGO.SetActive(false);
            }

            var previousSong = _currentPreviewSong;
            _currentPreviewSong = null;
            
            if (previousSong != null)
            {
                OnPreviewStopped?.Invoke();
            }
        }

        /// <summary>
        /// Check if song audio is loaded (only allows RAM playback)
        /// </summary>
        public void LoadSongAudio(CustomSong song, System.Action<bool> onComplete = null)
        {
            if (song == null)
            {
                onComplete?.Invoke(false);
                return;
            }

            // Only succeed if already in RAM
            if (song.audioClip != null)
            {
                onComplete?.Invoke(true);
                return;
            }

            onComplete?.Invoke(false);
        }

        /// <summary>
        /// Get current playback position as normalized value (0-1)
        /// </summary>
        public float GetPlaybackProgress()
        {
            if (_previewSource == null || _currentPreviewSong?.audioClip == null)
            {
                return 0f;
            }

            return (float)_previewSource.Position / _currentPreviewSong.audioClip.length;
        }

        /// <summary>
        /// Seek to specific position in current preview
        /// </summary>
        public void SeekPreview(float normalizedPosition)
        {
            if (_previewSource == null || _currentPreviewSong?.audioClip == null)
            {
                return;
            }

            float targetTime = normalizedPosition * _currentPreviewSong.audioClip.length;
            _previewSource.Position = targetTime;
        }
    }
}