using MelonLoader;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RiqMenu {
    public class CustomSongsScene {

        public delegate void CustomSongSelectedDelegate(string filename);
        public CustomSongSelectedDelegate CustomSongSelected;

        public Canvas mainCanvas => _mainCanvas;
        private Canvas _mainCanvas;

        private GameObject mainCanvasGO;

        private GameObject _contentPanel;

        public CustomSongsScene(MelonLogger.Instance logger = null) {
            // -----------------------------------------------------------------
            // Create main canvas
            mainCanvasGO = new GameObject("Main Canvas");
            mainCanvasGO.SetActive(false);
            mainCanvasGO.transform.SetParent(null, false);
            mainCanvasGO.transform.position = Vector3.zero;

            RectTransform mainCanvasTransform = mainCanvasGO.AddComponent<RectTransform>();
            _mainCanvas = mainCanvasGO.AddComponent<Canvas>();
            CanvasScaler mainCanvasScaler = mainCanvasGO.AddComponent<CanvasScaler>();
            GraphicRaycaster mainCanvasRaycaster = mainCanvasGO.AddComponent<GraphicRaycaster>();

            _mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            mainCanvasScaler.referenceResolution = new Vector2(1920, 1080);

            // -----------------------------------------------------------------
            // Setup scroll bar panel
            GameObject scrollArea = new GameObject("Scroll Area");
            RectTransform scrollTransform = scrollArea.AddComponent<RectTransform>();
            scrollArea.AddComponent<CanvasRenderer>();
            scrollTransform.SetParent(mainCanvasGO.transform, false);
            scrollTransform.anchorMin = new Vector2(0.5f, 0);
            scrollTransform.anchorMax = new Vector2(0.5f, 0);
            scrollTransform.pivot = new Vector2(0.5f, 0);
            scrollTransform.anchoredPosition = new Vector2(0, 50);

            Image bgImage = scrollArea.AddComponent<Image>();
            scrollArea.AddComponent<RectMask2D>();

            scrollTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 800);
            scrollTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 550);

            ScrollRect scrollRect = scrollArea.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 20f;

            // -----------------------------------------------------------------
            // Setup content panel
            _contentPanel = new GameObject("ContentPanel");
            RectTransform contentPanelTransform = _contentPanel.AddComponent<RectTransform>();
            
            contentPanelTransform.SetParent(scrollArea.transform,false);
            // Couldn't get this to work any other way. heckin unity
            contentPanelTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 800);
            contentPanelTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 550);

            _contentPanel.AddComponent<CanvasRenderer>();
            VerticalLayoutGroup layoutGroup = _contentPanel.AddComponent<VerticalLayoutGroup>();

            layoutGroup.padding = new RectOffset(8, 8, 8, 8);
            layoutGroup.spacing = 16;

            layoutGroup.childAlignment = TextAnchor.MiddleCenter;

            layoutGroup.childScaleWidth = false;
            layoutGroup.childScaleHeight = false;

            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;

            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;

            ContentSizeFitter contentSizeFitter = _contentPanel.AddComponent<ContentSizeFitter>();
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // -----------------------------------------------------------------
            // Set scroll rect content
            scrollRect.content = contentPanelTransform;
        }

        public void SetVisible(bool isVisible) {
            if (mainCanvasGO == null) {
                return;
            }
            mainCanvasGO.SetActive(isVisible);
        }

        public void SetContent(string[] filenames) {
            ClearContentPanelChildren();

            for (int i = 0; i < filenames.Length; i++) {
                CreateSongPanel(filenames[i], _contentPanel.transform);
            }
        }

        void ClearContentPanelChildren() {
            int childCount = _contentPanel.transform.childCount;
            for (int i = 0; i < childCount; i++) {
                if (_contentPanel.transform.GetChild(i) == null) continue;

                GameObject.Destroy(_contentPanel.transform.GetChild(i).gameObject);
            }
        }

        private GameObject CreateSongPanel(string songName, Transform parent) {
            // Create main panel
            GameObject songGO = new GameObject(songName);
            RectTransform songTransform = songGO.AddComponent<RectTransform>();
            songGO.AddComponent<CanvasRenderer>();
            Image songBGImage = songGO.AddComponent<Image>();
            Button songButton = songGO.AddComponent<Button>();

            songButton.onClick.AddListener(() => {
                CustomSongSelected?.Invoke(songName);
            });

            songTransform.SetParent(parent);
            songBGImage.color = Color.black;

            // Set panel height
            songTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100);

            // Create text
            GameObject textGO = new GameObject($"{songName} Text");
            RectTransform textTransform = textGO.AddComponent<RectTransform>();
            textTransform.anchorMin = Vector2.zero;
            textTransform.anchorMax = Vector2.one;
            textTransform.pivot = new Vector2(0.5f, 0.5f);
            textTransform.SetParent(songGO.transform);

            textGO.AddComponent<CanvasRenderer>();
            TextMeshProUGUI textText = textGO.AddComponent<TextMeshProUGUI>();
            textText.color = Color.white;
            textText.verticalAlignment = VerticalAlignmentOptions.Middle;
            textText.horizontalAlignment = HorizontalAlignmentOptions.Left;
            textText.text = Path.GetFileName(songName);

            return songGO;
        }
    }
}
