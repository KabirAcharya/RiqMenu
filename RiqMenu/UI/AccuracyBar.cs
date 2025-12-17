using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;

namespace RiqMenu.UI {
    /// <summary>
    /// Displays a color-coded accuracy indicator bar at the bottom of the screen during gameplay.
    /// Shows timing of each hit relative to perfect timing.
    /// </summary>
    public class AccuracyBar : MonoBehaviour {
        private static AccuracyBar _instance;
        public static AccuracyBar Instance => _instance;

        // UI Elements
        private GameObject _barContainer;
        private RectTransform _barRect;
        private Image _backgroundImage;
        private List<GameObject> _hitIndicators = new List<GameObject>();

        // Settings
        private const float BAR_HEIGHT = 8f;
        private const float BAR_WIDTH_PERCENT = 0.4f; // 40% of screen width
        private const float INDICATOR_WIDTH = 3f;
        private const float INDICATOR_LIFETIME = 2f;
        private const float CENTER_LINE_WIDTH = 2f;

        // Colors matching judgement types
        private static readonly Color PerfectColor = new Color(0.3f, 0.85f, 1f, 1f);    // Cyan
        private static readonly Color HitColor = new Color(0.3f, 1f, 0.3f, 1f);         // Green
        private static readonly Color AlmostColor = new Color(1f, 0.8f, 0.2f, 1f);      // Yellow/Orange
        private static readonly Color MissColor = new Color(1f, 0.3f, 0.3f, 1f);        // Red
        private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.6f);    // Semi-transparent black
        private static readonly Color CenterLineColor = new Color(1f, 1f, 1f, 0.8f);    // White

        // Timing windows (from Judge.cs)
        private const float PERFECT_WINDOW = 0.035f;
        private const float HIT_WINDOW = 0.075f;
        private const float ALMOST_WINDOW = 0.18f;

        private bool _isVisible = false;
        private Canvas _canvas;

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
            _canvas.sortingOrder = 1000; // On top of everything

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            gameObject.AddComponent<GraphicRaycaster>();

            // Create bar container
            _barContainer = new GameObject("AccuracyBarContainer");
            _barContainer.transform.SetParent(transform, false);
            _barRect = _barContainer.AddComponent<RectTransform>();

            // Position at bottom center
            _barRect.anchorMin = new Vector2(0.5f, 0f);
            _barRect.anchorMax = new Vector2(0.5f, 0f);
            _barRect.pivot = new Vector2(0.5f, 0f);
            _barRect.anchoredPosition = new Vector2(0f, 20f);
            _barRect.sizeDelta = new Vector2(Screen.width * BAR_WIDTH_PERCENT, BAR_HEIGHT);

            // Background
            _backgroundImage = _barContainer.AddComponent<Image>();
            _backgroundImage.color = BackgroundColor;

            // Create colored zones to show timing windows
            CreateTimingZones();

            // Center line (perfect timing marker)
            CreateCenterLine();
        }

        private void CreateTimingZones() {
            float barWidth = Screen.width * BAR_WIDTH_PERCENT;

            // Calculate zone widths as proportion of half-bar (since center is 0)
            // Each zone extends from center, so multiply by 2 for full width
            float perfectZoneHalfWidth = (PERFECT_WINDOW / ALMOST_WINDOW) * (barWidth / 2f);
            float hitZoneHalfWidth = (HIT_WINDOW / ALMOST_WINDOW) * (barWidth / 2f);

            // Perfect zone (center) - full width is 2x half width
            CreateZone("PerfectZone", 0f, perfectZoneHalfWidth * 2f, new Color(PerfectColor.r, PerfectColor.g, PerfectColor.b, 0.3f));

            // Hit zones (left and right of perfect)
            float hitZoneWidth = hitZoneHalfWidth - perfectZoneHalfWidth;
            CreateZone("HitZoneLeft", -perfectZoneHalfWidth - (hitZoneWidth / 2f), hitZoneWidth, new Color(HitColor.r, HitColor.g, HitColor.b, 0.2f));
            CreateZone("HitZoneRight", perfectZoneHalfWidth + (hitZoneWidth / 2f), hitZoneWidth, new Color(HitColor.r, HitColor.g, HitColor.b, 0.2f));
        }

        private void CreateZone(string name, float xOffset, float width, Color color) {
            var zone = new GameObject(name);
            zone.transform.SetParent(_barContainer.transform, false);

            var rect = zone.AddComponent<RectTransform>();
            // Anchor to center of parent
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(xOffset, 0f);
            rect.sizeDelta = new Vector2(width, 0f);

            var img = zone.AddComponent<Image>();
            img.color = color;
        }

        private void CreateCenterLine() {
            var centerLine = new GameObject("CenterLine");
            centerLine.transform.SetParent(_barContainer.transform, false);

            var rect = centerLine.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(CENTER_LINE_WIDTH, 0f);

            var img = centerLine.AddComponent<Image>();
            img.color = CenterLineColor;
        }

        /// <summary>
        /// Called when a hit occurs. Delta is the timing offset from perfect (negative = early, positive = late).
        /// </summary>
        public void RegisterHit(float delta, Judgement judgement) {
            if (!_isVisible) return;

            // Clamp delta to the Almost window
            float clampedDelta = Mathf.Clamp(delta, -ALMOST_WINDOW, ALMOST_WINDOW);

            // Convert delta to position on bar (-1 to 1 range, then to pixels)
            float normalizedPos = clampedDelta / ALMOST_WINDOW;
            float barWidth = _barRect.sizeDelta.x;
            float xPos = normalizedPos * (barWidth / 2f);

            // Get color based on judgement
            Color color = GetColorForJudgement(judgement);

            // Create indicator
            CreateHitIndicator(xPos, color);
        }

        private Color GetColorForJudgement(Judgement judgement) {
            switch (judgement) {
                case Judgement.Perfect:
                    return PerfectColor;
                case Judgement.Hit:
                    return HitColor;
                case Judgement.Almost:
                    return AlmostColor;
                case Judgement.Miss:
                case Judgement.Bad:
                default:
                    return MissColor;
            }
        }

        private void CreateHitIndicator(float xPos, Color color) {
            var indicator = new GameObject("HitIndicator");
            indicator.transform.SetParent(_barContainer.transform, false);

            var rect = indicator.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(xPos, 0f);
            rect.sizeDelta = new Vector2(INDICATOR_WIDTH, 0f);

            var img = indicator.AddComponent<Image>();
            img.color = color;

            _hitIndicators.Add(indicator);

            // Fade out and destroy after lifetime
            StartCoroutine(FadeAndDestroy(indicator, img, INDICATOR_LIFETIME));
        }

        private System.Collections.IEnumerator FadeAndDestroy(GameObject obj, Image img, float lifetime) {
            float elapsed = 0f;
            Color startColor = img.color;

            // Stay visible for first half, then fade
            float fadeStart = lifetime * 0.5f;

            while (elapsed < lifetime) {
                // Check if object was destroyed externally
                if (obj == null || img == null)
                    yield break;

                elapsed += Time.deltaTime;

                if (elapsed > fadeStart) {
                    float fadeProgress = (elapsed - fadeStart) / (lifetime - fadeStart);
                    img.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Lerp(1f, 0f, fadeProgress));
                }

                yield return null;
            }

            if (obj != null) {
                _hitIndicators.Remove(obj);
                Destroy(obj);
            }
        }

        public void Show() {
            _isVisible = true;
            if (_barContainer != null)
                _barContainer.SetActive(true);

            // Update bar size for current screen
            if (_barRect != null)
                _barRect.sizeDelta = new Vector2(Screen.width * BAR_WIDTH_PERCENT, BAR_HEIGHT);
        }

        public void Hide() {
            _isVisible = false;
            if (_barContainer != null)
                _barContainer.SetActive(false);

            // Clear all indicators
            ClearIndicators();
        }

        public void ClearIndicators() {
            foreach (var indicator in _hitIndicators) {
                if (indicator != null)
                    Destroy(indicator);
            }
            _hitIndicators.Clear();
        }

        private void OnDestroy() {
            if (_instance == this)
                _instance = null;
        }
    }
}
