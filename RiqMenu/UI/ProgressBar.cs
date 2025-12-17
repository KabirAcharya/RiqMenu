using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace RiqMenu.UI {
    /// <summary>
    /// Displays a song progress bar at the top of the screen during gameplay.
    /// </summary>
    public class ProgressBar : MonoBehaviour {
        private static ProgressBar _instance;
        public static ProgressBar Instance => _instance;

        // UI Elements
        private GameObject _barContainer;
        private RectTransform _barRect;
        private Image _backgroundImage;
        private Image _fillImage;
        private RectTransform _fillRect;

        // Settings
        private const float BAR_HEIGHT = 4f;
        private const float BAR_WIDTH_PERCENT = 1.0f; // Full screen width
        private const float BAR_Y_OFFSET = 0f; // At very top

        // Colors
        private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color FillColor = new Color(0.3f, 0.85f, 1f, 0.8f); // Cyan

        private bool _isVisible = false;
        private Canvas _canvas;

        // Progress tracking
        private float _songLength = 0f;
        private float _currentTime = 0f;

        // Reflection cache for JukeboxScript
        private static Type _jukeboxType;
        private static FieldInfo _musicField;
        private static PropertyInfo _positionProp;
        private static PropertyInfo _lengthProp;
        private object _jukeboxInstance;
        private object _musicInstance;
        private static bool _reflectionInitialized = false;

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            CreateUI();
            Hide();
        }

        private void CreateUI() {
            // Create canvas
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 999; // Just below AccuracyBar

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            gameObject.AddComponent<GraphicRaycaster>();

            // Create bar container
            _barContainer = new GameObject("ProgressBarContainer");
            _barContainer.transform.SetParent(transform, false);
            _barRect = _barContainer.AddComponent<RectTransform>();

            // Position at top, full width
            _barRect.anchorMin = new Vector2(0f, 1f);
            _barRect.anchorMax = new Vector2(1f, 1f);
            _barRect.pivot = new Vector2(0.5f, 1f);
            _barRect.anchoredPosition = new Vector2(0f, -BAR_Y_OFFSET);
            _barRect.sizeDelta = new Vector2(0f, BAR_HEIGHT); // Width is stretched via anchors

            // Background
            _backgroundImage = _barContainer.AddComponent<Image>();
            _backgroundImage.color = BackgroundColor;

            // Fill bar (child of container)
            var fillObj = new GameObject("ProgressFill");
            fillObj.transform.SetParent(_barContainer.transform, false);
            _fillRect = fillObj.AddComponent<RectTransform>();

            // Fill anchored to left, stretches based on progress
            _fillRect.anchorMin = new Vector2(0f, 0f);
            _fillRect.anchorMax = new Vector2(0f, 1f); // Will update anchorMax.x for progress
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.anchoredPosition = Vector2.zero;
            _fillRect.sizeDelta = Vector2.zero;

            _fillImage = fillObj.AddComponent<Image>();
            _fillImage.color = FillColor;
        }

        private void Update() {
            if (!_isVisible) return;

            try {
                UpdateProgress();
            } catch {
                // Ignore errors during transitions
            }
        }

        private void UpdateProgress() {
            // Initialize reflection cache if needed
            if (!_reflectionInitialized) {
                _jukeboxType = Type.GetType("JukeboxScript, Assembly-CSharp");
                if (_jukeboxType == null) return;

                // CurrentSecond is a double property
                _positionProp = _jukeboxType.GetProperty("CurrentSecond", BindingFlags.Public | BindingFlags.Instance);
                // music is a public TempoSound field
                _musicField = _jukeboxType.GetField("music", BindingFlags.Public | BindingFlags.Instance);

                _reflectionInitialized = true;
            }

            // Find jukebox instance if we don't have one
            if (_jukeboxInstance == null) {
                _jukeboxInstance = FindObjectOfType(_jukeboxType);
                if (_jukeboxInstance == null) return;
            }

            // Get music instance and its Length property
            if (_songLength <= 0f && _musicField != null) {
                try {
                    _musicInstance = _musicField.GetValue(_jukeboxInstance);
                    if (_musicInstance != null) {
                        // Get Length property from TempoSound
                        if (_lengthProp == null) {
                            _lengthProp = _musicInstance.GetType().GetProperty("Length", BindingFlags.Public | BindingFlags.Instance);
                        }
                        if (_lengthProp != null) {
                            var lengthObj = _lengthProp.GetValue(_musicInstance);
                            if (lengthObj != null) {
                                _songLength = (float)Convert.ToDouble(lengthObj);
                            }
                        }
                    }
                } catch {
                    return;
                }
            }

            if (_songLength <= 0f) return;

            // Get current time from CurrentSecond property
            if (_positionProp != null) {
                try {
                    var posObj = _positionProp.GetValue(_jukeboxInstance);
                    if (posObj != null) {
                        _currentTime = (float)Convert.ToDouble(posObj);
                    }
                } catch {
                    return;
                }
            }

            // Calculate progress (0 to 1)
            float progress = Mathf.Clamp01(_currentTime / _songLength);

            // Update fill bar
            if (_fillRect != null) {
                _fillRect.anchorMax = new Vector2(progress, 1f);
            }
        }

        public void Show() {
            _isVisible = true;
            _songLength = 0f; // Reset to fetch new song length
            _currentTime = 0f;
            _jukeboxInstance = null; // Reset to find new jukebox instance
            _musicInstance = null; // Reset music instance

            if (_barContainer != null)
                _barContainer.SetActive(true);

            // Reset fill
            if (_fillRect != null)
                _fillRect.anchorMax = new Vector2(0f, 1f);
        }

        public void Hide() {
            _isVisible = false;
            if (_barContainer != null)
                _barContainer.SetActive(false);
        }

        private void OnDestroy() {
            if (_instance == this)
                _instance = null;
        }
    }
}
