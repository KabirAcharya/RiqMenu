using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using RiqMenu.Core;
using RiqMenu.Online;

namespace RiqMenu.UI.Toolkit {
    public enum OverlayTab {
        Local,
        Online
    }

    /// <summary>
    /// Modern UI Toolkit-based overlay for song selection
    /// Styled to match the Riqs & Mods website
    /// </summary>
    public class ToolkitOverlay : MonoBehaviour {
        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _overlay;
        private bool _isVisible = false;

        // Tab state
        private OverlayTab _currentTab = OverlayTab.Local;
        private Button _localTab;
        private Button _onlineTab;
        private VisualElement _localContent;
        private VisualElement _onlineContent;

        // Local songs
        private ScrollView _localSongList;
        private TextField _localSearchField;
        private int _selectedLocalIndex = 0;
        private List<VisualElement> _localSongElements = new List<VisualElement>();
        private List<int> _filteredLocalIndices = new List<int>();
        private string _localSearchQuery = "";
        private Label _audioStatusLabel;
        private Label _autoplayLabel;
        private const int PAGE_SIZE = 10;

        // Online songs
        private ScrollView _onlineSongList;
        private TextField _onlineSearchField;
        private RiqsApiClient _apiClient = new RiqsApiClient();
        private List<OnlineSong> _onlineSongs = new List<OnlineSong>();
        private int _selectedOnlineIndex = 0;
        private List<VisualElement> _onlineSongElements = new List<VisualElement>();
        private bool _isLoading = false;
        private string _onlineSort = "newest";
        private int _currentPage = 1;
        private bool _hasMorePages = true;
        private string _currentSearchQuery = null;
        private float _onlineSearchDebounceTime = 0f;
        private string _pendingOnlineSearch = null;
        private const float ONLINE_SEARCH_DEBOUNCE_DELAY = 0.4f;

        // Status
        private VisualElement _statusContainer;

        // Audio preview
        private int _lastPreviewedSong = -1;
        private bool _isLoadingAudio = false;
        private float _loadingStartTime = 0f;

        public bool IsVisible => _isVisible;
        public event System.Action OnOverlayOpened;
        public event System.Action OnOverlayClosed;
        public event System.Action<int> OnSongSelected;

        // Delay timer to ignore input briefly after opening
        private float _inputDelayTimer = 0f;
        private const float INPUT_DELAY = 0.15f; // 150ms delay after opening

        // Search mode flag - more reliable than focus detection
        private bool _isSearchMode = false;

        // Metadata editor modal
        private bool _isEditorOpen = false;
        private bool _editorJustClosed = false; // Prevent Enter from playing song after editor closes
        private VisualElement _editorModal;
        private TextField _editorTitleField;
        private TextField _editorCreatorField;
        private TextField _editorBpmField;
        private TextField _editorDifficultyField;
        private CustomSong _editingSong;

        private void Awake() {
            CreateUIDocument();
        }

        private AssetBundle _themeBundle;

        private void CreateUIDocument() {
            // Create UIDocument component
            _uiDocument = gameObject.AddComponent<UIDocument>();

            // Create a PanelSettings asset at runtime
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);

            // Try to load theme from embedded AssetBundle resource
            ThemeStyleSheet defaultTheme = null;

            try {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("RiqMenu.Assets.riqmenu_theme")) {
                    if (stream != null) {
                        byte[] bundleData = new byte[stream.Length];
                        stream.Read(bundleData, 0, bundleData.Length);
                        _themeBundle = AssetBundle.LoadFromMemory(bundleData);
                        if (_themeBundle != null) {
                            defaultTheme = _themeBundle.LoadAsset<ThemeStyleSheet>("UnityDefaultRuntimeTheme");
                            if (defaultTheme != null) {
                                Debug.Log("[ToolkitOverlay] Theme loaded from embedded AssetBundle");
                            }
                        }
                    }
                    else {
                        Debug.LogWarning("[ToolkitOverlay] Embedded theme resource not found");
                    }
                }
            }
            catch (System.Exception ex) {
                Debug.LogWarning($"[ToolkitOverlay] Failed to load embedded theme: {ex.Message}");
            }

            // Fallback: try to find any existing theme
            if (defaultTheme == null) {
                var allThemes = Resources.FindObjectsOfTypeAll<ThemeStyleSheet>();
                if (allThemes.Length > 0) {
                    defaultTheme = allThemes[0];
                    Debug.Log($"[ToolkitOverlay] Using found theme: {defaultTheme.name}");
                }
            }

            if (defaultTheme != null) {
                panelSettings.themeStyleSheet = defaultTheme;
            }
            else {
                Debug.LogError("[ToolkitOverlay] Could not find theme - UI will not render!");
            }

            _uiDocument.panelSettings = panelSettings;

            // Build the UI
            BuildUI();
        }

        private void OnDestroy() {
            if (_themeBundle != null) {
                _themeBundle.Unload(true);
                _themeBundle = null;
            }
        }

        private void BuildUI() {
            _root = _uiDocument.rootVisualElement;

            // Load styles
            var styleSheet = ScriptableObject.CreateInstance<StyleSheet>();
            // Note: In production, we'd parse the USS. For now, apply styles inline.

            // Create overlay container (hidden by default)
            _overlay = new VisualElement();
            _overlay.name = "riq-overlay";
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.right = 0;
            _overlay.style.top = 0;
            _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new Color(0, 0, 0, 0.85f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.display = DisplayStyle.None;
            _overlay.focusable = true;
            _overlay.tabIndex = -1; // Disable tab navigation

            // Intercept Tab key to prevent UI Toolkit's default tab navigation
            _overlay.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.keyCode == KeyCode.Tab) {
                    evt.StopPropagation();
                    evt.PreventDefault();
                    // Tab handling is done in HandleInput via UnityEngine.Input
                }
            }, TrickleDown.TrickleDown);

            // Main card
            var card = CreateCard();
            _overlay.Add(card);

            _root.Add(_overlay);

            // Create metadata editor modal (hidden by default)
            CreateEditorModal();
        }

        private void CreateEditorModal() {
            _editorModal = new VisualElement();
            _editorModal.name = "editor-modal";
            _editorModal.style.position = Position.Absolute;
            _editorModal.style.left = 0;
            _editorModal.style.right = 0;
            _editorModal.style.top = 0;
            _editorModal.style.bottom = 0;
            _editorModal.style.backgroundColor = new Color(0, 0, 0, 0.7f);
            _editorModal.style.alignItems = Align.Center;
            _editorModal.style.justifyContent = Justify.Center;
            _editorModal.style.display = DisplayStyle.None;

            var modalCard = new VisualElement();
            modalCard.style.backgroundColor = ParseColor(RiqMenuStyles.Cream);
            modalCard.style.borderTopLeftRadius = 16;
            modalCard.style.borderTopRightRadius = 16;
            modalCard.style.borderBottomLeftRadius = 16;
            modalCard.style.borderBottomRightRadius = 16;
            modalCard.style.paddingTop = 24;
            modalCard.style.paddingBottom = 24;
            modalCard.style.paddingLeft = 32;
            modalCard.style.paddingRight = 32;
            modalCard.style.width = 400;

            // Title
            var title = new Label("Edit Song Metadata");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ParseColor(RiqMenuStyles.Charcoal);
            title.style.marginBottom = 20;
            modalCard.Add(title);

            // Title field
            var titleLabel = new Label("Title");
            titleLabel.style.fontSize = 12;
            titleLabel.style.color = ParseColor(RiqMenuStyles.Gray);
            titleLabel.style.marginBottom = 4;
            modalCard.Add(titleLabel);

            _editorTitleField = new TextField();
            _editorTitleField.style.marginBottom = 12;
            ApplyTextFieldStyle(_editorTitleField);
            modalCard.Add(_editorTitleField);

            // Creator field
            var creatorLabel = new Label("Creator");
            creatorLabel.style.fontSize = 12;
            creatorLabel.style.color = ParseColor(RiqMenuStyles.Gray);
            creatorLabel.style.marginBottom = 4;
            modalCard.Add(creatorLabel);

            _editorCreatorField = new TextField();
            _editorCreatorField.style.marginBottom = 12;
            ApplyTextFieldStyle(_editorCreatorField);
            modalCard.Add(_editorCreatorField);

            // BPM field
            var bpmLabel = new Label("BPM");
            bpmLabel.style.fontSize = 12;
            bpmLabel.style.color = ParseColor(RiqMenuStyles.Gray);
            bpmLabel.style.marginBottom = 4;
            modalCard.Add(bpmLabel);

            _editorBpmField = new TextField();
            _editorBpmField.style.marginBottom = 12;
            ApplyTextFieldStyle(_editorBpmField);
            modalCard.Add(_editorBpmField);

            // Difficulty field
            var diffLabel = new Label("Difficulty (easy/medium/hard/extreme)");
            diffLabel.style.fontSize = 12;
            diffLabel.style.color = ParseColor(RiqMenuStyles.Gray);
            diffLabel.style.marginBottom = 4;
            modalCard.Add(diffLabel);

            _editorDifficultyField = new TextField();
            _editorDifficultyField.style.marginBottom = 20;
            ApplyTextFieldStyle(_editorDifficultyField);
            modalCard.Add(_editorDifficultyField);

            // Buttons
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.FlexEnd;

            var cancelBtn = new Button(() => CloseEditor(false)) { text = "Cancel" };
            cancelBtn.style.marginRight = 8;
            cancelBtn.style.paddingLeft = 16;
            cancelBtn.style.paddingRight = 16;
            cancelBtn.style.paddingTop = 8;
            cancelBtn.style.paddingBottom = 8;
            cancelBtn.style.backgroundColor = ParseColor(RiqMenuStyles.GrayLighter);
            cancelBtn.style.color = ParseColor(RiqMenuStyles.Charcoal);
            cancelBtn.style.borderTopLeftRadius = 8;
            cancelBtn.style.borderTopRightRadius = 8;
            cancelBtn.style.borderBottomLeftRadius = 8;
            cancelBtn.style.borderBottomRightRadius = 8;
            buttonRow.Add(cancelBtn);

            var saveBtn = new Button(() => CloseEditor(true)) { text = "Save" };
            saveBtn.style.paddingLeft = 16;
            saveBtn.style.paddingRight = 16;
            saveBtn.style.paddingTop = 8;
            saveBtn.style.paddingBottom = 8;
            saveBtn.style.backgroundColor = ParseColor(RiqMenuStyles.Cyan);
            saveBtn.style.color = Color.white;
            saveBtn.style.borderTopLeftRadius = 8;
            saveBtn.style.borderTopRightRadius = 8;
            saveBtn.style.borderBottomLeftRadius = 8;
            saveBtn.style.borderBottomRightRadius = 8;
            buttonRow.Add(saveBtn);

            modalCard.Add(buttonRow);

            // Hint
            var hint = new Label("Press Escape to cancel, Enter to save");
            hint.style.fontSize = 11;
            hint.style.color = ParseColor(RiqMenuStyles.Gray);
            hint.style.marginTop = 12;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            modalCard.Add(hint);

            _editorModal.Add(modalCard);
            _root.Add(_editorModal);
        }

        private void ApplyTextFieldStyle(TextField field) {
            field.style.backgroundColor = Color.white;
            field.style.borderTopWidth = 2;
            field.style.borderBottomWidth = 2;
            field.style.borderLeftWidth = 2;
            field.style.borderRightWidth = 2;
            field.style.borderTopColor = ParseColor(RiqMenuStyles.GrayLighter);
            field.style.borderBottomColor = ParseColor(RiqMenuStyles.GrayLighter);
            field.style.borderLeftColor = ParseColor(RiqMenuStyles.GrayLighter);
            field.style.borderRightColor = ParseColor(RiqMenuStyles.GrayLighter);
            field.style.borderTopLeftRadius = 8;
            field.style.borderTopRightRadius = 8;
            field.style.borderBottomLeftRadius = 8;
            field.style.borderBottomRightRadius = 8;
            field.style.paddingLeft = 12;
            field.style.paddingRight = 12;
            field.style.paddingTop = 8;
            field.style.paddingBottom = 8;
        }

        private void OpenEditor() {
            if (_currentTab != OverlayTab.Local) return;

            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            var song = songManager.GetSong(_selectedLocalIndex);
            if (song == null) return;

            _editingSong = song;
            _editorTitleField.value = song.SongTitle ?? "";
            _editorCreatorField.value = song.Creator ?? "";
            _editorBpmField.value = song.Bpm?.ToString() ?? "";
            _editorDifficultyField.value = song.Difficulty ?? "";

            _editorModal.style.display = DisplayStyle.Flex;
            _isEditorOpen = true;

            // Focus title field
            _editorTitleField.schedule.Execute(() => _editorTitleField.Focus()).StartingIn(50);
        }

        private void CloseEditor(bool save) {
            if (save && _editingSong != null) {
                var songManager = RiqMenuSystemManager.Instance?.SongManager;
                if (songManager != null) {
                    float? bpm = null;
                    if (float.TryParse(_editorBpmField.value, out float parsedBpm)) {
                        bpm = parsedBpm;
                    }

                    songManager.SaveMetadata(
                        _editingSong,
                        _editorTitleField.value,
                        _editorCreatorField.value,
                        bpm,
                        _editorDifficultyField.value
                    );

                    // Refresh the song list display
                    RefreshLocalSongList();
                }
            }

            _editorModal.style.display = DisplayStyle.None;
            _isEditorOpen = false;
            _editorJustClosed = true; // Prevent Enter from playing song this frame
            _editingSong = null;
        }

        private VisualElement CreateCard() {
            var card = new VisualElement();
            card.name = "riq-card";
            ApplyCardStyle(card);

            // Tabs
            var tabsContainer = CreateTabs();
            card.Add(tabsContainer);

            // Content container
            var contentContainer = new VisualElement();
            contentContainer.style.flexGrow = 1;
            contentContainer.style.flexShrink = 1;

            _localContent = CreateLocalContent();
            _onlineContent = CreateOnlineContent();
            _onlineContent.style.display = DisplayStyle.None;

            contentContainer.Add(_localContent);
            contentContainer.Add(_onlineContent);
            card.Add(contentContainer);

            // Footer
            var footer = CreateFooter();
            card.Add(footer);

            return card;
        }

        private void ApplyCardStyle(VisualElement card) {
            card.style.width = 720;
            card.style.height = 620;
            card.style.backgroundColor = ParseColor(RiqMenuStyles.WarmWhite);
            card.style.borderTopLeftRadius = 20;
            card.style.borderTopRightRadius = 20;
            card.style.borderBottomLeftRadius = 20;
            card.style.borderBottomRightRadius = 20;
            card.style.borderTopWidth = 3;
            card.style.borderBottomWidth = 3;
            card.style.borderLeftWidth = 3;
            card.style.borderRightWidth = 3;
            card.style.borderTopColor = ParseColor(RiqMenuStyles.GrayLighter);
            card.style.borderBottomColor = ParseColor(RiqMenuStyles.GrayLighter);
            card.style.borderLeftColor = ParseColor(RiqMenuStyles.GrayLighter);
            card.style.borderRightColor = ParseColor(RiqMenuStyles.GrayLighter);
            card.style.overflow = Overflow.Hidden;
        }

        private VisualElement CreateTabs() {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.FlexEnd;
            container.style.flexShrink = 0;
            container.style.backgroundColor = ParseColor(RiqMenuStyles.Cream);
            container.style.borderBottomWidth = 3;
            container.style.borderBottomColor = ParseColor(RiqMenuStyles.GrayLighter);
            container.style.paddingLeft = 24;
            container.style.paddingRight = 24;
            container.style.paddingTop = 20;

            _localTab = CreateTab("Local", true);
            _localTab.clicked += () => SwitchTab(OverlayTab.Local);
            container.Add(_localTab);

            _onlineTab = CreateTab("Online", false);
            _onlineTab.clicked += () => SwitchTab(OverlayTab.Online);
            container.Add(_onlineTab);

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            container.Add(spacer);

            // Hint with icon style
            var hint = new Label("← → to switch tabs");
            hint.style.unityTextAlign = TextAnchor.MiddleRight;
            hint.style.fontSize = 12;
            hint.style.color = ParseColor(RiqMenuStyles.GrayLight);
            hint.style.paddingBottom = 14;
            container.Add(hint);

            return container;
        }

        private Button CreateTab(string text, bool active) {
            var tab = new Button();
            tab.text = text;
            tab.style.paddingLeft = 24;
            tab.style.paddingRight = 24;
            tab.style.paddingTop = 12;
            tab.style.paddingBottom = 12;
            tab.style.marginRight = 8;
            tab.style.borderTopLeftRadius = 12;
            tab.style.borderTopRightRadius = 12;
            tab.style.borderBottomLeftRadius = 0;
            tab.style.borderBottomRightRadius = 0;
            tab.style.borderTopWidth = 0;
            tab.style.borderBottomWidth = 0;
            tab.style.borderLeftWidth = 0;
            tab.style.borderRightWidth = 0;
            tab.style.unityFontStyleAndWeight = FontStyle.Bold;
            tab.style.fontSize = 14;

            ApplyTabStyle(tab, active);

            return tab;
        }

        private void ApplyTabStyle(Button tab, bool active) {
            // Always have 3px borders to prevent shifting
            tab.style.borderTopWidth = 3;
            tab.style.borderLeftWidth = 3;
            tab.style.borderRightWidth = 3;
            tab.style.marginBottom = -3;
            tab.style.paddingBottom = 15;

            if (active) {
                tab.style.backgroundColor = ParseColor(RiqMenuStyles.WarmWhite);
                tab.style.color = ParseColor(RiqMenuStyles.CyanDark);
                tab.style.borderTopColor = ParseColor(RiqMenuStyles.GrayLighter);
                tab.style.borderLeftColor = ParseColor(RiqMenuStyles.GrayLighter);
                tab.style.borderRightColor = ParseColor(RiqMenuStyles.GrayLighter);
            }
            else {
                tab.style.backgroundColor = Color.clear;
                tab.style.color = ParseColor(RiqMenuStyles.Gray);
                // Transparent borders to keep same size
                tab.style.borderTopColor = Color.clear;
                tab.style.borderLeftColor = Color.clear;
                tab.style.borderRightColor = Color.clear;
            }
        }

        private VisualElement CreateLocalContent() {
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            content.style.flexDirection = FlexDirection.Column;
            content.style.paddingLeft = 24;
            content.style.paddingRight = 24;
            content.style.paddingTop = 20;
            content.style.paddingBottom = 16;

            // Title row
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.marginBottom = 16;
            titleRow.style.flexShrink = 0;

            var title = new Label("Local Songs");
            title.style.fontSize = 22;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ParseColor(RiqMenuStyles.Charcoal);
            title.style.flexGrow = 1;
            titleRow.Add(title);

            var countLabel = new Label();
            countLabel.name = "local-count";
            // Style like the Newest badge
            countLabel.style.paddingLeft = 12;
            countLabel.style.paddingRight = 12;
            countLabel.style.paddingTop = 4;
            countLabel.style.paddingBottom = 4;
            countLabel.style.marginLeft = 12;
            countLabel.style.backgroundColor = ParseColor(RiqMenuStyles.GrayLighter);
            countLabel.style.color = ParseColor(RiqMenuStyles.Gray);
            countLabel.style.borderTopLeftRadius = 50;
            countLabel.style.borderTopRightRadius = 50;
            countLabel.style.borderBottomLeftRadius = 50;
            countLabel.style.borderBottomRightRadius = 50;
            countLabel.style.fontSize = 11;
            countLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleRow.Add(countLabel);

            content.Add(titleRow);

            // Search
            _localSearchField = CreateSearchField("Search local songs...");
            _localSearchField.style.flexShrink = 0;
            _localSearchField.RegisterValueChangedCallback(evt => {
                string newValue = evt.newValue;
                if (newValue == "Search local songs..." || newValue == "") {
                    _localSearchQuery = "";
                }
                else {
                    _localSearchQuery = newValue;
                }
                _selectedLocalIndex = 0;
                RefreshLocalSongList();
                TryPreviewCurrentSong();
            });
            content.Add(_localSearchField);

            // Song list
            _localSongList = new ScrollView(ScrollViewMode.Vertical);
            _localSongList.style.flexGrow = 1;
            _localSongList.style.flexShrink = 1;
            _localSongList.tabIndex = -1; // Disable tab navigation
            StyleScrollView(_localSongList);
            content.Add(_localSongList);

            // Status area (audio status + autoplay)
            var statusArea = new VisualElement();
            statusArea.style.flexDirection = FlexDirection.Column;
            statusArea.style.flexShrink = 0;
            statusArea.style.marginTop = 8;

            _audioStatusLabel = new Label("");
            _audioStatusLabel.style.fontSize = 13;
            _audioStatusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _audioStatusLabel.style.color = ParseColor(RiqMenuStyles.Yellow);
            _audioStatusLabel.style.marginBottom = 4;
            statusArea.Add(_audioStatusLabel);

            _autoplayLabel = new Label("Autoplay: OFF (P)");
            _autoplayLabel.style.fontSize = 13;
            _autoplayLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _autoplayLabel.style.color = ParseColor(RiqMenuStyles.Coral);
            statusArea.Add(_autoplayLabel);

            content.Add(statusArea);

            return content;
        }

        private VisualElement CreateOnlineContent() {
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            content.style.flexDirection = FlexDirection.Column;
            content.style.paddingLeft = 24;
            content.style.paddingRight = 24;
            content.style.paddingTop = 20;
            content.style.paddingBottom = 16;

            // Title row
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.marginBottom = 16;
            titleRow.style.flexShrink = 0;

            var title = new Label("Online Songs");
            title.style.fontSize = 22;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ParseColor(RiqMenuStyles.Charcoal);
            title.style.flexGrow = 1;
            titleRow.Add(title);

            // Sort badge
            var sortBadge = new Label("Newest");
            sortBadge.name = "sort-label";
            sortBadge.style.paddingLeft = 12;
            sortBadge.style.paddingRight = 12;
            sortBadge.style.paddingTop = 4;
            sortBadge.style.paddingBottom = 4;
            sortBadge.style.marginLeft = 12;
            sortBadge.style.backgroundColor = ParseColor(RiqMenuStyles.CyanLight);
            sortBadge.style.color = ParseColor(RiqMenuStyles.CyanDark);
            sortBadge.style.borderTopLeftRadius = 50;
            sortBadge.style.borderTopRightRadius = 50;
            sortBadge.style.borderBottomLeftRadius = 50;
            sortBadge.style.borderBottomRightRadius = 50;
            sortBadge.style.fontSize = 11;
            sortBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleRow.Add(sortBadge);

            content.Add(titleRow);

            // Search with debouncing
            _onlineSearchField = CreateSearchField("Search online songs...");
            _onlineSearchField.style.flexShrink = 0;
            _onlineSearchField.RegisterValueChangedCallback(evt => {
                string newValue = evt.newValue;
                if (newValue == "Search online songs..." || newValue == "") {
                    _pendingOnlineSearch = null;
                    // If we had a search active, reload default songs
                    if (!string.IsNullOrEmpty(_currentSearchQuery)) {
                        _currentSearchQuery = null;
                        LoadOnlineSongs();
                    }
                } else {
                    // Debounce: set pending search and timer
                    _pendingOnlineSearch = newValue;
                    _onlineSearchDebounceTime = ONLINE_SEARCH_DEBOUNCE_DELAY;
                }
            });
            content.Add(_onlineSearchField);

            // Status container
            _statusContainer = new VisualElement();
            _statusContainer.style.display = DisplayStyle.None;
            _statusContainer.style.flexShrink = 0;
            content.Add(_statusContainer);

            // Song list
            _onlineSongList = new ScrollView(ScrollViewMode.Vertical);
            _onlineSongList.style.flexGrow = 1;
            _onlineSongList.style.flexShrink = 1;
            _onlineSongList.tabIndex = -1; // Disable tab navigation
            StyleScrollView(_onlineSongList);
            content.Add(_onlineSongList);

            return content;
        }

        private void StyleScrollView(ScrollView scrollView) {
            // Always visible to prevent content shifting
            scrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;

            // Fixed padding to prevent content shift
            scrollView.contentContainer.style.paddingRight = 8;

            var scroller = scrollView.verticalScroller;

            // Thin scrollbar
            scroller.style.width = 6;

            // Hide arrow buttons
            var lowBtn = scroller.Q("unity-low-button");
            var highBtn = scroller.Q("unity-high-button");
            if (lowBtn != null) lowBtn.style.display = DisplayStyle.None;
            if (highBtn != null) highBtn.style.display = DisplayStyle.None;

            // Transparent track
            var slider = scroller.Q("unity-slider");
            if (slider != null) {
                slider.style.backgroundColor = Color.clear;
                slider.style.marginTop = 0;
                slider.style.marginBottom = 0;
            }

            var tracker = scroller.Q("unity-tracker");
            if (tracker != null) {
                tracker.style.backgroundColor = Color.clear;
                tracker.style.borderTopWidth = 0;
                tracker.style.borderBottomWidth = 0;
                tracker.style.borderLeftWidth = 0;
                tracker.style.borderRightWidth = 0;
            }

            // Simple thumb - no borders, no radius
            var dragger = scroller.Q("unity-dragger");
            if (dragger != null) {
                dragger.style.backgroundColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
                dragger.style.borderTopWidth = 0;
                dragger.style.borderBottomWidth = 0;
                dragger.style.borderLeftWidth = 0;
                dragger.style.borderRightWidth = 0;
            }

            var draggerBorder = scroller.Q("unity-dragger-border");
            if (draggerBorder != null) {
                draggerBorder.style.display = DisplayStyle.None;
            }
        }

        private TextField CreateSearchField(string placeholder) {
            var field = new TextField();
            field.value = placeholder;
            field.style.height = 48;
            field.style.marginBottom = 16;

            // Start non-focusable to prevent accidental focus during navigation
            // EnterSearchMode will enable focus when needed
            field.focusable = false;
            field.delegatesFocus = true;

            // Style the TextField itself
            field.style.paddingLeft = 24;
            field.style.paddingRight = 24;
            field.style.borderTopLeftRadius = 24;
            field.style.borderTopRightRadius = 24;
            field.style.borderBottomLeftRadius = 24;
            field.style.borderBottomRightRadius = 24;
            field.style.borderTopWidth = 3;
            field.style.borderBottomWidth = 3;
            field.style.borderLeftWidth = 3;
            field.style.borderRightWidth = 3;
            field.style.borderTopColor = ParseColor(RiqMenuStyles.GrayLighter);
            field.style.borderBottomColor = ParseColor(RiqMenuStyles.GrayLighter);
            field.style.borderLeftColor = ParseColor(RiqMenuStyles.GrayLighter);
            field.style.borderRightColor = ParseColor(RiqMenuStyles.GrayLighter);
            field.style.backgroundColor = Color.white;

            // Style inner input after it's attached to visual tree
            field.RegisterCallback<AttachToPanelEvent>(evt => {
                var input = field.Q<VisualElement>("unity-text-input");
                if (input != null) {
                    input.style.paddingTop = 0;
                    input.style.paddingBottom = 0;
                    input.style.backgroundColor = Color.clear;
                    input.style.fontSize = 15;
                    input.style.color = ParseColor(RiqMenuStyles.GrayLight);
                    input.style.unityTextAlign = TextAnchor.MiddleLeft;
                    input.style.borderTopWidth = 0;
                    input.style.borderBottomWidth = 0;
                    input.style.borderLeftWidth = 0;
                    input.style.borderRightWidth = 0;
                }
            });

            // Clear placeholder on focus
            field.RegisterCallback<FocusInEvent>(evt => {
                if (field.value == placeholder) {
                    field.value = "";
                    var inp = field.Q<VisualElement>("unity-text-input");
                    if (inp != null) inp.style.color = ParseColor(RiqMenuStyles.Charcoal);
                }
            });

            field.RegisterCallback<FocusOutEvent>(evt => {
                if (string.IsNullOrEmpty(field.value)) {
                    field.value = placeholder;
                    var inp = field.Q<VisualElement>("unity-text-input");
                    if (inp != null) inp.style.color = ParseColor(RiqMenuStyles.GrayLight);
                }
            });

            return field;
        }

        private VisualElement CreateFooter() {
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Column;
            footer.style.alignItems = Align.Center;
            footer.style.flexShrink = 0;
            footer.style.paddingLeft = 24;
            footer.style.paddingRight = 24;
            footer.style.paddingTop = 12;
            footer.style.paddingBottom = 12;
            footer.style.backgroundColor = ParseColor(RiqMenuStyles.Cream);
            footer.style.borderTopWidth = 3;
            footer.style.borderTopColor = ParseColor(RiqMenuStyles.GrayLighter);

            // Row 1: Enter, W/S, A/D, Tab
            var row1Keys = new string[] { "Enter", "W/S", "A/D", "Tab" };
            var row1Actions = new string[] { "Play", "Navigate", "Switch Tab", "Search" };

            // Row 2: E, P, M, Esc
            var row2Keys = new string[] { "E", "P", "M", "Esc" };
            var row2Actions = new string[] { "Edit", "Auto-Play", "Mute", "Exit" };

            var row1 = CreateHelpRow(row1Keys, row1Actions);
            row1.style.marginBottom = 8;
            footer.Add(row1);

            var row2 = CreateHelpRow(row2Keys, row2Actions);
            footer.Add(row2);

            return footer;
        }

        private VisualElement CreateHelpRow(string[] keys, string[] actions) {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;

            for (int i = 0; i < keys.Length; i++) {
                var keyLabel = new Label(keys[i]);
                keyLabel.style.paddingLeft = 10;
                keyLabel.style.paddingRight = 10;
                keyLabel.style.paddingTop = 5;
                keyLabel.style.paddingBottom = 5;
                keyLabel.style.backgroundColor = ParseColor(RiqMenuStyles.GrayLighter);
                keyLabel.style.borderTopLeftRadius = 6;
                keyLabel.style.borderTopRightRadius = 6;
                keyLabel.style.borderBottomLeftRadius = 6;
                keyLabel.style.borderBottomRightRadius = 6;
                keyLabel.style.borderTopWidth = 2;
                keyLabel.style.borderBottomWidth = 2;
                keyLabel.style.borderLeftWidth = 2;
                keyLabel.style.borderRightWidth = 2;
                keyLabel.style.borderTopColor = ParseColor(RiqMenuStyles.GrayLight);
                keyLabel.style.borderBottomColor = ParseColor(RiqMenuStyles.GrayLight);
                keyLabel.style.borderLeftColor = ParseColor(RiqMenuStyles.GrayLight);
                keyLabel.style.borderRightColor = ParseColor(RiqMenuStyles.GrayLight);
                keyLabel.style.fontSize = 11;
                keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                keyLabel.style.color = ParseColor(RiqMenuStyles.Charcoal);
                row.Add(keyLabel);

                var actionLabel = new Label(actions[i]);
                actionLabel.style.fontSize = 12;
                actionLabel.style.color = ParseColor(RiqMenuStyles.Gray);
                actionLabel.style.marginLeft = 8;
                actionLabel.style.marginRight = 32;
                row.Add(actionLabel);
            }

            return row;
        }

        private VisualElement CreateSongItem(string title, string creator, string fileType, int? bpm, int? downloads, bool selected) {
            var item = new VisualElement();
            item.style.flexDirection = FlexDirection.Column;
            item.style.paddingLeft = 20;
            item.style.paddingRight = 20;
            item.style.paddingTop = 14;
            item.style.paddingBottom = 14;
            item.style.marginBottom = 10;
            item.style.borderTopLeftRadius = 16;
            item.style.borderTopRightRadius = 16;
            item.style.borderBottomLeftRadius = 16;
            item.style.borderBottomRightRadius = 16;
            item.style.borderTopWidth = 3;
            item.style.borderBottomWidth = 3;
            item.style.borderLeftWidth = 3;
            item.style.borderRightWidth = 3;

            if (selected) {
                item.style.backgroundColor = ParseColor("#E0F7FF"); // Lighter cyan for selection
                item.style.borderTopColor = ParseColor(RiqMenuStyles.Cyan);
                item.style.borderBottomColor = ParseColor(RiqMenuStyles.Cyan);
                item.style.borderLeftColor = ParseColor(RiqMenuStyles.Cyan);
                item.style.borderRightColor = ParseColor(RiqMenuStyles.Cyan);
            }
            else {
                item.style.backgroundColor = Color.white;
                item.style.borderTopColor = ParseColor(RiqMenuStyles.GrayLighter);
                item.style.borderBottomColor = ParseColor(RiqMenuStyles.GrayLighter);
                item.style.borderLeftColor = ParseColor(RiqMenuStyles.GrayLighter);
                item.style.borderRightColor = ParseColor(RiqMenuStyles.GrayLighter);
            }

            // Header row
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 16;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = ParseColor(RiqMenuStyles.Charcoal);
            titleLabel.style.flexGrow = 1;
            header.Add(titleLabel);

            // File type badge
            var badge = CreateBadge(fileType.ToUpper(), fileType.ToLower() == "bop" ? "bop" : "riq");
            header.Add(badge);

            item.Add(header);

            // Meta row
            var meta = new VisualElement();
            meta.style.flexDirection = FlexDirection.Row;
            meta.style.alignItems = Align.Center;

            var creatorLabel = new Label($"by {creator}");
            creatorLabel.style.fontSize = 13;
            creatorLabel.style.color = ParseColor(RiqMenuStyles.Gray);
            creatorLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            meta.Add(creatorLabel);

            if (bpm.HasValue) {
                var bpmBadge = CreateBadge($"{bpm} BPM", "bpm");
                meta.Add(bpmBadge);
            }

            // Only show downloads if it has a value (for online songs)
            if (downloads.HasValue) {
                var dlBadge = CreateBadge($"{downloads} DLs", "downloads");
                meta.Add(dlBadge);
            }

            item.Add(meta);

            return item;
        }

        private VisualElement CreateBadge(string text, string type) {
            var badge = new Label(text);
            badge.style.paddingLeft = 10;
            badge.style.paddingRight = 10;
            badge.style.paddingTop = 4;
            badge.style.paddingBottom = 4;
            badge.style.borderTopLeftRadius = 50;
            badge.style.borderTopRightRadius = 50;
            badge.style.borderBottomLeftRadius = 50;
            badge.style.borderBottomRightRadius = 50;
            badge.style.fontSize = 10;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.marginLeft = 8;
            badge.style.borderTopWidth = 2;
            badge.style.borderBottomWidth = 2;
            badge.style.borderLeftWidth = 2;
            badge.style.borderRightWidth = 2;

            switch (type) {
                case "riq":
                    badge.style.backgroundColor = ParseColor(RiqMenuStyles.CyanLight);
                    badge.style.color = ParseColor(RiqMenuStyles.CyanDark);
                    badge.style.borderTopColor = ParseColor(RiqMenuStyles.Cyan);
                    badge.style.borderBottomColor = ParseColor(RiqMenuStyles.Cyan);
                    badge.style.borderLeftColor = ParseColor(RiqMenuStyles.Cyan);
                    badge.style.borderRightColor = ParseColor(RiqMenuStyles.Cyan);
                    break;
                case "bop":
                    badge.style.backgroundColor = ParseColor(RiqMenuStyles.YellowLight);
                    badge.style.color = ParseColor("#B8860B");
                    badge.style.borderTopColor = ParseColor(RiqMenuStyles.Yellow);
                    badge.style.borderBottomColor = ParseColor(RiqMenuStyles.Yellow);
                    badge.style.borderLeftColor = ParseColor(RiqMenuStyles.Yellow);
                    badge.style.borderRightColor = ParseColor(RiqMenuStyles.Yellow);
                    break;
                case "bpm":
                    badge.style.backgroundColor = ParseColor(RiqMenuStyles.Lavender);
                    badge.style.color = ParseColor(RiqMenuStyles.Purple);
                    badge.style.borderTopColor = ParseColor(RiqMenuStyles.Purple);
                    badge.style.borderBottomColor = ParseColor(RiqMenuStyles.Purple);
                    badge.style.borderLeftColor = ParseColor(RiqMenuStyles.Purple);
                    badge.style.borderRightColor = ParseColor(RiqMenuStyles.Purple);
                    break;
                case "downloads":
                    badge.style.backgroundColor = ParseColor(RiqMenuStyles.SoftPeach);
                    badge.style.color = ParseColor(RiqMenuStyles.Coral);
                    badge.style.borderTopColor = ParseColor(RiqMenuStyles.CoralLight);
                    badge.style.borderBottomColor = ParseColor(RiqMenuStyles.CoralLight);
                    badge.style.borderLeftColor = ParseColor(RiqMenuStyles.CoralLight);
                    badge.style.borderRightColor = ParseColor(RiqMenuStyles.CoralLight);
                    break;
            }

            return badge;
        }

        private void SwitchTab(OverlayTab tab) {
            if (_currentTab == tab) return;

            // Stop audio preview when switching tabs
            StopPreview();

            _currentTab = tab;
            ApplyTabStyle(_localTab, tab == OverlayTab.Local);
            ApplyTabStyle(_onlineTab, tab == OverlayTab.Online);

            _localContent.style.display = tab == OverlayTab.Local ? DisplayStyle.Flex : DisplayStyle.None;
            _onlineContent.style.display = tab == OverlayTab.Online ? DisplayStyle.Flex : DisplayStyle.None;

            if (tab == OverlayTab.Online && _onlineSongs.Count == 0) {
                LoadOnlineSongs();
            }
            else if (tab == OverlayTab.Local) {
                // Reload songs from disk (may have new downloads)
                var songManager = RiqMenuSystemManager.Instance?.SongManager;
                songManager?.ReloadSongs();
                RefreshLocalSongList();

                // Start preview for current selection when returning to Local
                TryPreviewCurrentSong();
            }
        }

        private void LoadOnlineSongs() {
            _isLoading = true;
            _currentPage = 1;
            _hasMorePages = true;
            _currentSearchQuery = null;
            ShowStatus("Loading songs...", "loading");

            _apiClient.GetSongs(_onlineSort, 1, (songs, error) => {
                _isLoading = false;

                if (error != null) {
                    ShowStatus($"Error: {error}", "error");
                    return;
                }

                _onlineSongs = songs ?? new List<OnlineSong>();
                _hasMorePages = songs != null && songs.Count >= 20; // Assume more if we got full page
                _selectedOnlineIndex = 0;
                HideStatus();
                RefreshOnlineSongList();
            });
        }

        private void LoadMoreOnlineSongs() {
            if (_isLoading || !_hasMorePages) return;

            _isLoading = true;
            _currentPage++;

            RiqsApiClient.SongsCallback callback = (songs, error) => {
                _isLoading = false;

                if (error != null) {
                    _currentPage--; // Revert page increment on error
                    return;
                }

                if (songs == null || songs.Count == 0) {
                    _hasMorePages = false;
                    return;
                }

                _hasMorePages = songs.Count >= 20;

                // Append new songs
                int startIndex = _onlineSongs.Count;
                _onlineSongs.AddRange(songs);

                // Add new song elements to the list
                for (int i = 0; i < songs.Count; i++) {
                    var song = songs[i];
                    int songIndex = startIndex + i;
                    var item = CreateSongItem(
                        song.DisplayTitle,
                        song.Creator ?? song.UploaderName ?? "Unknown",
                        song.FileType ?? "riq",
                        song.Bpm.HasValue ? (int?)Mathf.RoundToInt(song.Bpm.Value) : null,
                        song.DownloadCount,
                        false
                    );

                    int index = songIndex;
                    item.RegisterCallback<ClickEvent>(evt => SelectOnlineSong(index));

                    _onlineSongList.Add(item);
                    _onlineSongElements.Add(item);
                }
            };

            if (!string.IsNullOrEmpty(_currentSearchQuery)) {
                // For search, we'd need paginated search - for now just don't load more
                _hasMorePages = false;
                _isLoading = false;
            }
            else {
                _apiClient.GetSongs(_onlineSort, _currentPage, callback);
            }
        }

        private void RefreshOnlineSongList() {
            _onlineSongList.Clear();
            _onlineSongElements.Clear();

            for (int i = 0; i < _onlineSongs.Count; i++) {
                var song = _onlineSongs[i];
                var item = CreateSongItem(
                    song.DisplayTitle,
                    song.Creator ?? song.UploaderName ?? "Unknown",
                    song.FileType ?? "riq",
                    song.Bpm.HasValue ? (int?)Mathf.RoundToInt(song.Bpm.Value) : null,
                    song.DownloadCount, // Show downloads for online songs
                    i == _selectedOnlineIndex
                );

                int index = i;
                item.RegisterCallback<ClickEvent>(evt => SelectOnlineSong(index));

                _onlineSongList.Add(item);
                _onlineSongElements.Add(item);
            }
        }

        public void RefreshLocalSongList() {
            _localSongList.Clear();
            _localSongElements.Clear();
            _filteredLocalIndices.Clear();

            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            // Build filtered list based on search query
            var scoredSongs = new List<(int index, int score)>();
            for (int i = 0; i < songManager.TotalSongs; i++) {
                var song = songManager.GetSong(i);
                if (song == null) continue;

                if (string.IsNullOrEmpty(_localSearchQuery)) {
                    scoredSongs.Add((i, 0));
                }
                else {
                    int titleScore = CalculateFuzzyScore(song.SongTitle ?? "", _localSearchQuery);
                    int creatorScore = CalculateFuzzyScore(song.Creator ?? "", _localSearchQuery);
                    int bestScore = Mathf.Max(titleScore, creatorScore);
                    if (bestScore > 0) {
                        scoredSongs.Add((i, bestScore));
                    }
                }
            }

            // Sort by score if searching
            if (!string.IsNullOrEmpty(_localSearchQuery)) {
                scoredSongs.Sort((a, b) => b.score.CompareTo(a.score));
            }

            // Update count label
            var countLabel = _localContent.Q<Label>("local-count");
            if (countLabel != null) {
                countLabel.text = $"{scoredSongs.Count} songs";
            }

            // Create UI elements for filtered songs
            for (int displayIndex = 0; displayIndex < scoredSongs.Count; displayIndex++) {
                int actualIndex = scoredSongs[displayIndex].index;
                _filteredLocalIndices.Add(actualIndex);

                var song = songManager.GetSong(actualIndex);
                if (song == null) continue;

                string fileType = song.riq.EndsWith(".bop", StringComparison.OrdinalIgnoreCase) ? "BOP" : "RIQ";
                string creator = !string.IsNullOrEmpty(song.Creator) ? song.Creator : "Unknown";
                var item = CreateSongItem(
                    song.SongTitle,
                    creator,
                    fileType,
                    song.Bpm.HasValue ? (int?)Mathf.RoundToInt(song.Bpm.Value) : null,
                    song.DownloadCount,
                    displayIndex == _selectedLocalIndex
                );

                int index = displayIndex;
                item.RegisterCallback<ClickEvent>(evt => SelectLocalSong(index));

                _localSongList.Add(item);
                _localSongElements.Add(item);
            }

            // Reset selection if out of bounds
            if (_selectedLocalIndex >= _localSongElements.Count) {
                _selectedLocalIndex = Mathf.Max(0, _localSongElements.Count - 1);
            }
        }

        private int CalculateFuzzyScore(string text, string query) {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return 0;

            string lowerText = text.ToLower();
            string lowerQuery = query.ToLower();

            // Exact substring match gets highest score
            if (lowerText.Contains(lowerQuery))
                return 1000 + (lowerQuery.Length * 10);

            // Fuzzy matching
            int score = 0, textIndex = 0, queryIndex = 0, consecutiveMatches = 0;

            while (textIndex < lowerText.Length && queryIndex < lowerQuery.Length) {
                if (lowerText[textIndex] == lowerQuery[queryIndex]) {
                    score += 10 + consecutiveMatches;
                    consecutiveMatches++;
                    queryIndex++;
                }
                else {
                    consecutiveMatches = 0;
                }
                textIndex++;
            }

            // Bonus for completing the query
            if (queryIndex == lowerQuery.Length) score += 50;

            return score;
        }

        private void SelectLocalSong(int index) {
            if (index == _selectedLocalIndex) return;

            // Update visual state
            if (_selectedLocalIndex < _localSongElements.Count) {
                ApplySongItemStyle(_localSongElements[_selectedLocalIndex], false);
            }

            _selectedLocalIndex = index;

            if (_selectedLocalIndex < _localSongElements.Count) {
                ApplySongItemStyle(_localSongElements[_selectedLocalIndex], true);
            }

            // Play audio preview
            TryPreviewCurrentSong();
        }

        private void TryPreviewCurrentSong() {
            if (_currentTab != OverlayTab.Local) return;
            if (_selectedLocalIndex == _lastPreviewedSong) return;

            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;

            if (songManager == null || audioManager == null) return;

            // Get actual song index from filtered list
            int actualIndex = _filteredLocalIndices.Count > 0 && _selectedLocalIndex < _filteredLocalIndices.Count
                ? _filteredLocalIndices[_selectedLocalIndex]
                : _selectedLocalIndex;

            var song = songManager.GetSong(actualIndex);
            if (song == null) return;

            _lastPreviewedSong = _selectedLocalIndex;

            if (audioManager.IsPreviewPlaying) {
                audioManager.StopPreview();
            }

            _isLoadingAudio = true;
            _loadingStartTime = Time.time;
            audioManager.PlayPreview(song);
        }

        private void StopPreview() {
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            audioManager?.StopPreview();
            _lastPreviewedSong = -1;
        }

        private void SelectOnlineSong(int index) {
            // Bounds check - don't select if list is empty or index invalid
            if (_onlineSongElements.Count == 0) return;
            if (index < 0 || index >= _onlineSongElements.Count) return;
            if (index == _selectedOnlineIndex) return;

            if (_selectedOnlineIndex >= 0 && _selectedOnlineIndex < _onlineSongElements.Count) {
                ApplySongItemStyle(_onlineSongElements[_selectedOnlineIndex], false);
            }

            _selectedOnlineIndex = index;

            if (_selectedOnlineIndex >= 0 && _selectedOnlineIndex < _onlineSongElements.Count) {
                ApplySongItemStyle(_onlineSongElements[_selectedOnlineIndex], true);
            }
        }

        private void ApplySongItemStyle(VisualElement item, bool selected) {
            if (selected) {
                item.style.backgroundColor = ParseColor("#E0F7FF"); // Lighter cyan for selection
                item.style.borderTopColor = ParseColor(RiqMenuStyles.Cyan);
                item.style.borderBottomColor = ParseColor(RiqMenuStyles.Cyan);
                item.style.borderLeftColor = ParseColor(RiqMenuStyles.Cyan);
                item.style.borderRightColor = ParseColor(RiqMenuStyles.Cyan);
            }
            else {
                item.style.backgroundColor = Color.white;
                item.style.borderTopColor = ParseColor(RiqMenuStyles.GrayLighter);
                item.style.borderBottomColor = ParseColor(RiqMenuStyles.GrayLighter);
                item.style.borderLeftColor = ParseColor(RiqMenuStyles.GrayLighter);
                item.style.borderRightColor = ParseColor(RiqMenuStyles.GrayLighter);
            }
        }

        private void ShowStatus(string message, string type) {
            _statusContainer.Clear();
            _statusContainer.style.display = DisplayStyle.Flex;

            var status = new Label(message);
            status.style.paddingLeft = 16;
            status.style.paddingRight = 16;
            status.style.paddingTop = 12;
            status.style.paddingBottom = 12;
            status.style.borderTopLeftRadius = 12;
            status.style.borderTopRightRadius = 12;
            status.style.borderBottomLeftRadius = 12;
            status.style.borderBottomRightRadius = 12;
            status.style.marginBottom = 12;
            status.style.fontSize = 13;
            status.style.unityFontStyleAndWeight = FontStyle.Bold;

            switch (type) {
                case "loading":
                    status.style.backgroundColor = ParseColor(RiqMenuStyles.CyanLight);
                    status.style.color = ParseColor(RiqMenuStyles.CyanDark);
                    break;
                case "success":
                    status.style.backgroundColor = ParseColor(RiqMenuStyles.Mint);
                    status.style.color = ParseColor(RiqMenuStyles.Green);
                    break;
                case "error":
                    status.style.backgroundColor = ParseColor(RiqMenuStyles.CoralLight);
                    status.style.color = ParseColor(RiqMenuStyles.Coral);
                    break;
            }

            _statusContainer.Add(status);
        }

        private void HideStatus() {
            _statusContainer.style.display = DisplayStyle.None;
        }

        private void Update() {
            if (!_isVisible) return;

            // Update audio status display
            UpdateAudioStatus();

            // Handle online search debounce
            if (_onlineSearchDebounceTime > 0) {
                _onlineSearchDebounceTime -= Time.deltaTime;
                if (_onlineSearchDebounceTime <= 0 && !string.IsNullOrEmpty(_pendingOnlineSearch)) {
                    ExecuteOnlineSearch(_pendingOnlineSearch);
                    _pendingOnlineSearch = null;
                }
            }

            // Ignore input briefly after opening
            if (_inputDelayTimer > 0) {
                _inputDelayTimer -= Time.deltaTime;
                return;
            }

            HandleInput();
        }

        private void ExecuteOnlineSearch(string query) {
            _isLoading = true;
            _currentSearchQuery = query;
            _currentPage = 1;
            _hasMorePages = false; // Search doesn't support pagination yet
            ShowStatus($"Searching for \"{query}\"...", "loading");

            _apiClient.SearchSongs(query, (songs, error) => {
                _isLoading = false;

                if (error != null) {
                    ShowStatus($"Error: {error}", "error");
                    return;
                }

                _onlineSongs = songs ?? new List<OnlineSong>();
                _selectedOnlineIndex = 0;
                HideStatus();
                RefreshOnlineSongList();
            });
        }

        private void HandleInput() {
            // Skip input for one frame after editor closes to prevent Enter from playing song
            if (_editorJustClosed) {
                _editorJustClosed = false;
                return;
            }

            // Handle editor modal input separately
            if (_isEditorOpen) {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) {
                    CloseEditor(false);
                }
                else if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) {
                    CloseEditor(true);
                }
                return; // Don't process other input while editor is open
            }

            // Escape always works - exits search mode or closes overlay
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) {
                if (_isSearchMode) {
                    ExitSearchMode();
                    return;
                }
                Hide();
                return;
            }

            // If in search mode, only allow Escape, Enter, or Tab to exit
            if (_isSearchMode) {
                // Enter or Tab while searching exits search mode and keeps results
                if (UnityEngine.Input.GetKeyDown(KeyCode.Return) ||
                    UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter) ||
                    UnityEngine.Input.GetKeyDown(KeyCode.Tab)) {
                    ExitSearchMode();
                }
                return; // Skip all other input while typing
            }

            // Tab to enter search mode
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab)) {
                EnterSearchMode();
                return;
            }

            // E to edit metadata (Local tab only)
            if (UnityEngine.Input.GetKeyDown(KeyCode.E) && _currentTab == OverlayTab.Local) {
                OpenEditor();
                return;
            }

            // Tab switching with arrows or A/D
            if (UnityEngine.Input.GetKeyDown(KeyCode.LeftArrow) || UnityEngine.Input.GetKeyDown(KeyCode.A)) {
                SwitchTab(OverlayTab.Local);
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.RightArrow) || UnityEngine.Input.GetKeyDown(KeyCode.D)) {
                SwitchTab(OverlayTab.Online);
            }

            // Autoplay toggle (P key) - only in Local tab
            if (UnityEngine.Input.GetKeyDown(KeyCode.P) && _currentTab == OverlayTab.Local) {
                MixtapeLoaderCustom.autoplay = !MixtapeLoaderCustom.autoplay;
                UpdateAutoplayLabel();
            }

            // Mute toggle (M key)
            if (UnityEngine.Input.GetKeyDown(KeyCode.M)) {
                var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
                audioManager?.ToggleMute();
            }

            // Navigation
            if (UnityEngine.Input.GetKeyDown(KeyCode.UpArrow) || UnityEngine.Input.GetKeyDown(KeyCode.W)) {
                if (_currentTab == OverlayTab.Local) {
                    SelectLocalSong(Mathf.Max(0, _selectedLocalIndex - 1));
                    ScrollToSelectedLocal();
                }
                else {
                    SelectOnlineSong(Mathf.Max(0, _selectedOnlineIndex - 1));
                    ScrollToSelectedOnline();
                }
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.DownArrow) || UnityEngine.Input.GetKeyDown(KeyCode.S)) {
                if (_currentTab == OverlayTab.Local) {
                    SelectLocalSong(Mathf.Min(_localSongElements.Count - 1, _selectedLocalIndex + 1));
                    ScrollToSelectedLocal();
                }
                else {
                    SelectOnlineSong(Mathf.Min(_onlineSongElements.Count - 1, _selectedOnlineIndex + 1));
                    ScrollToSelectedOnline();

                    // Load more when near the bottom (within 5 items)
                    if (_selectedOnlineIndex >= _onlineSongElements.Count - 5) {
                        LoadMoreOnlineSongs();
                    }
                }
            }
            // Page Up/Down navigation
            else if (UnityEngine.Input.GetKeyDown(KeyCode.PageUp)) {
                if (_currentTab == OverlayTab.Local) {
                    SelectLocalSong(Mathf.Max(0, _selectedLocalIndex - PAGE_SIZE));
                    ScrollToSelectedLocal();
                }
                else {
                    SelectOnlineSong(Mathf.Max(0, _selectedOnlineIndex - PAGE_SIZE));
                    ScrollToSelectedOnline();
                }
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.PageDown)) {
                if (_currentTab == OverlayTab.Local) {
                    SelectLocalSong(Mathf.Min(_localSongElements.Count - 1, _selectedLocalIndex + PAGE_SIZE));
                    ScrollToSelectedLocal();
                }
                else {
                    SelectOnlineSong(Mathf.Min(_onlineSongElements.Count - 1, _selectedOnlineIndex + PAGE_SIZE));
                    ScrollToSelectedOnline();

                    // Load more when near the bottom
                    if (_selectedOnlineIndex >= _onlineSongElements.Count - 5) {
                        LoadMoreOnlineSongs();
                    }
                }
            }

            // Selection
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) {
                if (_currentTab == OverlayTab.Local) {
                    // Get actual song index from filtered list
                    int actualIndex = _filteredLocalIndices.Count > 0 && _selectedLocalIndex < _filteredLocalIndices.Count
                        ? _filteredLocalIndices[_selectedLocalIndex]
                        : _selectedLocalIndex;
                    OnSongSelected?.Invoke(actualIndex);
                    Hide();
                }
                else {
                    // Download selected online song
                    DownloadSelectedSong();
                }
            }

        }

        private void EnterSearchMode() {
            _isSearchMode = true;

            TextField field = _currentTab == OverlayTab.Local ? _localSearchField : _onlineSearchField;
            if (field == null) return;

            // Enable focusable so field can receive input
            field.focusable = true;

            // Clear placeholder text
            string placeholder = _currentTab == OverlayTab.Local ? "Search local songs..." : "Search online songs...";
            if (field.value == placeholder) {
                field.value = "";
                var input = field.Q<VisualElement>("unity-text-input");
                if (input != null) input.style.color = ParseColor(RiqMenuStyles.Charcoal);
            }

            // Schedule focus for next frame to ensure layout is complete
            field.schedule.Execute(() => {
                field.Focus();
                field.SelectAll(); // Triggers edit mode
            });
        }

        private void ExitSearchMode() {
            _isSearchMode = false;

            // Blur BOTH search fields and disable focusable to prevent accidental focus
            if (_localSearchField != null) {
                _localSearchField.Blur();
                _localSearchField.focusable = false;
                // Reset placeholder if empty
                if (string.IsNullOrEmpty(_localSearchField.value)) {
                    _localSearchField.value = "Search local songs...";
                    var input = _localSearchField.Q<VisualElement>("unity-text-input");
                    if (input != null) input.style.color = ParseColor(RiqMenuStyles.GrayLight);
                }
            }

            if (_onlineSearchField != null) {
                _onlineSearchField.Blur();
                _onlineSearchField.focusable = false;
                // Reset placeholder if empty
                if (string.IsNullOrEmpty(_onlineSearchField.value)) {
                    _onlineSearchField.value = "Search online songs...";
                    var input = _onlineSearchField.Q<VisualElement>("unity-text-input");
                    if (input != null) input.style.color = ParseColor(RiqMenuStyles.GrayLight);
                }
            }

            // Focus overlay to take focus away from any field
            _overlay?.Focus();
        }

        private void UpdateAutoplayLabel() {
            if (_autoplayLabel != null) {
                _autoplayLabel.text = $"Autoplay: {(MixtapeLoaderCustom.autoplay ? "ON" : "OFF")} (P)";
                _autoplayLabel.style.color = ParseColor(MixtapeLoaderCustom.autoplay ? RiqMenuStyles.Green : RiqMenuStyles.Coral);
            }
        }

        private void UpdateAudioStatus() {
            if (_audioStatusLabel == null) return;

            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            if (audioManager == null) return;

            if (_isLoadingAudio) {
                if (audioManager.IsPreviewPlaying) {
                    _isLoadingAudio = false;
                    var song = audioManager.CurrentPreviewSong;
                    string title = song?.SongTitle ?? "";
                    if (title.Length > 40) title = title.Substring(0, 37) + "...";
                    string muteIndicator = audioManager.IsMuted ? " (Muted)" : "";
                    _audioStatusLabel.text = $"Now Playing: {title}{muteIndicator}";
                    _audioStatusLabel.style.color = ParseColor(audioManager.IsMuted ? RiqMenuStyles.GrayLight : RiqMenuStyles.Yellow);
                }
                else if (Time.time - _loadingStartTime > 0.5f) {
                    _audioStatusLabel.text = "Loading audio...";
                    _audioStatusLabel.style.color = ParseColor(RiqMenuStyles.CyanLight);
                }
            }
            else if (audioManager.IsPreviewPlaying) {
                // Update mute state for already playing audio
                var song = audioManager.CurrentPreviewSong;
                string title = song?.SongTitle ?? "";
                if (title.Length > 40) title = title.Substring(0, 37) + "...";
                string muteIndicator = audioManager.IsMuted ? " (Muted)" : "";
                _audioStatusLabel.text = $"Now Playing: {title}{muteIndicator}";
                _audioStatusLabel.style.color = ParseColor(audioManager.IsMuted ? RiqMenuStyles.GrayLight : RiqMenuStyles.Yellow);
            }
            else {
                _audioStatusLabel.text = "";
            }
        }

        private void ScrollToSelectedLocal() {
            if (_selectedLocalIndex >= 0 && _selectedLocalIndex < _localSongElements.Count) {
                _localSongList.ScrollTo(_localSongElements[_selectedLocalIndex]);
            }
        }

        private void ScrollToSelectedOnline() {
            if (_selectedOnlineIndex >= 0 && _selectedOnlineIndex < _onlineSongElements.Count) {
                _onlineSongList.ScrollTo(_onlineSongElements[_selectedOnlineIndex]);
            }
        }

        private void DownloadSelectedSong() {
            if (_selectedOnlineIndex >= _onlineSongs.Count) return;

            var song = _onlineSongs[_selectedOnlineIndex];
            string songsFolder = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "RiqMenu");

            ShowStatus($"Downloading {song.Title}...", "loading");

            _apiClient.DownloadSong(song, songsFolder,
                (filePath, error) => {
                    if (error != null) {
                        ShowStatus($"Error: {error}", "error");
                    }
                    else {
                        ShowStatus("Downloaded! Switch to Local to play.", "success");
                        var songManager = RiqMenuSystemManager.Instance?.SongManager;
                        songManager?.ReloadSongs();
                    }
                },
                null
            );
        }

        public void Show() {
            if (_isVisible) return;

            _isVisible = true;
            _inputDelayTimer = INPUT_DELAY; // Ignore input briefly after opening
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.Focus(); // Focus to receive keyboard events

            // Reset state
            _lastPreviewedSong = -1;
            _localSearchQuery = "";
            _selectedLocalIndex = 0;
            _isSearchMode = false;

            // Reset search field
            if (_localSearchField != null) {
                _localSearchField.value = "Search local songs...";
                var inp = _localSearchField.Q<VisualElement>("unity-text-input");
                if (inp != null) inp.style.color = ParseColor(RiqMenuStyles.GrayLight);
            }

            RefreshLocalSongList();
            UpdateAutoplayLabel();

            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.BlockInput();

            OnOverlayOpened?.Invoke();

            // Start preview for currently selected song
            TryPreviewCurrentSong();
        }

        public void Hide() {
            if (!_isVisible) return;

            _isVisible = false;
            _overlay.style.display = DisplayStyle.None;

            // Stop audio preview
            StopPreview();

            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.UnblockInput();

            OnOverlayClosed?.Invoke();
        }

        public void Toggle() {
            if (_isVisible) Hide();
            else Show();
        }

        private Color ParseColor(string hex) {
            if (ColorUtility.TryParseHtmlString(hex, out Color color)) {
                return color;
            }
            return Color.white;
        }
    }
}
