using System;
using UnityEngine;
using RiqMenu.Core;
using RiqMenu.Input;

namespace RiqMenu.UI
{
    /// <summary>
    /// Manages all UI components including the draggable overlay
    /// </summary>
    public class UIManager : MonoBehaviour, IRiqMenuSystem {
        public bool IsActive { get; private set; }

        private SongsOverlay _songsOverlay;

        public SongsOverlay SongsOverlay => _songsOverlay;

        public void Initialize() {
            Debug.Log("[UIManager] Initializing");

            // Create songs overlay
            _songsOverlay = gameObject.AddComponent<SongsOverlay>();

            // Subscribe to input events
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            if (inputManager != null) {
                inputManager.OnOverlayToggleRequested += ToggleSongsOverlay;
                inputManager.OnEscapePressed += HandleEscapePressed;
            }

            // Subscribe to overlay events
            if (_songsOverlay != null) {
                _songsOverlay.OnSongSelected += OnSongSelected;
            }

            IsActive = true;
        }

        public void Cleanup() {
            // Unsubscribe from events
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            if (inputManager != null) {
                inputManager.OnOverlayToggleRequested -= ToggleSongsOverlay;
                inputManager.OnEscapePressed -= HandleEscapePressed;
            }

            if (_songsOverlay != null) {
                _songsOverlay.OnSongSelected -= OnSongSelected;
                Destroy(_songsOverlay);
                _songsOverlay = null;
            }

            IsActive = false;
        }

        public void Update() {
            // UI Manager doesn't need constant updates beyond its components
        }

        private void OnGUI() {
            // Also handle songs overlay GUI here
            if (_songsOverlay != null && _songsOverlay.IsVisible) {
                _songsOverlay.DrawOverlayGUI();
            }
        }

        private void ToggleSongsOverlay() {
            if (_songsOverlay != null) {
                _songsOverlay.Toggle();
            }
        }

        private void HandleEscapePressed() {
            if (_songsOverlay != null && _songsOverlay.IsVisible) {
                _songsOverlay.Hide();
            }
        }

        private void OnSongSelected(int songIndex) {
            Debug.Log($"[UIManager] Song selected: {songIndex}");

            // Unblock input before changing scenes
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.UnblockInput();

            // Start playing the selected song using the same method as original RiqMenu
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            var song = songManager?.GetSong(songIndex);

            if (song != null) {
                var riqMenu = FindObjectOfType<RiqMenuMain>();
                if (riqMenu != null) {
                    // Set up the song path for RiqLoader
                    riqMenu.riqPath = song.riq;
                    RiqLoader.path = song.riq;

                    // Mark that this song was launched from RiqMenu for proper exit handling
                    RiqMenuMain.LaunchedFromRiqMenu = true;

                    Debug.Log($"[UIManager] Loading song: {song.SongTitle} from path: {song.riq}");

                    UnityEngine.SceneManagement.SceneManager.LoadScene(SceneKey.RiqLoader.ToString());
                }
                else {
                    Debug.LogError("[UIManager] Could not find RiqMenuMain instance");
                }
            }
        }
    }
}
