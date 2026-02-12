using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RiqMenu.Core;

namespace RiqMenu.Input;

/// <summary>
/// High-level input method currently used by the player.
/// </summary>
public enum RiqInputMethod {
    KeyboardMouse,
    Gamepad,
    Touch
}

/// <summary>
/// Logical actions used by menu and global input handling.
/// </summary>
public enum RiqInputAction {
    OverlayToggle,
    AudioStop,
    RefreshSongs,
    Submit,
    Cancel,
    Search,
    Edit,
    PreviousTab,
    NextTab,
    ToggleAutoplay,
    ToggleMute,
    PageUp,
    PageDown,
    NavigateUp,
    NavigateDown,
    NavigateLeft,
    NavigateRight
}

/// <summary>
/// Discrete direction for menu navigation.
/// </summary>
public enum NavigationDirection {
    None,
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Display label and physical keys for a single action binding.
/// </summary>
public sealed class InputBinding(string displayLabel, params KeyCode[] keys) {
    public string DisplayLabel { get; set; } = displayLabel;
    public KeyCode[] Keys { get; set; } = keys ?? Array.Empty<KeyCode>();
}

/// <summary>
/// Centralized input management for RiqMenu systems
/// </summary>
public class RiqInputManager : MonoBehaviour, IRiqMenuSystem {
    private const float GAMEPAD_AXIS_DEADZONE = 0.5f;
    private const float MENU_NAV_INITIAL_REPEAT_DELAY = 0.28f;
    private const float MENU_NAV_REPEAT_INTERVAL = 0.12f;

    private bool _inputBlocked;
    private NavigationDirection _heldMenuDirection = NavigationDirection.None;
    private float _nextMenuNavRepeatTime;

    private readonly Dictionary<RiqInputMethod, Dictionary<RiqInputAction, InputBinding>> _bindings = new();
    private readonly HashSet<KeyCode> _gamepadBoundKeys = [];

    public bool IsActive { get; private set; }
    public bool IsInputBlocked => _inputBlocked;
    public RiqInputMethod CurrentInputMethod { get; private set; } = RiqInputMethod.KeyboardMouse;

    public event System.Action OnOverlayToggleRequested;
    public event System.Action OnMenuCancelPressed;
    public event System.Action<Vector2> OnMouseDrag;
    public event System.Action OnMouseUp;
    public event System.Action OnMouseDown;
    public event System.Action<RiqInputMethod> OnInputMethodChanged;

    /// <summary>
    /// Keyboard binding for opening the overlay.
    /// </summary>
    public KeyCode SongsOverlayKey {
        get => GetPrimaryKey(RiqInputMethod.KeyboardMouse, RiqInputAction.OverlayToggle, KeyCode.F1);
        set => SetBinding(RiqInputMethod.KeyboardMouse, RiqInputAction.OverlayToggle, KeyLabel(value), value);
    }

    /// <summary>
    /// Keyboard binding for stopping audio preview.
    /// </summary>
    public KeyCode AudioStopKey {
        get => GetPrimaryKey(RiqInputMethod.KeyboardMouse, RiqInputAction.AudioStop, KeyCode.F2);
        set => SetBinding(RiqInputMethod.KeyboardMouse, RiqInputAction.AudioStop, KeyLabel(value), value);
    }

    /// <summary>
    /// Keyboard binding for refreshing song data.
    /// </summary>
    public KeyCode RefreshKey {
        get => GetPrimaryKey(RiqInputMethod.KeyboardMouse, RiqInputAction.RefreshSongs, KeyCode.F5);
        set => SetBinding(RiqInputMethod.KeyboardMouse, RiqInputAction.RefreshSongs, KeyLabel(value), value);
    }

    /// <summary>
    /// Initializes default bindings and enables input processing.
    /// </summary>
    public void Initialize() {
        InitializeDefaultBindings();
        Debug.Log("[InputManager] Initializing");
        IsActive = true;
    }

    /// <summary>
    /// Disables input processing.
    /// </summary>
    public void Cleanup() {
        IsActive = false;
    }

    /// <summary>
    /// Processes input each frame while active.
    /// </summary>
    public void Update() {
        if (!IsActive) return;

        DetectActiveInputMethod();
        HandleGlobalInput();
        HandleMouseInput();
    }

    /// <summary>
    /// Updates a binding for an action/method pair.
    /// </summary>
    public void SetBinding(RiqInputMethod method, RiqInputAction action, string displayLabel,
        params KeyCode[] keys) {
        if (!_bindings.TryGetValue(method, out var methodBindings)) {
            methodBindings = new Dictionary<RiqInputAction, InputBinding>();
            _bindings[method] = methodBindings;
        }

        methodBindings[action] = new InputBinding(displayLabel, keys);
        RefreshGamepadBoundKeys();
    }

    /// <summary>
    /// Returns the display label for an action in the provided input method.
    /// </summary>
    public string GetBindingLabel(RiqInputAction action, RiqInputMethod? methodOverride = null) {
        var method = methodOverride ?? CurrentInputMethod;
        return TryGetBinding(method, action, out var binding) ? binding.DisplayLabel : string.Empty;
    }

    /// <summary>
    /// Check if a specific key is pressed (respects input blocking).
    /// </summary>
    public bool GetKeyDown(KeyCode key, bool ignoreBlocking = false) {
        if (_inputBlocked && !ignoreBlocking) return false;
        return UnityEngine.Input.GetKeyDown(key);
    }

    /// <summary>
    /// Check if mouse button is pressed (respects input blocking).
    /// </summary>
    public bool GetMouseButtonDown(int button, bool ignoreBlocking = false) {
        if (_inputBlocked && !ignoreBlocking) return false;
        return UnityEngine.Input.GetMouseButtonDown(button);
    }

    /// <summary>
    /// Returns true when submit is pressed.
    /// </summary>
    public bool GetMenuSubmitDown(bool ignoreBlocking = false, bool allowSpace = true) {
        if (!IsActionDown(RiqInputAction.Submit, ignoreBlocking)) return false;
        return allowSpace || !UnityEngine.Input.GetKeyDown(KeyCode.Space);
    }

    /// <summary>
    /// Returns a repeated navigation direction for menu movement.
    /// </summary>
    public NavigationDirection ConsumeMenuNavigationDirection(bool ignoreBlocking = false) {
        if (_inputBlocked && !ignoreBlocking) return NavigationDirection.None;

        var direction = GetCurrentMenuDirection();
        if (direction == NavigationDirection.None) {
            _heldMenuDirection = NavigationDirection.None;
            _nextMenuNavRepeatTime = 0f;
            return NavigationDirection.None;
        }

        if (direction != _heldMenuDirection) {
            _heldMenuDirection = direction;
            _nextMenuNavRepeatTime = Time.unscaledTime + MENU_NAV_INITIAL_REPEAT_DELAY;
            return direction;
        }

        if (Time.unscaledTime >= _nextMenuNavRepeatTime) {
            _nextMenuNavRepeatTime = Time.unscaledTime + MENU_NAV_REPEAT_INTERVAL;
            return direction;
        }

        return NavigationDirection.None;
    }

    /// <summary>
    /// Returns true when the requested action was pressed this frame.
    /// </summary>
    public bool IsActionDown(RiqInputAction action, bool ignoreBlocking = false) {
        if (_inputBlocked && !ignoreBlocking) return false;

        foreach (var methodBindings in _bindings.Values) {
            if (!methodBindings.TryGetValue(action, out var binding)) continue;

            if (binding.Keys.Any(UnityEngine.Input.GetKeyDown)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Blocks all input except global hotkeys.
    /// </summary>
    public void BlockInput() {
        _inputBlocked = true;
    }

    /// <summary>
    /// Unblocks input.
    /// </summary>
    public void UnblockInput() {
        _inputBlocked = false;
    }

    /// <summary>
    /// Temporarily blocks input for a duration.
    /// </summary>
    public void BlockInputTemporary(float duration) {
        BlockInput();
        Invoke(nameof(UnblockInput), duration);
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

    private void InitializeDefaultBindings() {
        _bindings.Clear();

        _bindings[RiqInputMethod.KeyboardMouse] = new Dictionary<RiqInputAction, InputBinding> {
            { RiqInputAction.OverlayToggle, new InputBinding("F1", KeyCode.F1) },
            { RiqInputAction.AudioStop, new InputBinding("F2", KeyCode.F2) },
            { RiqInputAction.RefreshSongs, new InputBinding("F5", KeyCode.F5) }, 
            {
                RiqInputAction.Submit, new InputBinding("Enter", KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space)
            },
            { RiqInputAction.Cancel, new InputBinding("Esc", KeyCode.Escape) },
            { RiqInputAction.Search, new InputBinding("Tab", KeyCode.Tab) },
            { RiqInputAction.Edit, new InputBinding("R", KeyCode.R) },
            { RiqInputAction.PreviousTab, new InputBinding("Q", KeyCode.Q) },
            { RiqInputAction.NextTab, new InputBinding("E", KeyCode.E) },
            { RiqInputAction.ToggleAutoplay, new InputBinding("P", KeyCode.P) },
            { RiqInputAction.ToggleMute, new InputBinding("M", KeyCode.M) },
            { RiqInputAction.PageUp, new InputBinding("PgUp", KeyCode.PageUp) },
            { RiqInputAction.PageDown, new InputBinding("PgDn", KeyCode.PageDown) },
            { RiqInputAction.NavigateUp, new InputBinding("W / Up", KeyCode.W, KeyCode.UpArrow) },
            { RiqInputAction.NavigateDown, new InputBinding("S / Down", KeyCode.S, KeyCode.DownArrow) },
            { RiqInputAction.NavigateLeft, new InputBinding("A / Left", KeyCode.A, KeyCode.LeftArrow) },
            { RiqInputAction.NavigateRight, new InputBinding("D / Right", KeyCode.D, KeyCode.RightArrow) }
        };

        _bindings[RiqInputMethod.Gamepad] = new Dictionary<RiqInputAction, InputBinding> {
            { RiqInputAction.OverlayToggle, new InputBinding("Start", KeyCode.JoystickButton7) },
            { RiqInputAction.AudioStop, new InputBinding("Back", KeyCode.JoystickButton6) },
            { RiqInputAction.RefreshSongs, new InputBinding("Refresh", KeyCode.JoystickButton11) },
            { RiqInputAction.Submit, new InputBinding("A", KeyCode.JoystickButton0) },
            { RiqInputAction.Cancel, new InputBinding("B", KeyCode.JoystickButton1) },
            { RiqInputAction.Search, new InputBinding("Y", KeyCode.JoystickButton3) },
            { RiqInputAction.Edit, new InputBinding("X", KeyCode.JoystickButton2) },
            { RiqInputAction.PreviousTab, new InputBinding("LB", KeyCode.JoystickButton4) },
            { RiqInputAction.NextTab, new InputBinding("RB", KeyCode.JoystickButton5) },
            { RiqInputAction.ToggleAutoplay, new InputBinding("R-Stick", KeyCode.JoystickButton10) },
            { RiqInputAction.ToggleMute, new InputBinding("L-Stick", KeyCode.JoystickButton9) },
            { RiqInputAction.PageUp, new InputBinding("Unbound") },
            { RiqInputAction.PageDown, new InputBinding("Unbound") },
            { RiqInputAction.NavigateUp, new InputBinding("Stick Up", KeyCode.JoystickButton13) },
            { RiqInputAction.NavigateDown, new InputBinding("Stick Down", KeyCode.JoystickButton14) },
            { RiqInputAction.NavigateLeft, new InputBinding("Stick Left", KeyCode.JoystickButton15) },
            { RiqInputAction.NavigateRight, new InputBinding("Stick Right", KeyCode.JoystickButton16) }
        };

        _bindings[RiqInputMethod.Touch] = new Dictionary<RiqInputAction, InputBinding> {
            { RiqInputAction.OverlayToggle, new InputBinding("Overlay") },
            { RiqInputAction.AudioStop, new InputBinding("Audio Stop") },
            { RiqInputAction.RefreshSongs, new InputBinding("Refresh") },
            { RiqInputAction.Submit, new InputBinding("Tap") },
            { RiqInputAction.Cancel, new InputBinding("Back") },
            { RiqInputAction.Search, new InputBinding("Search Box") },
            { RiqInputAction.Edit, new InputBinding("Hold") },
            { RiqInputAction.PreviousTab, new InputBinding("Tab Buttons") },
            { RiqInputAction.NextTab, new InputBinding("Tab Buttons") },
            { RiqInputAction.ToggleAutoplay, new InputBinding("Autoplay") },
            { RiqInputAction.ToggleMute, new InputBinding("Mute") },
            { RiqInputAction.PageUp, new InputBinding("Swipe Up") },
            { RiqInputAction.PageDown, new InputBinding("Swipe Down") },
            { RiqInputAction.NavigateUp, new InputBinding("Swipe") },
            { RiqInputAction.NavigateDown, new InputBinding("Swipe") },
            { RiqInputAction.NavigateLeft, new InputBinding("Swipe") },
            { RiqInputAction.NavigateRight, new InputBinding("Swipe") }
        };

        RefreshGamepadBoundKeys();
    }

    private void RefreshGamepadBoundKeys() {
        _gamepadBoundKeys.Clear();
        if (!_bindings.TryGetValue(RiqInputMethod.Gamepad, out var methodBindings)) return;

        foreach (var key in methodBindings.Values.SelectMany(binding => binding.Keys))
        {
            _gamepadBoundKeys.Add(key);
        }
    }

    private void HandleGlobalInput() {
        if (IsActionDown(RiqInputAction.OverlayToggle, ignoreBlocking: true)) {
            OnOverlayToggleRequested?.Invoke();
        }

        if (IsActionDown(RiqInputAction.AudioStop, ignoreBlocking: true)) {
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            audioManager?.StopPreview();
        }

        if (IsActionDown(RiqInputAction.RefreshSongs, ignoreBlocking: true)) {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            songManager?.Initialize();
        }

        if (!_inputBlocked && IsActionDown(RiqInputAction.Cancel, ignoreBlocking: true)) {
            OnMenuCancelPressed?.Invoke();
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
            Vector2 mouseDelta = new Vector2(UnityEngine.Input.GetAxis("Mouse X"),
                UnityEngine.Input.GetAxis("Mouse Y"));
            if (mouseDelta.magnitude > 0.01f) {
                OnMouseDrag?.Invoke(mouseDelta);
            }
        }
    }

    private bool IsActionHeld(RiqInputAction action) {
        foreach (var methodBindings in _bindings.Values) {
            if (!methodBindings.TryGetValue(action, out var binding)) continue;

            if (binding.Keys.Any(UnityEngine.Input.GetKey))
            {
                return true;
            }
        }

        return false;
    }

    private void DetectActiveInputMethod() {
        if (HasTouchInputThisFrame()) {
            SetInputMethod(RiqInputMethod.Touch);
            return;
        }

        if (HasGamepadInputThisFrame()) {
            SetInputMethod(RiqInputMethod.Gamepad);
            return;
        }

        if (HasKeyboardMouseInputThisFrame()) {
            SetInputMethod(RiqInputMethod.KeyboardMouse);
        }
    }

    private void SetInputMethod(RiqInputMethod inputMethod) {
        if (CurrentInputMethod == inputMethod) return;

        CurrentInputMethod = inputMethod;
        OnInputMethodChanged?.Invoke(CurrentInputMethod);
    }

    private bool HasTouchInputThisFrame() {
        return UnityEngine.Input.touchCount > 0;
    }

    private bool HasGamepadInputThisFrame() {
        if (!HasConnectedGamepad()) return false;

        if (_gamepadBoundKeys.Any(UnityEngine.Input.GetKeyDown))
        {
            return true;
        }

        float horizontal = UnityEngine.Input.GetAxisRaw("Horizontal");
        float vertical = UnityEngine.Input.GetAxisRaw("Vertical");
        return Mathf.Abs(horizontal) >= GAMEPAD_AXIS_DEADZONE || Mathf.Abs(vertical) >= GAMEPAD_AXIS_DEADZONE;
    }

    private bool HasKeyboardMouseInputThisFrame() {
        if (UnityEngine.Input.GetMouseButtonDown(0) ||
            UnityEngine.Input.GetMouseButtonDown(1) ||
            UnityEngine.Input.GetMouseButtonDown(2) ||
            Mathf.Abs(UnityEngine.Input.GetAxis("Mouse X")) > 0.01f ||
            Mathf.Abs(UnityEngine.Input.GetAxis("Mouse Y")) > 0.01f ||
            Mathf.Abs(UnityEngine.Input.mouseScrollDelta.y) > 0.01f) {
            return true;
        }

        if (!string.IsNullOrEmpty(UnityEngine.Input.inputString)) {
            return true;
        }

        return _bindings.Where(methodBinding => methodBinding.Key == RiqInputMethod.KeyboardMouse)
            .Any(methodBinding =>
                methodBinding.Value.Values.Any(binding => binding.Keys.Any(UnityEngine.Input.GetKeyDown)));
    }

    private static bool HasConnectedGamepad() {
        var joystickNames = UnityEngine.Input.GetJoystickNames();
        return joystickNames.Any(joystickName => !string.IsNullOrEmpty(joystickName));
    }

    private NavigationDirection GetCurrentMenuDirection() {
        var horizontal = 0;
        var vertical = 0;

        if (IsActionHeld(RiqInputAction.NavigateLeft)) horizontal--;
        if (IsActionHeld(RiqInputAction.NavigateRight)) horizontal++;
        if (IsActionHeld(RiqInputAction.NavigateUp)) vertical++;
        if (IsActionHeld(RiqInputAction.NavigateDown)) vertical--;

        var axisHorizontal = UnityEngine.Input.GetAxisRaw("Horizontal");
        var axisVertical = UnityEngine.Input.GetAxisRaw("Vertical");

        if (horizontal == 0) {
            horizontal = axisHorizontal switch {
                <= -GAMEPAD_AXIS_DEADZONE => -1,
                >= GAMEPAD_AXIS_DEADZONE => 1,
                _ => horizontal
            };
        }

        if (vertical == 0) {
            vertical = axisVertical switch {
                <= -GAMEPAD_AXIS_DEADZONE => -1,
                >= GAMEPAD_AXIS_DEADZONE => 1,
                _ => vertical
            };
        }

        if (Mathf.Abs(vertical) >= Mathf.Abs(horizontal) && vertical != 0) {
            return vertical > 0 ? NavigationDirection.Up : NavigationDirection.Down;
        }

        if (horizontal != 0) {
            return horizontal > 0 ? NavigationDirection.Right : NavigationDirection.Left;
        }

        return NavigationDirection.None;
    }

    private bool TryGetBinding(RiqInputMethod method, RiqInputAction action, out InputBinding binding) {
        binding = null;
        return _bindings.TryGetValue(method, out var methodBindings) &&
               methodBindings.TryGetValue(action, out binding);
    }

    private KeyCode GetPrimaryKey(RiqInputMethod method, RiqInputAction action, KeyCode fallback) {
        if (!TryGetBinding(method, action, out var binding) || binding.Keys.Length == 0) {
            return fallback;
        }

        return binding.Keys[0];
    }

    private static string KeyLabel(KeyCode key) {
        return key.ToString();
    }

}
