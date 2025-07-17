using System;
using UnityEngine;
using RiqMenu.Core;
using RiqMenu.Input;

namespace RiqMenu.UI
{
    /// <summary>
    /// Manages all UI components including the draggable overlay
    /// </summary>
    public class UIManager : MonoBehaviour, IRiqMenuSystem
    {
        public bool IsActive { get; private set; }
        
        private SongsOverlay _songsOverlay;
        private bool _showLoadingProgress = false;
        
        public SongsOverlay SongsOverlay => _songsOverlay;
        public bool IsShowingLoadingProgress 
        { 
            get => _showLoadingProgress;
            set => _showLoadingProgress = value;
        }

        public void Initialize()
        {
            Debug.Log("[UIManager] Initializing");
            
            // Create songs overlay
            _songsOverlay = gameObject.AddComponent<SongsOverlay>();
            
            // Subscribe to input events
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            if (inputManager != null)
            {
                inputManager.OnOverlayToggleRequested += ToggleSongsOverlay;
                inputManager.OnEscapePressed += HandleEscapePressed;
            }
            
            // Subscribe to overlay events
            if (_songsOverlay != null)
            {
                _songsOverlay.OnSongSelected += OnSongSelected;
            }
            
            // Subscribe to cache events for loading screen
            var cacheManager = RiqMenuSystemManager.Instance?.CacheManager;
            if (cacheManager != null)
            {
                cacheManager.OnCacheProgress += OnCacheProgress;
                cacheManager.OnCacheComplete += OnCacheComplete;
            }
            
            IsActive = true;
        }

        public void Cleanup()
        {
            // Unsubscribe from events
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            if (inputManager != null)
            {
                inputManager.OnOverlayToggleRequested -= ToggleSongsOverlay;
                inputManager.OnEscapePressed -= HandleEscapePressed;
            }
            
            var cacheManager = RiqMenuSystemManager.Instance?.CacheManager;
            if (cacheManager != null)
            {
                cacheManager.OnCacheProgress -= OnCacheProgress;
                cacheManager.OnCacheComplete -= OnCacheComplete;
            }
            
            if (_songsOverlay != null)
            {
                _songsOverlay.OnSongSelected -= OnSongSelected;
                Destroy(_songsOverlay);
                _songsOverlay = null;
            }
            
            IsActive = false;
        }

        public void Update()
        {
            // UI Manager doesn't need constant updates beyond its components
        }

        private void OnGUI()
        {
            if (_showLoadingProgress)
            {
                DrawLoadingProgress();
            }
            
            // Also handle songs overlay GUI here
            if (_songsOverlay != null && _songsOverlay.IsVisible)
            {
                _songsOverlay.DrawOverlayGUI();
            }
        }

        private void DrawLoadingProgress()
        {
            var cacheManager = RiqMenuSystemManager.Instance?.CacheManager;
            if (cacheManager == null) return;
            
            // Full screen black overlay (matching original loading screen)
            GUI.color = Color.black;
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.color = Color.white;
            
            // Calculate center position
            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;
            
            // Main container (matching original dimensions)
            float boxWidth = 400f;
            float boxHeight = 150f;
            Rect boxRect = new Rect(centerX - boxWidth/2, centerY - boxHeight/2, boxWidth, boxHeight);
            
            GUI.color = Color.gray;
            GUI.Box(boxRect, "");
            GUI.color = Color.white;
            
            // Title - show current phase based on what's happening
            string title = "RiqMenu - Loading Audio";
            if (cacheManager.CurrentProcessingFile.Contains("Extracting") || cacheManager.CurrentProcessingFile.Contains("cache"))
            {
                title = "RiqMenu - Building Audio Cache";
            }
            else if (cacheManager.CurrentProcessingFile.Contains("Loading") || cacheManager.CurrentProcessingFile.Contains("memory"))
            {
                title = "RiqMenu - Loading Into Memory";
            }
            
            GUI.Label(new Rect(boxRect.x + 10, boxRect.y + 10, boxRect.width - 20, 30), 
                     $"<size=16><color=white><b>{title}</b></color></size>");
            
            // Progress bar background
            float progressBarY = boxRect.y + 50;
            float progressBarWidth = boxRect.width - 20;
            float progressBarHeight = 20;
            Rect progressBgRect = new Rect(boxRect.x + 10, progressBarY, progressBarWidth, progressBarHeight);
            GUI.color = Color.black;
            GUI.Box(progressBgRect, "");
            
            // Progress bar fill
            if (cacheManager.TotalFilesToCache > 0)
            {
                float progress = (float)cacheManager.FilesProcessed / cacheManager.TotalFilesToCache;
                float fillWidth = progressBarWidth * progress;
                Rect progressFillRect = new Rect(boxRect.x + 10, progressBarY, fillWidth, progressBarHeight);
                GUI.color = Color.green;
                GUI.Box(progressFillRect, "");
            }
            
            GUI.color = Color.white;
            
            // Progress text
            string progressText = cacheManager.TotalFilesToCache > 0 ? 
                $"Processing: {cacheManager.FilesProcessed}/{cacheManager.TotalFilesToCache}" : 
                "Initializing...";
            GUI.Label(new Rect(boxRect.x + 10, progressBarY + 25, boxRect.width - 20, 20), 
                $"<color=white>{progressText}</color>");
            
            // Current file
            if (!string.IsNullOrEmpty(cacheManager.CurrentProcessingFile))
            {
                string displayName = cacheManager.CurrentProcessingFile.Length > 40 ? 
                    cacheManager.CurrentProcessingFile.Substring(0, 37) + "..." : 
                    cacheManager.CurrentProcessingFile;
                GUI.Label(new Rect(boxRect.x + 10, progressBarY + 50, boxRect.width - 20, 20), 
                    $"<color=yellow>{displayName}</color>");
            }
            
        }

        private void ToggleSongsOverlay()
        {
            if (_songsOverlay != null)
            {
                _songsOverlay.Toggle();
            }
        }

        private void HandleEscapePressed()
        {
            if (_songsOverlay != null && _songsOverlay.IsVisible)
            {
                _songsOverlay.Hide();
            }
        }

        private void OnSongSelected(int songIndex)
        {
            Debug.Log($"[UIManager] Song selected: {songIndex}");
            
            // Unblock input before changing scenes
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.UnblockInput();
            
            // Start playing the selected song using the same method as original RiqMenu
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            var song = songManager?.GetSong(songIndex);
            
            if (song != null)
            {
                var riqMenu = FindObjectOfType<RiqMenuMain>();
                if (riqMenu != null)
                {
                    // Set up the song path for RiqLoader (same as original implementation)
                    riqMenu.riqPath = song.riq;
                    
                    // CRITICAL: Also set RiqLoader.path (this is what the original patch does)
                    RiqLoader.path = song.riq;
                    
                    Debug.Log($"[UIManager] Loading song: {song.SongTitle} from path: {song.riq}");
                    
                    // Use the same scene loading approach as the original patch
                    UnityEngine.SceneManagement.SceneManager.LoadScene(SceneKey.RiqLoader.ToString());
                }
                else
                {
                    Debug.LogError("[UIManager] Could not find RiqMenuMain instance");
                }
            }
        }

        private void OnCacheProgress(int processed, int total, string currentFile)
        {
            // Loading progress is handled by OnGUI, this is just for potential future use
        }

        private void OnCacheComplete()
        {
            _showLoadingProgress = false;
            
            // Block input during cache operation
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.UnblockInput();
        }

        /// <summary>
        /// Show the loading progress screen
        /// </summary>
        public void ShowLoadingProgress()
        {
            _showLoadingProgress = true;
            
            // Set global cache progress flag to block all input
            var riqMenu = FindObjectOfType<RiqMenuMain>();
            if (riqMenu != null)
            {
                riqMenu.SetCacheProgressState(true);
            }
            
            // Block input during cache operation
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.BlockInput();
        }

        /// <summary>
        /// Hide the loading progress screen
        /// </summary>
        public void HideLoadingProgress()
        {
            _showLoadingProgress = false;
            
            // Clear global cache progress flag to allow input
            var riqMenu = FindObjectOfType<RiqMenuMain>();
            if (riqMenu != null)
            {
                riqMenu.SetCacheProgressState(false);
            }
            
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.UnblockInput();
        }
    }
}