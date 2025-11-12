using System;
using UnityEngine;
using RiqMenu.Core;

namespace RiqMenu.Input
{
    /// <summary>
    /// Centralized input management for RiqMenu systems
    /// </summary>
    public class RiqInputManager : MonoBehaviour, IRiqMenuSystem {
        public bool IsActive { get; private set; }

        private bool _inputBlocked = false;

        public bool IsInputBlocked => _inputBlocked;

        public event System.Action OnOverlayToggleRequested;
        public event System.Action OnEscapePressed;
        public event System.Action<Vector2> OnMouseDrag;
        public event System.Action OnMouseUp;
        public event System.Action OnMouseDown;

        // Configurable hotkeys
        public KeyCode SongsOverlayKey { get; set; } = KeyCode.F1;
        public KeyCode AudioStopKey { get; set; } = KeyCode.F2;
        public KeyCode RefreshKey { get; set; } = KeyCode.F5;

        public void Initialize() {
            Debug.Log("[InputManager] Initializing");
            IsActive = true;
        }

        public void Cleanup() {
            IsActive = false;
        }

        public void Update() {
            if (!IsActive) return;

            HandleKeyboardInput();
            HandleMouseInput();
        }

        private void HandleKeyboardInput() {
            // Global hotkeys that work even when input is blocked
            if (UnityEngine.Input.GetKeyDown(SongsOverlayKey)) {
                OnOverlayToggleRequested?.Invoke();
            }

            if (UnityEngine.Input.GetKeyDown(AudioStopKey)) {
                var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
                audioManager?.StopPreview();
            }

            if (UnityEngine.Input.GetKeyDown(RefreshKey)) {
                var songManager = RiqMenuSystemManager.Instance?.SongManager;
                songManager?.Initialize();
            }

            // Regular input that can be blocked
            if (!_inputBlocked) {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) {
                    OnEscapePressed?.Invoke();
                }
            }
        }

        private void HandleMouseInput() {
            if (_inputBlocked) return;

            if (UnityEngine.Input.GetMouseButtonDown(0)) {
                OnMouseDown?.Invoke();
            }

            if (UnityEngine.Input.GetMouseButtonUp(0)) {
                OnMouseUp?.Invoke();
            }

            if (UnityEngine.Input.GetMouseButton(0)) {
                Vector2 mouseDelta = new Vector2(UnityEngine.Input.GetAxis("Mouse X"), UnityEngine.Input.GetAxis("Mouse Y"));
                if (mouseDelta.magnitude > 0.01f) {
                    OnMouseDrag?.Invoke(mouseDelta);
                }
            }
        }

        /// <summary>
        /// Block all input except global hotkeys
        /// </summary>
        public void BlockInput() {
            _inputBlocked = true;
        }

        /// <summary>
        /// Unblock input
        /// </summary>
        public void UnblockInput() {
            _inputBlocked = false;
        }

        /// <summary>
        /// Temporarily block input for a duration
        /// </summary>
        public void BlockInputTemporary(float duration) {
            BlockInput();
            Invoke(nameof(UnblockInput), duration);
        }

        /// <summary>
        /// Check if a specific key is pressed (respects input blocking)
        /// </summary>
        public bool GetKeyDown(KeyCode key, bool ignoreBlocking = false) {
            if (_inputBlocked && !ignoreBlocking) return false;
            return UnityEngine.Input.GetKeyDown(key);
        }

        /// <summary>
        /// Check if mouse button is pressed (respects input blocking)
        /// </summary>
        public bool GetMouseButtonDown(int button, bool ignoreBlocking = false) {
            if (_inputBlocked && !ignoreBlocking) return false;
            return UnityEngine.Input.GetMouseButtonDown(button);
        }

        /// <summary>
        /// Get mouse position in screen coordinates
        /// </summary>
        public Vector2 GetMousePosition() {
            Vector2 mousePos = UnityEngine.Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y; // Convert to GUI coordinates
            return mousePos;
        }

        /// <summary>
        /// Check if mouse is over a specific rect
        /// </summary>
        public bool IsMouseOverRect(Rect rect) {
            return rect.Contains(GetMousePosition());
        }
    }
}
