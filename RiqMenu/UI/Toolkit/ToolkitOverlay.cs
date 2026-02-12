using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using RiqMenu.Core;
using RiqMenu.Input;
using RiqMenu.Online;

namespace RiqMenu.UI.Toolkit {
    public enum OverlayTab {
        Local,
        Online,
        Settings
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

        // Tab state - single source of truth with static accessor
        private OverlayTab _currentTab = OverlayTab.Local;
        public static OverlayTab CurrentTabStatic { get; private set; } = OverlayTab.Local;

        // Frame delay after tab switch to prevent same-frame input processing
        private bool _tabJustSwitched = false;
        private Button _localTab;
        private Button _onlineTab;
        private Button _settingsTab;
        private VisualElement _localContent;
        private VisualElement _onlineContent;
        private VisualElement _settingsContent;

        // Settings tab state
        private Button _accuracyBarToggle;
        private bool _accuracyBarValue;
        private Button _autoRestartToggle;
        private AutoRestartMode _autoRestartValue;
        private Button _progressBarToggle;
        private bool _progressBarValue;
        private int _selectedSettingIndex = 0;
        private List<VisualElement> _settingsRows = new List<VisualElement>();

        // Local songs
        private ScrollView _localSongList;
        private VisualElement _localGridWrapper;
        private TextField _localSearchField;
        private int _selectedLocalIndex = 0;
        private List<VisualElement> _localSongElements = new List<VisualElement>();
        private List<int> _filteredLocalIndices = new List<int>();
        private string _localSearchQuery = "";
        private Label _audioStatusLabel;
        private Label _autoplayLabel;
        private Label _tabsHintLabel;
        private Label _settingsHintLabel;
        private Label _editorHintLabel;
        private VisualElement _footerHintsContainer;
        private VisualElement _touchActionBar;
        private Dictionary<RiqInputAction, System.Action> _touchActionHandlers;
        private RiqInputManager _inputManager;
        private RiqInputMethod _activeInputMethod = RiqInputMethod.KeyboardMouse;
        private const int GRID_COLUMNS = 4;
        private const int PAGE_SIZE = 20;

        private enum FooterKeyBindingType {
            Action,
            CombinedActions
        }

        private readonly struct FooterHintEntry(
            FooterKeyBindingType keyBindingType,
            string label,
            RiqInputAction primaryAction = default,
            RiqInputAction secondaryAction = default) {
            public FooterKeyBindingType KeyBindingType { get; } = keyBindingType;
            public string Label { get; } = label;
            public RiqInputAction PrimaryAction { get; } = primaryAction;
            public RiqInputAction SecondaryAction { get; } = secondaryAction;

            public static FooterHintEntry Action(RiqInputAction action, string label) {
                return new FooterHintEntry(FooterKeyBindingType.Action, label, primaryAction: action);
            }

            public static FooterHintEntry Combined(RiqInputAction firstAction, RiqInputAction secondAction, string label) {
                return new FooterHintEntry(FooterKeyBindingType.CombinedActions, label, primaryAction: firstAction, secondaryAction: secondAction);
            }
        }

        private static readonly Dictionary<OverlayTab, FooterHintEntry[][]> FooterHintRowsByTab = new() {
            [OverlayTab.Local] = [
                [
                    FooterHintEntry.Action(RiqInputAction.Submit, "Play"),
                    FooterHintEntry.Action(RiqInputAction.NavigateUp, "Navigate"),
                    FooterHintEntry.Combined(RiqInputAction.PreviousTab, RiqInputAction.NextTab, "Switch Tab"),
                    FooterHintEntry.Action(RiqInputAction.Search, "Search")
                ],
                [
                    FooterHintEntry.Action(RiqInputAction.Edit, "Edit"),
                    FooterHintEntry.Action(RiqInputAction.ToggleAutoplay, "Auto-Play"),
                    FooterHintEntry.Action(RiqInputAction.ToggleMute, "Mute"),
                    FooterHintEntry.Action(RiqInputAction.Cancel, "Exit")
                ]
            ],
            [OverlayTab.Online] = [
                [
                    FooterHintEntry.Action(RiqInputAction.Submit, "Download"),
                    FooterHintEntry.Action(RiqInputAction.NavigateUp, "Navigate"),
                    FooterHintEntry.Combined(RiqInputAction.PreviousTab, RiqInputAction.NextTab, "Switch Tab"),
                    FooterHintEntry.Action(RiqInputAction.Search, "Search"),
                    FooterHintEntry.Action(RiqInputAction.Cancel, "Exit")
                ]
            ],
            [OverlayTab.Settings] = [
                [
                    FooterHintEntry.Action(RiqInputAction.Submit, "Toggle"),
                    FooterHintEntry.Action(RiqInputAction.NavigateUp, "Navigate"),
                    FooterHintEntry.Combined(RiqInputAction.PreviousTab, RiqInputAction.NextTab, "Switch Tab"),
                    FooterHintEntry.Action(RiqInputAction.Cancel, "Exit")
                ]
            ]
        };

        private static readonly Dictionary<OverlayTab, FooterHintEntry[]> TouchActionEntriesByTab = new() {
            [OverlayTab.Local] = [
                FooterHintEntry.Action(RiqInputAction.Submit, "Play"),
                FooterHintEntry.Action(RiqInputAction.Edit, "Edit"),
                FooterHintEntry.Action(RiqInputAction.ToggleAutoplay, "Autoplay"),
                FooterHintEntry.Action(RiqInputAction.ToggleMute, "Mute"),
                FooterHintEntry.Action(RiqInputAction.Cancel, "Exit")
            ],
            [OverlayTab.Online] = [
                FooterHintEntry.Action(RiqInputAction.Submit, "Download"),
                FooterHintEntry.Action(RiqInputAction.Cancel, "Exit")
            ],
            [OverlayTab.Settings] = [
                FooterHintEntry.Action(RiqInputAction.Cancel, "Exit")
            ]
        };

        // Online songs
        private ScrollView _onlineSongList;
        private VisualElement _onlineGridWrapper;
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
        private HashSet<string> _localFileHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pending UI updates from background threads
        private volatile bool _pendingOnlineRefresh = false;
        private volatile string _pendingOnlineError = null;
        private volatile bool _pendingHideStatus = false;
        private volatile string _pendingStatusMessage = null;
        private volatile string _pendingStatusType = null;
        private volatile string _pendingDownloadHash = null;

        // Status
        private VisualElement _statusContainer;

        // Audio preview
        private int _lastPreviewedSong = -1;
        private bool _isLoadingAudio = false;
        private float _loadingStartTime = 0f;

        public bool IsVisible => _isVisible;
        public event System.Action OnOverlayOpened;
        public event System.Action OnOverlayClosed;
        public event System.Action<int, OverlayTab> OnSongSelected;

        // Delay timer to ignore input briefly after opening
        private float _inputDelayTimer = 0f;
        private const float INPUT_DELAY = 0.15f; // 150ms delay after opening

        // Toggle cooldown to prevent rapid open/close
        private float _lastToggleTime = 0f;
        private const float TOGGLE_COOLDOWN = 0.3f; // 300ms cooldown between toggles

        // Hide cooldown to prevent immediate re-open after playing a song
        private float _lastHideTime = 0f;
        private const float REOPEN_COOLDOWN = 1.0f; // 1 second cooldown after hiding

        // Search mode flag - more reliable than focus detection
        private bool _isSearchMode = false;

        // Metadata editor modal
        private bool _isEditorOpen = false;
        private bool _editorJustClosed = false; // Prevent Enter from playing song after editor closes
        private VisualElement _editorModal;
        private TextField _editorTitleField;
        private TextField _editorArtistField;
        private TextField _editorCreatorField;
        private TextField _editorBpmField;
        private TextField _editorDifficultyField;
        private CustomSong _editingSong;

        private void Awake() {
            InitializeTouchActionHandlers();
            CreateUIDocument();
            TryConnectInputManager();
        }

        private void InitializeTouchActionHandlers() {
            _touchActionHandlers = new Dictionary<RiqInputAction, System.Action> {
                [RiqInputAction.Submit] = ExecuteCurrentTabPrimaryAction,
                [RiqInputAction.Edit] = OpenEditor,
                [RiqInputAction.Search] = EnterSearchMode,
                [RiqInputAction.ToggleAutoplay] = ToggleAutoplay,
                [RiqInputAction.ToggleMute] = ToggleMute,
                [RiqInputAction.Cancel] = Hide
            };
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
            if (_inputManager != null) {
                _inputManager.OnInputMethodChanged -= HandleInputMethodChanged;
                _inputManager = null;
            }

            if (_themeBundle != null) {
                _themeBundle.Unload(true);
                _themeBundle = null;
            }
        }

        private void SetupFont() {
            // Use the game's own font as primary (same look as the default theme),
            // then add CJK fallbacks from game resources in JP → CN → KR order.
            FontAsset primaryFontAsset = null;

            // Find the game's loaded fonts and use one as primary
            string[] primaryCandidates = { "NotInter-Regular", "Arial" };
            var allFonts = Resources.FindObjectsOfTypeAll<Font>();
            foreach (var candidateName in primaryCandidates) {
                var font = allFonts.FirstOrDefault(f => f.name == candidateName);
                if (font == null) continue;
                try {
                    primaryFontAsset = FontAsset.CreateFontAsset(font);
                    if (primaryFontAsset != null) {
                        Debug.Log($"[ToolkitOverlay] Primary font: {font.name}");
                        break;
                    }
                } catch (Exception ex) {
                    Debug.LogWarning($"[ToolkitOverlay] {candidateName} failed: {ex.Message}");
                }
            }

            // CJK fallbacks from game resources — regular weight only, JP → CN → KR
            string[] cjkFontNames = {
                "MPLUSRounded1c-Regular SDF",    // Japanese
                "TaiwanPearl-Regular SDF",       // Chinese
                "Binggrae SDF",                  // Korean
            };

            var fallbacks = new List<FontAsset>();
            foreach (var fontName in cjkFontNames) {
                try {
                    var tmpFont = Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/" + fontName);
                    if (tmpFont?.sourceFontFile == null) {
                        Debug.Log($"[ToolkitOverlay] Skipping {fontName}: {(tmpFont == null ? "not found" : "no sourceFontFile")}");
                        continue;
                    }
                    var fontAsset = FontAsset.CreateFontAsset(tmpFont.sourceFontFile);
                    if (fontAsset == null) continue;
                    Debug.Log($"[ToolkitOverlay] Fallback: {tmpFont.sourceFontFile.name} ({fontName})");
                    fallbacks.Add(fontAsset);
                } catch (Exception ex) {
                    Debug.LogWarning($"[ToolkitOverlay] Failed with {fontName}: {ex.Message}");
                }
            }

            // If no game font worked as primary, promote first CJK font
            if (primaryFontAsset == null && fallbacks.Count > 0) {
                primaryFontAsset = fallbacks[0];
                fallbacks.RemoveAt(0);
            }

            if (primaryFontAsset != null) {
                if (fallbacks.Count > 0) {
                    primaryFontAsset.fallbackFontAssetTable = fallbacks;
                }
                _root.style.unityFontDefinition = FontDefinition.FromSDFFont(primaryFontAsset);
                Debug.Log($"[ToolkitOverlay] Font set with {fallbacks.Count} CJK fallback(s)");
            }
        }

        private void BuildUI() {
            _root = _uiDocument.rootVisualElement;

            // Find a CJK-capable font from game assets (OS fonts don't work with TextCore)
            SetupFont();

            // Create overlay container (hidden by default)
            _overlay = new VisualElement();
            _overlay.name = "riq-overlay";
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.right = 0;
            _overlay.style.top = 0;
            _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new Color(0, 0, 0, 0.85f);
            _overlay.style.alignItems = Align.Stretch;
            _overlay.style.justifyContent = Justify.FlexStart;
            _overlay.style.display = DisplayStyle.None;
            _overlay.focusable = true;
            _overlay.tabIndex = -1; // Disable tab navigation

            // Intercept keys to prevent UI Toolkit's default navigation from interfering
            // But allow them through when in search mode so text fields work
            _overlay.RegisterCallback<KeyDownEvent>(evt => {
                // Don't intercept if we're in search mode (typing in text field)
                if (_isSearchMode) return;

                if (evt.keyCode == KeyCode.Tab) {
                    evt.StopPropagation();
                    evt.PreventDefault();
                }
                // Stop Enter from propagating to other UI elements or game menus
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) {
                    evt.StopPropagation();
                    evt.PreventDefault();
                }
            }, TrickleDown.TrickleDown);

            // Also intercept NavigationSubmit events to prevent them from reaching game menus
            // But allow them through when in search mode
            _overlay.RegisterCallback<NavigationSubmitEvent>(evt => {
                if (_isSearchMode) return;
                evt.StopPropagation();
                evt.PreventDefault();
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

            // Artist field
            var artistLabel = new Label("Artist");
            artistLabel.style.fontSize = 12;
            artistLabel.style.color = ParseColor(RiqMenuStyles.Gray);
            artistLabel.style.marginBottom = 4;
            modalCard.Add(artistLabel);

            _editorArtistField = new TextField();
            _editorArtistField.style.marginBottom = 12;
            ApplyTextFieldStyle(_editorArtistField);
            modalCard.Add(_editorArtistField);

            // Mapper field
            var creatorLabel = new Label("Mapper");
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
            _editorHintLabel = new Label("Press Escape to cancel, Enter to save");
            var hint = _editorHintLabel;
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
            _editorArtistField.value = song.Artist ?? "";
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
                        _editorArtistField.value,
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
            _settingsContent = CreateSettingsContent();
            _onlineContent.style.display = DisplayStyle.None;
            _settingsContent.style.display = DisplayStyle.None;

            contentContainer.Add(_localContent);
            contentContainer.Add(_onlineContent);
            contentContainer.Add(_settingsContent);
            card.Add(contentContainer);

            // Footer
            var footer = CreateFooter();
            card.Add(footer);

            return card;
        }

        private void ApplyCardStyle(VisualElement card) {
            card.style.flexGrow = 1;
            card.style.marginLeft = 60;
            card.style.marginRight = 60;
            card.style.marginTop = 40;
            card.style.marginBottom = 40;
            card.style.backgroundColor = ParseColor(RiqMenuStyles.WarmWhite);
            card.style.borderTopLeftRadius = 16;
            card.style.borderTopRightRadius = 16;
            card.style.borderBottomLeftRadius = 16;
            card.style.borderBottomRightRadius = 16;
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

            _settingsTab = CreateTab("Settings", false);
            _settingsTab.clicked += () => SwitchTab(OverlayTab.Settings);
            container.Add(_settingsTab);

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            container.Add(spacer);

            // Hint with icon style
            _tabsHintLabel = new Label("Q / E to switch tabs");
            var hint = _tabsHintLabel;
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
            tab.focusable = false; // Prevent Enter key from triggering tab click
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
            _localSearchField.focusable = false; // Only focusable via Tab key
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

            // Song grid
            _localSongList = new ScrollView(ScrollViewMode.Vertical);
            _localSongList.style.flexGrow = 1;
            _localSongList.style.flexShrink = 1;
            _localSongList.tabIndex = -1;
            StyleScrollView(_localSongList);

            _localGridWrapper = new VisualElement();
            _localGridWrapper.style.flexDirection = FlexDirection.Row;
            _localGridWrapper.style.flexWrap = Wrap.Wrap;
            _localSongList.Add(_localGridWrapper);

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
            _onlineSearchField.focusable = false; // Only focusable via Tab key
            _onlineSearchField.RegisterValueChangedCallback(evt => {
                string newValue = evt.newValue;
                if (newValue == "Search online songs..." || newValue == "") {
                    _pendingOnlineSearch = null;
                    // If we had a search active, reload default songs
                    if (!string.IsNullOrEmpty(_currentSearchQuery)) {
                        _currentSearchQuery = null;
                        LoadOnlineSongs();
                    }
                }
                else {
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

            // Song grid
            _onlineSongList = new ScrollView(ScrollViewMode.Vertical);
            _onlineSongList.style.flexGrow = 1;
            _onlineSongList.style.flexShrink = 1;
            _onlineSongList.tabIndex = -1;
            StyleScrollView(_onlineSongList);

            _onlineGridWrapper = new VisualElement();
            _onlineGridWrapper.style.flexDirection = FlexDirection.Row;
            _onlineGridWrapper.style.flexWrap = Wrap.Wrap;
            _onlineSongList.Add(_onlineGridWrapper);

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

        private VisualElement CreateSettingsContent() {
            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.paddingLeft = 24;
            container.style.paddingRight = 24;
            container.style.paddingTop = 24;
            container.style.paddingBottom = 24;
            container.style.backgroundColor = ParseColor(RiqMenuStyles.WarmWhite);

            // Header
            var header = new Label("Settings");
            header.style.fontSize = 24;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = ParseColor(RiqMenuStyles.Charcoal);
            header.style.marginBottom = 24;
            container.Add(header);

            // Gameplay section
            var gameplaySection = new Label("Gameplay");
            gameplaySection.style.fontSize = 16;
            gameplaySection.style.unityFontStyleAndWeight = FontStyle.Bold;
            gameplaySection.style.color = ParseColor(RiqMenuStyles.Gray);
            gameplaySection.style.marginBottom = 12;
            container.Add(gameplaySection);

            _settingsRows.Clear();

            // Accuracy Bar toggle
            _accuracyBarValue = RiqMenuSettings.AccuracyBarEnabled;
            var accuracyBarRow = CreateSettingsToggle(
                "Accuracy Bar",
                "Show timing indicator during gameplay",
                _accuracyBarValue,
                (value) => {
                    _accuracyBarValue = value;
                    RiqMenuSettings.AccuracyBarEnabled = value;
                },
                out _accuracyBarToggle
            );
            _settingsRows.Add(accuracyBarRow);
            container.Add(accuracyBarRow);

            // Auto-Restart toggle (cycles through Off/Miss/Non-Perfect)
            _autoRestartValue = RiqMenuSettings.AutoRestartMode;
            var autoRestartRow = CreateSettingsCycleToggle(
                "Auto Restart",
                "Restart song on miss or non-perfect hit",
                GetAutoRestartLabel(_autoRestartValue),
                () => {
                    _autoRestartValue = RiqMenuSettings.CycleAutoRestartMode();
                    UpdateAutoRestartToggle();
                },
                out _autoRestartToggle
            );
            _settingsRows.Add(autoRestartRow);
            container.Add(autoRestartRow);

            // Progress Bar toggle
            _progressBarValue = RiqMenuSettings.ProgressBarEnabled;
            var progressBarRow = CreateSettingsToggle(
                "Progress Bar",
                "Show song progress at top of screen",
                _progressBarValue,
                (value) => {
                    _progressBarValue = value;
                    RiqMenuSettings.ProgressBarEnabled = value;
                },
                out _progressBarToggle
            );
            _settingsRows.Add(progressBarRow);
            container.Add(progressBarRow);

            _accuracyBarToggle.clicked += () => {
                _selectedSettingIndex = 0;
                UpdateSettingsSelection();
            };
            _autoRestartToggle.clicked += () => {
                _selectedSettingIndex = 1;
                UpdateSettingsSelection();
            };
            _progressBarToggle.clicked += () => {
                _selectedSettingIndex = 2;
                UpdateSettingsSelection();
            };

            // Update selection highlight
            UpdateSettingsSelection();

            // Hint text
            _settingsHintLabel = new Label("Use Up/Down to navigate, Enter to toggle");
            var hintText = _settingsHintLabel;
            hintText.style.fontSize = 12;
            hintText.style.color = ParseColor(RiqMenuStyles.GrayLight);
            hintText.style.marginTop = 8;
            hintText.style.unityFontStyleAndWeight = FontStyle.Italic;
            container.Add(hintText);

            // Info text at bottom
            var infoText = new Label("Settings are saved automatically");
            infoText.style.fontSize = 12;
            infoText.style.color = ParseColor(RiqMenuStyles.GrayLight);
            infoText.style.marginTop = 24;
            container.Add(infoText);

            return container;
        }

        private VisualElement CreateSettingsToggle(string label, string description, bool initialValue, System.Action<bool> onChanged, out Button toggleButton) {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.paddingTop = 12;
            row.style.paddingBottom = 12;
            row.style.paddingLeft = 16;
            row.style.paddingRight = 16;
            row.style.marginBottom = 8;
            row.style.backgroundColor = Color.white;
            row.style.borderTopLeftRadius = 8;
            row.style.borderTopRightRadius = 8;
            row.style.borderBottomLeftRadius = 8;
            row.style.borderBottomRightRadius = 8;

            // Left side - label and description
            var leftSide = new VisualElement();
            leftSide.style.flexGrow = 1;

            var labelText = new Label(label);
            labelText.style.fontSize = 14;
            labelText.style.unityFontStyleAndWeight = FontStyle.Bold;
            labelText.style.color = ParseColor(RiqMenuStyles.Charcoal);
            leftSide.Add(labelText);

            var descText = new Label(description);
            descText.style.fontSize = 12;
            descText.style.color = ParseColor(RiqMenuStyles.Gray);
            descText.style.marginTop = 2;
            leftSide.Add(descText);

            row.Add(leftSide);

            // Right side - toggle button
            var toggleBtn = new Button();
            toggleBtn.focusable = false;
            toggleBtn.style.width = 60;
            toggleBtn.style.height = 32;
            toggleBtn.style.borderTopLeftRadius = 16;
            toggleBtn.style.borderTopRightRadius = 16;
            toggleBtn.style.borderBottomLeftRadius = 16;
            toggleBtn.style.borderBottomRightRadius = 16;
            toggleBtn.style.borderTopWidth = 0;
            toggleBtn.style.borderBottomWidth = 0;
            toggleBtn.style.borderLeftWidth = 0;
            toggleBtn.style.borderRightWidth = 0;

            bool currentValue = initialValue;
            UpdateToggleStyle(toggleBtn, currentValue);

            toggleBtn.clicked += () => {
                currentValue = !currentValue;
                UpdateToggleStyle(toggleBtn, currentValue);
                onChanged?.Invoke(currentValue);
            };

            row.Add(toggleBtn);
            toggleButton = toggleBtn;

            return row;
        }

        private void UpdateToggleStyle(Button toggle, bool isOn) {
            if (isOn) {
                toggle.text = "ON";
                toggle.style.backgroundColor = ParseColor(RiqMenuStyles.CyanDark);
                toggle.style.color = Color.white;
            }
            else {
                toggle.text = "OFF";
                toggle.style.backgroundColor = ParseColor(RiqMenuStyles.GrayLighter);
                toggle.style.color = ParseColor(RiqMenuStyles.Gray);
            }
        }

        private VisualElement CreateSettingsCycleToggle(string label, string description, string initialLabel, System.Action onCycle, out Button toggleButton) {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.paddingTop = 12;
            row.style.paddingBottom = 12;
            row.style.paddingLeft = 16;
            row.style.paddingRight = 16;
            row.style.marginBottom = 8;
            row.style.backgroundColor = Color.white;
            row.style.borderTopLeftRadius = 8;
            row.style.borderTopRightRadius = 8;
            row.style.borderBottomLeftRadius = 8;
            row.style.borderBottomRightRadius = 8;

            // Left side - label and description
            var leftSide = new VisualElement();
            leftSide.style.flexGrow = 1;

            var labelText = new Label(label);
            labelText.style.fontSize = 14;
            labelText.style.unityFontStyleAndWeight = FontStyle.Bold;
            labelText.style.color = ParseColor(RiqMenuStyles.Charcoal);
            leftSide.Add(labelText);

            var descText = new Label(description);
            descText.style.fontSize = 12;
            descText.style.color = ParseColor(RiqMenuStyles.Gray);
            descText.style.marginTop = 2;
            leftSide.Add(descText);

            row.Add(leftSide);

            // Right side - cycle button
            var cycleBtn = new Button();
            cycleBtn.text = initialLabel;
            cycleBtn.focusable = false;
            cycleBtn.style.minWidth = 100;
            cycleBtn.style.height = 32;
            cycleBtn.style.borderTopLeftRadius = 16;
            cycleBtn.style.borderTopRightRadius = 16;
            cycleBtn.style.borderBottomLeftRadius = 16;
            cycleBtn.style.borderBottomRightRadius = 16;
            cycleBtn.style.borderTopWidth = 0;
            cycleBtn.style.borderBottomWidth = 0;
            cycleBtn.style.borderLeftWidth = 0;
            cycleBtn.style.borderRightWidth = 0;
            cycleBtn.style.paddingLeft = 12;
            cycleBtn.style.paddingRight = 12;

            UpdateCycleButtonStyle(cycleBtn, initialLabel);

            cycleBtn.clicked += () => {
                onCycle?.Invoke();
            };

            row.Add(cycleBtn);
            toggleButton = cycleBtn;

            return row;
        }

        private void UpdateCycleButtonStyle(Button btn, string label) {
            btn.text = label;
            if (label == "OFF") {
                btn.style.backgroundColor = ParseColor(RiqMenuStyles.GrayLighter);
                btn.style.color = ParseColor(RiqMenuStyles.Gray);
            }
            else if (label == "MISS") {
                btn.style.backgroundColor = ParseColor(RiqMenuStyles.CoralLight);
                btn.style.color = ParseColor(RiqMenuStyles.Coral);
            }
            else if (label == "NON-PERFECT") {
                btn.style.backgroundColor = ParseColor(RiqMenuStyles.YellowLight);
                btn.style.color = ParseColor("#B8860B");
            }
            else {
                btn.style.backgroundColor = ParseColor(RiqMenuStyles.CyanDark);
                btn.style.color = Color.white;
            }
        }

        private string GetAutoRestartLabel(AutoRestartMode mode) {
            switch (mode) {
                case AutoRestartMode.OnMiss:
                    return "MISS";
                case AutoRestartMode.OnNonPerfect:
                    return "NON-PERFECT";
                default:
                    return "OFF";
            }
        }

        private void UpdateAutoRestartToggle() {
            if (_autoRestartToggle != null) {
                string label = GetAutoRestartLabel(_autoRestartValue);
                UpdateCycleButtonStyle(_autoRestartToggle, label);
            }
        }

        private void UpdateSettingsSelection() {
            for (int i = 0; i < _settingsRows.Count; i++) {
                var row = _settingsRows[i];
                bool isSelected = i == _selectedSettingIndex;

                // Apply selection border
                row.style.borderTopWidth = isSelected ? 2 : 0;
                row.style.borderBottomWidth = isSelected ? 2 : 0;
                row.style.borderLeftWidth = isSelected ? 2 : 0;
                row.style.borderRightWidth = isSelected ? 2 : 0;
                row.style.borderTopColor = ParseColor(RiqMenuStyles.Cyan);
                row.style.borderBottomColor = ParseColor(RiqMenuStyles.Cyan);
                row.style.borderLeftColor = ParseColor(RiqMenuStyles.Cyan);
                row.style.borderRightColor = ParseColor(RiqMenuStyles.Cyan);
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

            _footerHintsContainer = new VisualElement();
            _footerHintsContainer.style.flexDirection = FlexDirection.Column;
            _footerHintsContainer.style.alignItems = Align.Center;
            footer.Add(_footerHintsContainer);

            _touchActionBar = new VisualElement();
            _touchActionBar.style.flexDirection = FlexDirection.Row;
            _touchActionBar.style.flexWrap = Wrap.Wrap;
            _touchActionBar.style.justifyContent = Justify.Center;
            _touchActionBar.style.alignItems = Align.Center;
            _touchActionBar.style.marginTop = 8;
            footer.Add(_touchActionBar);

            UpdateFooterHints();

            return footer;
        }

        private VisualElement CreateHelpRow(FooterHintEntry[] entries) {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;

            foreach (var entry in entries)
            {
                var keyLabel = new Label(GetFooterKeyLabel(entry));
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

                var actionLabel = new Label(entry.Label);
                actionLabel.style.fontSize = 12;
                actionLabel.style.color = ParseColor(RiqMenuStyles.Gray);
                actionLabel.style.marginLeft = 8;
                actionLabel.style.marginRight = 32;
                row.Add(actionLabel);
            }

            return row;
        }

        private void TryConnectInputManager() {
            if (_inputManager) return;

            _inputManager = RiqMenuSystemManager.Instance?.InputManager;
            if (!_inputManager) return;

            _activeInputMethod = _inputManager.CurrentInputMethod;
            _inputManager.OnInputMethodChanged += HandleInputMethodChanged;
            UpdateFooterHints();
            UpdateContextHintLabels();
            UpdateAutoplayLabel();
            UpdateSearchFieldTouchAccessibility();
            UpdateSettingsSelection();
        }

        private void HandleInputMethodChanged(RiqInputMethod inputMethod) {
            _activeInputMethod = inputMethod;
            UpdateFooterHints();
            UpdateContextHintLabels();
            UpdateAutoplayLabel();
            UpdateSearchFieldTouchAccessibility();
            UpdateSettingsSelection();
        }

        private void UpdateFooterHints() {
            if (_footerHintsContainer == null) return;

            _footerHintsContainer.Clear();
            if (_activeInputMethod == RiqInputMethod.Touch) {
                UpdateTouchActionBar();
                return;
            }

            if (!FooterHintRowsByTab.TryGetValue(_currentTab, out var rows)) return;

            for (int i = 0; i < rows.Length; i++) {
                var row = CreateHelpRow(rows[i]);
                if (i < rows.Length - 1) {
                    row.style.marginBottom = 8;
                }

                _footerHintsContainer.Add(row);
            }

            UpdateTouchActionBar();
        }

        private void UpdateTouchActionBar() {
            if (_touchActionBar == null) return;

            _touchActionBar.Clear();
            bool isTouch = _activeInputMethod == RiqInputMethod.Touch;
            _touchActionBar.style.display = isTouch ? DisplayStyle.Flex : DisplayStyle.None;
            if (!isTouch) return;

            if (!TouchActionEntriesByTab.TryGetValue(_currentTab, out var entries)) return;
            for (int i = 0; i < entries.Length; i++) {
                var entry = entries[i];
                if (entry.KeyBindingType != FooterKeyBindingType.Action) continue;
                _touchActionBar.Add(CreateTouchActionButton(entry.Label, () => ExecuteTouchAction(entry.PrimaryAction)));
            }
        }

        private Button CreateTouchActionButton(string text, System.Action onClick) {
            var button = new Button(() => onClick?.Invoke()) { text = text };
            button.focusable = false;
            button.style.marginLeft = 8;
            button.style.marginRight = 8;
            button.style.marginTop = 6;
            button.style.marginBottom = 6;
            button.style.paddingLeft = 18;
            button.style.paddingRight = 18;
            button.style.paddingTop = 10;
            button.style.paddingBottom = 10;
            button.style.minHeight = 44;
            button.style.fontSize = 14;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.color = ParseColor(RiqMenuStyles.Charcoal);
            button.style.backgroundColor = ParseColor(RiqMenuStyles.GrayLighter);
            button.style.borderTopLeftRadius = 10;
            button.style.borderTopRightRadius = 10;
            button.style.borderBottomLeftRadius = 10;
            button.style.borderBottomRightRadius = 10;
            button.style.borderTopWidth = 2;
            button.style.borderBottomWidth = 2;
            button.style.borderLeftWidth = 2;
            button.style.borderRightWidth = 2;
            button.style.borderTopColor = ParseColor(RiqMenuStyles.GrayLight);
            button.style.borderBottomColor = ParseColor(RiqMenuStyles.GrayLight);
            button.style.borderLeftColor = ParseColor(RiqMenuStyles.GrayLight);
            button.style.borderRightColor = ParseColor(RiqMenuStyles.GrayLight);
            return button;
        }

        private void ExecuteTouchAction(RiqInputAction action) {
            if (_touchActionHandlers != null && _touchActionHandlers.TryGetValue(action, out var handler)) {
                handler.Invoke();
            }
        }

        private void UpdateContextHintLabels() {
            if (_tabsHintLabel != null) {
                _tabsHintLabel.text = $"{BuildCombinedBindingLabel(RiqInputAction.PreviousTab, RiqInputAction.NextTab)} to switch tabs";
            }

            if (_settingsHintLabel != null) {
                _settingsHintLabel.text = _activeInputMethod == RiqInputMethod.Touch
                    ? "Tap to toggle"
                    : $"Use {ResolveBindingLabel(RiqInputAction.NavigateUp)} to navigate, {ResolveBindingLabel(RiqInputAction.Submit)} to toggle";
            }

            if (_editorHintLabel != null) {
                _editorHintLabel.text = $"Press {ResolveBindingLabel(RiqInputAction.Cancel)} to cancel, {ResolveBindingLabel(RiqInputAction.Submit)} to save";
            }
        }

        private string GetFooterKeyLabel(FooterHintEntry entry) {
            switch (entry.KeyBindingType) {
                case FooterKeyBindingType.Action:
                    return ResolveBindingLabel(entry.PrimaryAction);
                case FooterKeyBindingType.CombinedActions:
                    return BuildCombinedBindingLabel(entry.PrimaryAction, entry.SecondaryAction);
                default:
                    return string.Empty;
            }
        }

        private string BuildCombinedBindingLabel(RiqInputAction leftAction, RiqInputAction rightAction) {
            string left = ResolveBindingLabel(leftAction);
            string right = ResolveBindingLabel(rightAction);
            if (string.IsNullOrEmpty(left)) return right;
            if (string.IsNullOrEmpty(right) || left == right) return left;
            return $"{left} / {right}";
        }

        private string ResolveBindingLabel(RiqInputAction action) {
            if (_inputManager) {
                return _inputManager.GetBindingLabel(action, _activeInputMethod);
            }

            switch (action) {
                case RiqInputAction.Submit:
                    return "Enter";
                case RiqInputAction.NavigateUp:
                    return "WASD";
                case RiqInputAction.Search:
                    return "Tab";
                case RiqInputAction.Edit:
                    return "R";
                case RiqInputAction.ToggleAutoplay:
                    return "P";
                case RiqInputAction.ToggleMute:
                    return "M";
                case RiqInputAction.Cancel:
                    return "Esc";
                case RiqInputAction.PreviousTab:
                    return "Q";
                case RiqInputAction.NextTab:
                    return "E";
                default:
                    return string.Empty;
            }
        }

        private VisualElement CreateSongItem(string title, string artist, string mapper, string fileType, int? bpm, int? downloads, bool selected, bool isDownloaded = false) {
            var item = new VisualElement();
            item.style.width = 420;
            item.style.height = 130;
            item.style.flexDirection = FlexDirection.Column;
            item.style.paddingLeft = 16;
            item.style.paddingRight = 16;
            item.style.paddingTop = 12;
            item.style.paddingBottom = 12;
            item.style.marginRight = 12;
            item.style.marginBottom = 12;
            item.style.borderTopLeftRadius = 16;
            item.style.borderTopRightRadius = 16;
            item.style.borderBottomLeftRadius = 16;
            item.style.borderBottomRightRadius = 16;
            item.style.borderTopWidth = 3;
            item.style.borderBottomWidth = 3;
            item.style.borderLeftWidth = 3;
            item.style.borderRightWidth = 3;

            if (selected) {
                item.style.backgroundColor = ParseColor("#E0F7FF");
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

            // Title row
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 16;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = ParseColor(RiqMenuStyles.Charcoal);
            titleLabel.style.flexGrow = 1;
            titleLabel.style.flexShrink = 1;
            titleLabel.style.overflow = Overflow.Hidden;
            titleLabel.style.textOverflow = TextOverflow.Ellipsis;
            titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            header.Add(titleLabel);

            var badge = CreateBadge(fileType.ToUpper(), fileType.ToLower() == "bop" ? "bop" : "riq");
            header.Add(badge);

            if (isDownloaded) {
                var dlBadge = CreateBadge("✓", "downloaded");
                header.Add(dlBadge);
            }

            item.Add(header);

            // Artist row
            if (!string.IsNullOrEmpty(artist)) {
                var artistLabel = new Label(artist);
                artistLabel.style.fontSize = 13;
                artistLabel.style.color = ParseColor(RiqMenuStyles.Charcoal);
                artistLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                artistLabel.style.overflow = Overflow.Hidden;
                artistLabel.style.textOverflow = TextOverflow.Ellipsis;
                artistLabel.style.whiteSpace = WhiteSpace.NoWrap;
                artistLabel.style.marginBottom = 2;
                item.Add(artistLabel);
            }

            // Mapper + badges row
            var meta = new VisualElement();
            meta.style.flexDirection = FlexDirection.Row;
            meta.style.alignItems = Align.Center;

            var mapperLabel = new Label(!string.IsNullOrEmpty(mapper) ? $"Mapped by {mapper}" : !string.IsNullOrEmpty(artist) ? "" : "Unknown");
            if (!string.IsNullOrEmpty(mapperLabel.text)) {
                mapperLabel.style.fontSize = 12;
                mapperLabel.style.color = ParseColor(RiqMenuStyles.Gray);
                mapperLabel.style.overflow = Overflow.Hidden;
                mapperLabel.style.textOverflow = TextOverflow.Ellipsis;
                mapperLabel.style.whiteSpace = WhiteSpace.NoWrap;
                mapperLabel.style.flexShrink = 1;
                meta.Add(mapperLabel);
            }

            if (bpm.HasValue) {
                var bpmBadge = CreateBadge($"{bpm} BPM", "bpm");
                meta.Add(bpmBadge);
            }

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
                case "downloaded":
                    badge.style.backgroundColor = ParseColor(RiqMenuStyles.Mint);
                    badge.style.color = ParseColor(RiqMenuStyles.Green);
                    badge.style.borderTopColor = ParseColor(RiqMenuStyles.Green);
                    badge.style.borderBottomColor = ParseColor(RiqMenuStyles.Green);
                    badge.style.borderLeftColor = ParseColor(RiqMenuStyles.Green);
                    badge.style.borderRightColor = ParseColor(RiqMenuStyles.Green);
                    break;
            }

            return badge;
        }

        private void BuildLocalHashCache() {
            _localFileHashes.Clear();
            string songsFolder = Path.Combine(Application.dataPath, "StreamingAssets", "RiqMenu");
            if (!Directory.Exists(songsFolder)) return;

            // Read hashes from metadata files (fast) instead of computing them
            foreach (var metaFile in Directory.GetFiles(songsFolder, "*.meta.json")) {
                try {
                    string json = File.ReadAllText(metaFile, System.Text.Encoding.UTF8);
                    // Simple regex to extract hash from JSON
                    var match = System.Text.RegularExpressions.Regex.Match(json, @"""fileHash""\s*:\s*""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && !string.IsNullOrEmpty(match.Groups[1].Value)) {
                        _localFileHashes.Add(match.Groups[1].Value);
                    }
                }
                catch { }
            }
        }

        private bool IsHashDownloaded(string hash) {
            return !string.IsNullOrEmpty(hash) && _localFileHashes.Contains(hash);
        }

        private void SwitchTab(OverlayTab tab) {
            if (_currentTab == tab) return;

            // Stop audio preview when switching tabs
            StopPreview();

            _currentTab = tab;
            CurrentTabStatic = tab;
            _tabJustSwitched = true; // Prevent same-frame input processing
            ApplyTabStyle(_localTab, tab == OverlayTab.Local);
            ApplyTabStyle(_onlineTab, tab == OverlayTab.Online);
            ApplyTabStyle(_settingsTab, tab == OverlayTab.Settings);

            _localContent.style.display = tab == OverlayTab.Local ? DisplayStyle.Flex : DisplayStyle.None;
            _onlineContent.style.display = tab == OverlayTab.Online ? DisplayStyle.Flex : DisplayStyle.None;
            _settingsContent.style.display = tab == OverlayTab.Settings ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateFooterHints();

            if (tab == OverlayTab.Online) {
                if (_onlineSongs.Count == 0) {
                    LoadOnlineSongs();
                }
                else {
                    // Refresh to update downloaded indicators
                    BuildLocalHashCache();
                    RefreshOnlineSongList();
                }
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
            BuildLocalHashCache();
            ShowStatus("Loading songs...", "loading");

            _apiClient.GetSongs(_onlineSort, 1, (songs, error) => {
                if (error != null) {
                    _pendingOnlineError = error;
                    _isLoading = false;
                    return;
                }

                _onlineSongs = songs ?? new List<OnlineSong>();
                _hasMorePages = songs != null && songs.Count >= 20;
                _selectedOnlineIndex = 0;
                _pendingHideStatus = true;
                _pendingOnlineRefresh = true;
                _isLoading = false;
            });
        }

        private void LoadMoreOnlineSongs() {
            if (_isLoading || !_hasMorePages) return;

            _isLoading = true;
            _currentPage++;

            if (!string.IsNullOrEmpty(_currentSearchQuery)) {
                _hasMorePages = false;
                _isLoading = false;
                return;
            }

            _apiClient.GetSongs(_onlineSort, _currentPage, (songs, error) => {
                if (error != null) {
                    _currentPage--;
                    _isLoading = false;
                    return;
                }

                if (songs == null || songs.Count == 0) {
                    _hasMorePages = false;
                    _isLoading = false;
                    return;
                }

                _hasMorePages = songs.Count >= 20;
                _onlineSongs.AddRange(songs);
                _pendingOnlineRefresh = true;
                _isLoading = false;
            });
        }

        private void RefreshOnlineSongList() {
            _onlineGridWrapper.Clear();
            _onlineSongElements.Clear();

            for (int i = 0; i < _onlineSongs.Count; i++) {
                var song = _onlineSongs[i];
                bool isDownloaded = IsHashDownloaded(song.FileHash);
                var item = CreateSongItem(
                    song.Title ?? song.DisplayTitle,
                    song.Artist,
                    song.Creator ?? song.UploaderName,
                    song.FileType ?? "riq",
                    song.Bpm.HasValue ? (int?)Mathf.RoundToInt(song.Bpm.Value) : null,
                    song.DownloadCount,
                    i == _selectedOnlineIndex,
                    isDownloaded
                );

                int index = i;
                item.RegisterCallback<ClickEvent>(evt => {
                    bool wasSelected = index == _selectedOnlineIndex;
                    SelectOnlineSong(index);
                    if (_activeInputMethod == RiqInputMethod.Touch && wasSelected) {
                        ExecuteCurrentTabPrimaryAction();
                    }
                });

                _onlineGridWrapper.Add(item);
                _onlineSongElements.Add(item);
            }
        }

        public void RefreshLocalSongList() {
            _localGridWrapper.Clear();
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
                    int artistScore = CalculateFuzzyScore(song.Artist ?? "", _localSearchQuery);
                    int creatorScore = CalculateFuzzyScore(song.Creator ?? "", _localSearchQuery);
                    int bestScore = Mathf.Max(titleScore, Mathf.Max(artistScore, creatorScore));
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
                var item = CreateSongItem(
                    song.SongTitle,
                    song.Artist,
                    song.Creator,
                    fileType,
                    song.Bpm.HasValue ? (int?)Mathf.RoundToInt(song.Bpm.Value) : null,
                    song.DownloadCount,
                    displayIndex == _selectedLocalIndex
                );

                int index = displayIndex;
                item.RegisterCallback<ClickEvent>(evt => {
                    bool wasSelected = index == _selectedLocalIndex;
                    SelectLocalSong(index);
                    if (_activeInputMethod == RiqInputMethod.Touch && wasSelected) {
                        ExecuteCurrentTabPrimaryAction();
                    }
                });

                _localGridWrapper.Add(item);
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
            // Only allow selection when on Local tab
            if (_currentTab != OverlayTab.Local) return;
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
            // Only allow selection when on Online tab
            if (_currentTab != OverlayTab.Online) return;
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
            TryConnectInputManager();

            // Process pending UI updates from background threads
            if (_pendingOnlineError != null) {
                string err = _pendingOnlineError;
                _pendingOnlineError = null;
                ShowStatus($"Error: {err}", "error");
            }
            if (_pendingStatusMessage != null) {
                string msg = _pendingStatusMessage;
                string type = _pendingStatusType ?? "loading";
                _pendingStatusMessage = null;
                _pendingStatusType = null;
                ShowStatus(msg, type);
            }
            if (_pendingDownloadHash != null) {
                string hash = _pendingDownloadHash;
                _pendingDownloadHash = null;
                _localFileHashes.Add(hash);
                _pendingOnlineRefresh = true;
            }
            if (_pendingHideStatus) {
                _pendingHideStatus = false;
                HideStatus();
            }
            if (_pendingOnlineRefresh) {
                _pendingOnlineRefresh = false;
                RefreshOnlineSongList();
                ScrollToSelectedOnline();
            }

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

            // Load more online songs when near the bottom
            if (_currentTab == OverlayTab.Online && !_isLoading && _hasMorePages) {
                float contentHeight = _onlineGridWrapper.layout.height;
                float viewportHeight = _onlineSongList.layout.height;
                float scrollY = _onlineSongList.scrollOffset.y;
                if (contentHeight > 0 && scrollY + viewportHeight >= contentHeight - 200) {
                    LoadMoreOnlineSongs();
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
            BuildLocalHashCache();
            ShowStatus($"Searching for \"{query}\"...", "loading");

            _apiClient.SearchSongs(query, (songs, error) => {
                if (error != null) {
                    _pendingOnlineError = error;
                    _isLoading = false;
                    return;
                }

                _onlineSongs = songs ?? new List<OnlineSong>();
                _selectedOnlineIndex = 0;
                _pendingHideStatus = true;
                _pendingOnlineRefresh = true;
                _isLoading = false;
            });
        }

        private void HandleInput() {
            var inputManager = _inputManager ?? RiqMenuSystemManager.Instance?.InputManager;

            // Skip input for one frame after editor closes to prevent Enter from playing song
            if (_editorJustClosed) {
                _editorJustClosed = false;
                return;
            }

            // Handle editor modal input separately
            if (_isEditorOpen) {
                bool cancelPressed = inputManager?.IsActionDown(RiqInputAction.Cancel, ignoreBlocking: true) ??
                                     UnityEngine.Input.GetKeyDown(KeyCode.Escape);
                bool submitPressed = inputManager?.GetMenuSubmitDown(ignoreBlocking: true, allowSpace: false) ??
                                     (UnityEngine.Input.GetKeyDown(KeyCode.Return) ||
                                      UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter));

                if (cancelPressed) {
                    CloseEditor(false);
                }
                else if (submitPressed) {
                    CloseEditor(true);
                }
                return; // Don't process other input while editor is open
            }

            // Skip input processing for one frame after tab switch
            if (_tabJustSwitched) {
                _tabJustSwitched = false;
                return;
            }

            // Escape always works - exits search mode or closes overlay
            if (inputManager?.IsActionDown(RiqInputAction.Cancel, ignoreBlocking: true) ??
                UnityEngine.Input.GetKeyDown(KeyCode.Escape)) {
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
                bool submitPressed = inputManager?.GetMenuSubmitDown(ignoreBlocking: true, allowSpace: false) ??
                                     (UnityEngine.Input.GetKeyDown(KeyCode.Return) ||
                                      UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter));
                bool searchPressed = inputManager?.IsActionDown(RiqInputAction.Search, ignoreBlocking: true) ??
                                     UnityEngine.Input.GetKeyDown(KeyCode.Tab);
                if (submitPressed || searchPressed) {
                    ExitSearchMode();
                }
                return; // Skip all other input while typing
            }

            // Tab to enter search mode (only on Local and Online tabs)
            if ((inputManager?.IsActionDown(RiqInputAction.Search, ignoreBlocking: true) ??
                 UnityEngine.Input.GetKeyDown(KeyCode.Tab)) &&
                _currentTab != OverlayTab.Settings) {
                EnterSearchMode();
                return;
            }

            // R to edit metadata (Local tab only)
            if ((inputManager?.IsActionDown(RiqInputAction.Edit, ignoreBlocking: true) ??
                 UnityEngine.Input.GetKeyDown(KeyCode.R)) &&
                _currentTab == OverlayTab.Local) {
                OpenEditor();
                return;
            }

            // Tab switching with Q/E
            if (inputManager?.IsActionDown(RiqInputAction.PreviousTab, ignoreBlocking: true) ??
                UnityEngine.Input.GetKeyDown(KeyCode.Q)) {
                if (_currentTab == OverlayTab.Local)
                    SwitchTab(OverlayTab.Settings);
                else if (_currentTab == OverlayTab.Online)
                    SwitchTab(OverlayTab.Local);
                else
                    SwitchTab(OverlayTab.Online);
                return;
            }

            if (inputManager?.IsActionDown(RiqInputAction.NextTab, ignoreBlocking: true) ??
                UnityEngine.Input.GetKeyDown(KeyCode.E)) {
                if (_currentTab == OverlayTab.Local)
                    SwitchTab(OverlayTab.Online);
                else if (_currentTab == OverlayTab.Online)
                    SwitchTab(OverlayTab.Settings);
                else
                    SwitchTab(OverlayTab.Local);
                return;
            }

            // Autoplay toggle (P key) - only in Local tab
            if ((inputManager?.IsActionDown(RiqInputAction.ToggleAutoplay, ignoreBlocking: true) ??
                 UnityEngine.Input.GetKeyDown(KeyCode.P)) &&
                _currentTab == OverlayTab.Local) {
                ToggleAutoplay();
            }

            // Mute toggle (M key)
            if (inputManager?.IsActionDown(RiqInputAction.ToggleMute, ignoreBlocking: true) ??
                UnityEngine.Input.GetKeyDown(KeyCode.M)) {
                ToggleMute();
            }

            // Grid navigation
            var navDirection = inputManager?.ConsumeMenuNavigationDirection(ignoreBlocking: true) ??
                               NavigationDirection.None;
            switch (navDirection, _currentTab) {
                case (NavigationDirection.Up, OverlayTab.Local): {
                    int newIndex = _selectedLocalIndex - GRID_COLUMNS;
                    if (newIndex >= 0) {
                        SelectLocalSong(newIndex);
                        ScrollToSelectedLocal();
                    }

                    break;
                }
                case (NavigationDirection.Up, OverlayTab.Online): {
                    int newIndex = _selectedOnlineIndex - GRID_COLUMNS;
                    if (newIndex >= 0) {
                        SelectOnlineSong(newIndex);
                        ScrollToSelectedOnline();
                    }

                    break;
                }
                case (NavigationDirection.Up, OverlayTab.Settings): {
                    _selectedSettingIndex = Mathf.Max(0, _selectedSettingIndex - 1);
                    UpdateSettingsSelection();

                    break;
                }
                case (NavigationDirection.Down, OverlayTab.Local): {
                    var newIndex = _selectedLocalIndex + GRID_COLUMNS;
                    if (newIndex < _localSongElements.Count) {
                        SelectLocalSong(newIndex);
                    }
                    else if (_selectedLocalIndex / GRID_COLUMNS < (_localSongElements.Count - 1) / GRID_COLUMNS) {
                        SelectLocalSong(_localSongElements.Count - 1);
                    }

                    ScrollToSelectedLocal();
                    break;
                }
                case (NavigationDirection.Down, OverlayTab.Online): {
                    var newIndex = _selectedOnlineIndex + GRID_COLUMNS;
                    if (newIndex < _onlineSongElements.Count) {
                        SelectOnlineSong(newIndex);
                    }
                    else if (_selectedOnlineIndex / GRID_COLUMNS < (_onlineSongElements.Count - 1) / GRID_COLUMNS) {
                        SelectOnlineSong(_onlineSongElements.Count - 1);
                    }

                    ScrollToSelectedOnline();

                    if (_selectedOnlineIndex >= _onlineSongElements.Count - GRID_COLUMNS * 2) {
                        LoadMoreOnlineSongs();
                    }

                    break;
                }
                case (NavigationDirection.Down, OverlayTab.Settings): {
                    _selectedSettingIndex = Mathf.Min(_settingsRows.Count - 1, _selectedSettingIndex + 1);
                    UpdateSettingsSelection();

                    break;
                }
                case (NavigationDirection.Left, OverlayTab.Local): {
                    if (_selectedLocalIndex % GRID_COLUMNS > 0) {
                        SelectLocalSong(_selectedLocalIndex - 1);
                        ScrollToSelectedLocal();
                    }

                    break;
                }
                case (NavigationDirection.Left, OverlayTab.Online): {
                    if (_selectedOnlineIndex % GRID_COLUMNS > 0) {
                        SelectOnlineSong(_selectedOnlineIndex - 1);
                        ScrollToSelectedOnline();
                    }

                    break;
                }
                case (NavigationDirection.Right, OverlayTab.Local): {
                    if (_selectedLocalIndex % GRID_COLUMNS < GRID_COLUMNS - 1 &&
                        _selectedLocalIndex < _localSongElements.Count - 1) {
                        SelectLocalSong(_selectedLocalIndex + 1);
                        ScrollToSelectedLocal();
                    }

                    break;
                }
                case (NavigationDirection.Right, OverlayTab.Online): {
                    if (_selectedOnlineIndex % GRID_COLUMNS < GRID_COLUMNS - 1 &&
                        _selectedOnlineIndex < _onlineSongElements.Count - 1) {
                        SelectOnlineSong(_selectedOnlineIndex + 1);
                        ScrollToSelectedOnline();
                    }

                    if (_selectedOnlineIndex >= _onlineSongElements.Count - GRID_COLUMNS * 2) {
                        LoadMoreOnlineSongs();
                    }

                    break;
                }
                case (NavigationDirection.None, OverlayTab.Local):
                    if (inputManager?.IsActionDown(RiqInputAction.PageUp, ignoreBlocking: true) ??
                        UnityEngine.Input.GetKeyDown(KeyCode.PageUp)) {
                        SelectLocalSong(Mathf.Max(0, _selectedLocalIndex - PAGE_SIZE));
                        ScrollToSelectedLocal();
                    }
                    else if (inputManager?.IsActionDown(RiqInputAction.PageDown, ignoreBlocking: true) ??
                             UnityEngine.Input.GetKeyDown(KeyCode.PageDown)) {
                        SelectLocalSong(Mathf.Min(_localSongElements.Count - 1, _selectedLocalIndex + PAGE_SIZE));
                        ScrollToSelectedLocal();
                    }
                    
                    break;
                case (NavigationDirection.None, OverlayTab.Online):
                    if (inputManager?.IsActionDown(RiqInputAction.PageUp, ignoreBlocking: true) ??
                        UnityEngine.Input.GetKeyDown(KeyCode.PageUp)) {
                        SelectOnlineSong(Mathf.Max(0, _selectedOnlineIndex - PAGE_SIZE));
                        ScrollToSelectedOnline();
                    }
                    else if (inputManager?.IsActionDown(RiqInputAction.PageDown, ignoreBlocking: true) ??
                             UnityEngine.Input.GetKeyDown(KeyCode.PageDown)) {
                        SelectOnlineSong(Mathf.Min(_onlineSongElements.Count - 1,
                            _selectedOnlineIndex + PAGE_SIZE));
                        ScrollToSelectedOnline();

                        if (_selectedOnlineIndex >= _onlineSongElements.Count - GRID_COLUMNS * 2) {
                            LoadMoreOnlineSongs();
                        }
                    }

                    break;
            }

            // Selection - only handle for the current tab
            // Space is an alternative to Enter, but not while searching (need space for typing)
            bool selectPressed = inputManager?.GetMenuSubmitDown(ignoreBlocking: true, allowSpace: !_isSearchMode) ??
                                 (UnityEngine.Input.GetKeyDown(KeyCode.Return) ||
                                  UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter) ||
                                  (!_isSearchMode && UnityEngine.Input.GetKeyDown(KeyCode.Space)));
            if (selectPressed) {
                bool stopProcessing = HandleSubmit();
                if (stopProcessing) {
                    return; // Some actions on submit require that nothing else happens afterward
                }
            }
        }

        private void ExecuteCurrentTabPrimaryAction() {
            HandleSubmit();
        }

        private bool HandleSubmit() {
            bool localVisible = _localContent?.style.display == DisplayStyle.Flex;
            bool onlineVisible = _onlineContent?.style.display == DisplayStyle.Flex;

            if (_currentTab == OverlayTab.Local && localVisible) {
                // Only play if we have local songs
                if (_localSongElements.Count > 0 && _selectedLocalIndex >= 0) {
                    int actualIndex = _filteredLocalIndices.Count > 0 && _selectedLocalIndex < _filteredLocalIndices.Count
                        ? _filteredLocalIndices[_selectedLocalIndex]
                        : _selectedLocalIndex;
                    OnSongSelected?.Invoke(actualIndex, OverlayTab.Local);
                    Hide();
                }

                return true; // Don't process anything else after Enter on Local tab
            }

            if (_currentTab == OverlayTab.Online && onlineVisible) {
                // Only download if we have online songs
                if (_onlineSongElements.Count > 0 && _selectedOnlineIndex >= 0) {
                    DownloadSelectedSong();
                }

                return true; // Don't process anything else after Enter on Online tab
            }

            if (_currentTab == OverlayTab.Settings) {
                // Toggle the currently selected setting
                if (_selectedSettingIndex == 0 && _accuracyBarToggle != null) {
                    // Accuracy Bar toggle (boolean)
                    _accuracyBarValue = !_accuracyBarValue;
                    UpdateToggleStyle(_accuracyBarToggle, _accuracyBarValue);
                    RiqMenuSettings.AccuracyBarEnabled = _accuracyBarValue;
                }
                else if (_selectedSettingIndex == 1 && _autoRestartToggle != null) {
                    // Auto-Restart toggle (cycles through modes)
                    _autoRestartValue = RiqMenuSettings.CycleAutoRestartMode();
                    UpdateAutoRestartToggle();
                }
                else if (_selectedSettingIndex == 2 && _progressBarToggle != null) {
                    // Progress Bar toggle (boolean)
                    _progressBarValue = !_progressBarValue;
                    UpdateToggleStyle(_progressBarToggle, _progressBarValue);
                    RiqMenuSettings.ProgressBarEnabled = _progressBarValue;
                }
            }

            return false;
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
                // Focus the internal text input element for keyboard input
                var textInput = field.Q("unity-text-input");
                if (textInput != null) {
                    textInput.Focus();
                }
                field.SelectAll(); // Triggers edit mode
            });

            UpdateSearchFieldTouchAccessibility();
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
            UpdateSearchFieldTouchAccessibility();
        }

        private void UpdateSearchFieldTouchAccessibility() {
            bool touchFocusable = _activeInputMethod == RiqInputMethod.Touch || _isSearchMode;
            if (_localSearchField != null) {
                _localSearchField.focusable = touchFocusable;
            }
            if (_onlineSearchField != null) {
                _onlineSearchField.focusable = touchFocusable;
            }
        }

        private void ToggleAutoplay() {
            if (_currentTab != OverlayTab.Local) return;
            MixtapeLoaderCustom.autoplay = !MixtapeLoaderCustom.autoplay;
            UpdateAutoplayLabel();
        }

        private void ToggleMute() {
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            audioManager?.ToggleMute();
        }

        private void UpdateAutoplayLabel() {
            if (_autoplayLabel != null) {
                string toggleHint = _inputManager?.GetBindingLabel(RiqInputAction.ToggleAutoplay, _activeInputMethod);
                if (string.IsNullOrEmpty(toggleHint)) {
                    toggleHint = "P";
                }

                _autoplayLabel.text = $"Autoplay: {(MixtapeLoaderCustom.autoplay ? "ON" : "OFF")} ({toggleHint})";
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
            // Guard against empty list or invalid index
            if (_onlineSongs.Count == 0) {
                ShowStatus("No songs loaded yet", "error");
                return;
            }
            if (_selectedOnlineIndex < 0 || _selectedOnlineIndex >= _onlineSongs.Count) {
                ShowStatus("No song selected", "error");
                return;
            }

            var song = _onlineSongs[_selectedOnlineIndex];
            string songsFolder = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "RiqMenu");

            ShowStatus($"Downloading {song.Title}...", "loading");

            _apiClient.DownloadSong(song, songsFolder,
                (filePath, error) => {
                    if (error != null && error.Contains("already downloaded")) {
                        _pendingStatusMessage = "Already downloaded! Switch to Local to play.";
                        _pendingStatusType = "success";
                        if (!string.IsNullOrEmpty(song.FileHash)) {
                            _pendingDownloadHash = song.FileHash;
                        }
                    }
                    else if (error != null) {
                        _pendingOnlineError = error;
                    }
                    else {
                        _pendingStatusMessage = "Downloaded! Switch to Local to play.";
                        _pendingStatusType = "success";
                        if (!string.IsNullOrEmpty(song.FileHash)) {
                            _pendingDownloadHash = song.FileHash;
                        }
                    }
                },
                null
            );
        }

        public void Show() {
            if (_isVisible) return;
            TryConnectInputManager();

            // Prevent immediate re-open after hiding (e.g., after playing a song)
            if (Time.time - _lastHideTime < REOPEN_COOLDOWN) {
                return;
            }

            // Only allow showing on menu screens, not during gameplay
            string sceneName = RiqMenuState.CurrentScene.name;
            if (sceneName != SceneKey.TitleScreen.ToString() &&
                sceneName != SceneKey.StageSelect.ToString() &&
                sceneName != "StageSelectDemo") {
                Debug.Log($"[RiqMenu] Blocked Show() - not on menu screen (scene: {sceneName})");
                return;
            }

            _isVisible = true;
            _inputDelayTimer = INPUT_DELAY; // Ignore input briefly after opening
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.Focus(); // Focus to receive keyboard events

            // Reset state
            _lastPreviewedSong = -1;
            _localSearchQuery = "";
            _selectedLocalIndex = 0;
            _isSearchMode = false;
            _tabJustSwitched = false;

            // Ensure correct tab state and visibility on open - always start on Local
            _currentTab = OverlayTab.Local;
            CurrentTabStatic = OverlayTab.Local;
            _localContent.style.display = DisplayStyle.Flex;
            _onlineContent.style.display = DisplayStyle.None;
            _settingsContent.style.display = DisplayStyle.None;
            ApplyTabStyle(_localTab, true);
            ApplyTabStyle(_onlineTab, false);
            ApplyTabStyle(_settingsTab, false);
            UpdateFooterHints();

            // Reset search fields
            if (_localSearchField != null) {
                _localSearchField.value = "Search local songs...";
                _localSearchField.Blur();
                var inp = _localSearchField.Q<VisualElement>("unity-text-input");
                if (inp != null) inp.style.color = ParseColor(RiqMenuStyles.GrayLight);
            }
            if (_onlineSearchField != null) {
                _onlineSearchField.value = "Search online songs...";
                _onlineSearchField.Blur();
                var inp = _onlineSearchField.Q<VisualElement>("unity-text-input");
                if (inp != null) inp.style.color = ParseColor(RiqMenuStyles.GrayLight);
            }
            UpdateSearchFieldTouchAccessibility();

            RefreshLocalSongList();
            UpdateAutoplayLabel();

            _inputManager?.BlockInput();

            OnOverlayOpened?.Invoke();

            // Start preview for currently selected song
            TryPreviewCurrentSong();
        }

        public void Hide() {
            if (!_isVisible) return;

            _isVisible = false;
            _lastHideTime = Time.time; // Record hide time to prevent immediate re-open
            _overlay.style.display = DisplayStyle.None;

            // Stop audio preview
            StopPreview();

            _inputManager?.UnblockInput();

            OnOverlayClosed?.Invoke();
        }

        public void Toggle() {
            // Prevent rapid toggling
            if (Time.time - _lastToggleTime < TOGGLE_COOLDOWN) return;
            _lastToggleTime = Time.time;

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
