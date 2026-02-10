using System;
using UnityEngine;
using RiqMenu.Core;
using RiqMenu.Input;
using RiqMenu.UI.Toolkit;

namespace RiqMenu.UI
{
    /// <summary>
    /// Manages all UI components including the overlay
    /// </summary>
    public class UIManager : MonoBehaviour, IRiqMenuSystem {
        public bool IsActive { get; private set; }

        private ToolkitOverlay _overlay;

        public ToolkitOverlay Overlay => _overlay;

        public void Initialize() {
            Debug.Log("[UIManager] Initializing with UI Toolkit overlay");
            _overlay = gameObject.AddComponent<ToolkitOverlay>();
            _overlay.OnSongSelected += OnSongSelected;

            // Subscribe to input events
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            if (inputManager != null) {
                inputManager.OnOverlayToggleRequested += ToggleOverlay;
                inputManager.OnEscapePressed += HandleEscapePressed;
            }

            IsActive = true;
        }

        public void Cleanup() {
            // Unsubscribe from events
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            if (inputManager != null) {
                inputManager.OnOverlayToggleRequested -= ToggleOverlay;
                inputManager.OnEscapePressed -= HandleEscapePressed;
            }

            if (_overlay != null) {
                _overlay.OnSongSelected -= OnSongSelected;
                Destroy(_overlay);
                _overlay = null;
            }

            IsActive = false;
        }

        public void Update() {
            // UI Manager doesn't need constant updates beyond its components
        }

        private void ToggleOverlay() {
            _overlay?.Toggle();
        }

        private void HandleEscapePressed() {
            if (_overlay != null && _overlay.IsVisible) {
                _overlay.Hide();
            }
        }

        private void OnSongSelected(int songIndex, OverlayTab sourceTab) {
            // Final safeguard: only play if event came from Local tab
            if (sourceTab != OverlayTab.Local) {
                Debug.LogWarning($"[UIManager] Blocked song play - event from {sourceTab} tab, not Local");
                return;
            }

            Debug.Log($"[UIManager] Song selected: {songIndex} from {sourceTab} tab");

            // Unblock input before changing scenes
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.UnblockInput();

            // Start playing the selected song using the same method as original RiqMenu
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            var song = songManager?.GetSong(songIndex);

            if (song != null) {
                RiqLoader.path = song.riq;
                RiqMenuState.LaunchedFromRiqMenu = true;

                Debug.Log($"[UIManager] Loading song: {song.SongTitle} from path: {song.riq}");
                UnityEngine.SceneManagement.SceneManager.LoadScene(SceneKey.RiqLoader.ToString());
            }
        }
    }
}
