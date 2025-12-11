using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using RiqMenu.Core;
using RiqMenu.Online;

namespace RiqMenu.UI
{
    public enum OverlayTab { Local, Online }

    /// <summary>
    /// Fullscreen overlay for custom song selection, preview, and online browsing
    /// </summary>
    public class SongsOverlay : MonoBehaviour {
        private bool _isVisible = false;
        private float _showTime = 0f;

        // Tab state
        private OverlayTab _currentTab = OverlayTab.Local;

        // Local songs state
        private int _selectedSong = 0;
        private int _scrollOffset = 0;
        private int _lastPreviewedSong = -1;
        private bool _isLoadingAudio = false;
        private float _loadingStartTime = 0f;

        // Search functionality (local)
        private bool _isSearchMode = false;
        private string _searchQuery = "";
        private List<int> _filteredSongIndices = new List<int>();
        private int _filteredSelectedIndex = 0;

        // Online songs state
        private RiqsApiClient _apiClient = new RiqsApiClient();
        private List<OnlineSong> _onlineSongs = new List<OnlineSong>();
        private int _onlineSelectedIndex = 0;
        private int _onlineScrollOffset = 0;
        private bool _isOnlineLoading = false;
        private bool _isOnlineSearchMode = false;
        private string _onlineSearchQuery = "";
        private string _onlineError = null;
        private string _onlineSort = "newest";

        // Download state
        private bool _isDownloading = false;
        private float _downloadProgress = 0f;
        private string _downloadStatus = null;

        private const int VISIBLE_SONGS = 10;
        private const float INPUT_DELAY = 0.2f;

        public bool IsVisible => _isVisible;

        public event System.Action OnOverlayOpened;
        public event System.Action OnOverlayClosed;
        public event System.Action<int> OnSongSelected;

        private void Update() {
            if (!_isVisible) return;

            // Handle loading indicators
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            if (_isLoadingAudio && audioManager != null && audioManager.IsPreviewPlaying) {
                _isLoadingAudio = false;
            }
            if (_isLoadingAudio && Time.time - _loadingStartTime > 3f) {
                _isLoadingAudio = false;
            }

            HandleInput();
        }

        public void DrawOverlayGUI() {
            if (!_isVisible) return;
            DrawOverlay();
        }

        private void HandleInput() {
            if (Time.time - _showTime < INPUT_DELAY) return;
            if (_isDownloading) return; // Block input while downloading

            // Tab switching with Left/Right (unless in search mode)
            if (!_isSearchMode && !_isOnlineSearchMode) {
                if (TempoInput.GetActionDown(Action.Left) || UnityEngine.Input.GetKeyDown(KeyCode.A)) {
                    if (_currentTab != OverlayTab.Local) {
                        SwitchTab(OverlayTab.Local);
                    }
                    return;
                }
                if (TempoInput.GetActionDown(Action.Right) || UnityEngine.Input.GetKeyDown(KeyCode.D)) {
                    if (_currentTab != OverlayTab.Online) {
                        SwitchTab(OverlayTab.Online);
                    }
                    return;
                }
            }

            if (_currentTab == OverlayTab.Local) {
                HandleLocalInput();
            } else {
                HandleOnlineInput();
            }
        }

        private void SwitchTab(OverlayTab targetTab) {
            _currentTab = targetTab;

            // Reset states
            _isSearchMode = false;
            _isOnlineSearchMode = false;
            _searchQuery = "";
            _onlineSearchQuery = "";
            _downloadStatus = null;

            // Stop audio preview when switching tabs
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            audioManager?.StopPreview();

            // Reload content for the target tab
            if (_currentTab == OverlayTab.Online) {
                LoadOnlineSongs();
            } else {
                // Reload local songs
                var songManager = RiqMenuSystemManager.Instance?.SongManager;
                songManager?.ReloadSongs();
                _selectedSong = 0;
                _scrollOffset = 0;
                _lastPreviewedSong = -1;
            }
        }

        #region Local Songs

        private void HandleLocalInput() {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab)) {
                ToggleSearchMode();
                return;
            }

            if (_isSearchMode) {
                HandleLocalSearchInput();
                return;
            }

            HandleLocalNormalInput(songManager.TotalSongs);
        }

        private void HandleLocalSearchInput() {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            if (TempoInput.GetActionDown<Action>(Action.Confirm)) {
                if (_filteredSongIndices.Count > 0 && _filteredSelectedIndex < _filteredSongIndices.Count) {
                    int actualSongIndex = _filteredSongIndices[_filteredSelectedIndex];
                    OnSongSelected?.Invoke(actualSongIndex);
                    Hide();
                }
                return;
            }

            if (TempoInput.GetActionDown<Action>(Action.Cancel) || UnityEngine.Input.GetKeyDown(KeyCode.Tab)) {
                ExitSearchMode();
                return;
            }

            bool searchUpdated = false;

            if (UnityEngine.Input.GetKeyDown(KeyCode.Backspace)) {
                if (_searchQuery.Length > 0) {
                    _searchQuery = _searchQuery.Substring(0, _searchQuery.Length - 1);
                    searchUpdated = true;
                }
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.A) && (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))) {
                if (_searchQuery.Length > 0) {
                    _searchQuery = "";
                    searchUpdated = true;
                }
            }

            string inputString = UnityEngine.Input.inputString;
            foreach (char c in inputString) {
                if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || c == ' ') {
                    _searchQuery += c;
                    searchUpdated = true;
                }
            }

            if (searchUpdated) {
                UpdateLocalSearchResults();
            }

            if (_filteredSongIndices.Count > 0) {
                if (TempoInput.GetActionDown<Action>(Action.Up)) {
                    _filteredSelectedIndex = Mathf.Max(0, _filteredSelectedIndex - 1);
                    UpdateSelectionFromFiltered();
                    TryPreviewCurrentSong();
                } else if (TempoInput.GetActionDown<Action>(Action.Down)) {
                    _filteredSelectedIndex = Mathf.Min(_filteredSongIndices.Count - 1, _filteredSelectedIndex + 1);
                    UpdateSelectionFromFiltered();
                    TryPreviewCurrentSong();
                }
            }
        }

        private void HandleLocalNormalInput(int totalSongs) {
            if (TempoInput.GetActionDown(Action.Up)) {
                _selectedSong = Mathf.Max(0, _selectedSong - 1);
                UpdateScrollFromSelection();
                TryPreviewCurrentSong();
            } else if (TempoInput.GetActionDown(Action.Down)) {
                _selectedSong = Mathf.Min(totalSongs - 1, _selectedSong + 1);
                UpdateScrollFromSelection();
                TryPreviewCurrentSong();
            } else if (UnityEngine.Input.GetKeyDown(KeyCode.PageUp)) {
                _selectedSong = Mathf.Max(0, _selectedSong - VISIBLE_SONGS);
                UpdateScrollFromSelection();
                TryPreviewCurrentSong();
            } else if (UnityEngine.Input.GetKeyDown(KeyCode.PageDown)) {
                _selectedSong = Mathf.Min(totalSongs - 1, _selectedSong + VISIBLE_SONGS);
                UpdateScrollFromSelection();
                TryPreviewCurrentSong();
            } else if (UnityEngine.Input.GetKeyDown(KeyCode.P)) {
                MixtapeLoaderCustom.autoplay = !MixtapeLoaderCustom.autoplay;
            } else if (TempoInput.GetActionDown(Action.Confirm)) {
                if (_selectedSong < totalSongs) {
                    OnSongSelected?.Invoke(_selectedSong);
                    Hide();
                }
            } else if (TempoInput.GetActionDown<Action>(Action.Cancel)) {
                Hide();
            }
        }

        private void ToggleSearchMode() {
            _isSearchMode = !_isSearchMode;
            if (_isSearchMode) {
                _searchQuery = "";
                _filteredSongIndices.Clear();
                _filteredSelectedIndex = 0;
            }
        }

        private void ExitSearchMode() {
            _isSearchMode = false;
            _searchQuery = "";
            _filteredSongIndices.Clear();
            _filteredSelectedIndex = 0;
        }

        private void UpdateLocalSearchResults() {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            _filteredSongIndices.Clear();
            _filteredSelectedIndex = 0;

            if (string.IsNullOrEmpty(_searchQuery)) return;

            var searchResults = new List<(int index, int score)>();

            for (int i = 0; i < songManager.TotalSongs; i++) {
                var song = songManager.GetSong(i);
                if (song != null) {
                    int score = CalculateFuzzyScore(song.SongTitle.ToLower(), _searchQuery.ToLower());
                    if (score > 0) {
                        searchResults.Add((i, score));
                    }
                }
            }

            _filteredSongIndices = searchResults
                .OrderByDescending(x => x.score)
                .Select(x => x.index)
                .ToList();

            if (_filteredSongIndices.Count > 0) {
                UpdateSelectionFromFiltered();
            }
        }

        private int CalculateFuzzyScore(string text, string query) {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return 0;

            if (text.Contains(query)) {
                return 1000 + (query.Length * 10);
            }

            int score = 0;
            int textIndex = 0;
            int queryIndex = 0;
            int consecutiveMatches = 0;

            while (textIndex < text.Length && queryIndex < query.Length) {
                if (text[textIndex] == query[queryIndex]) {
                    score += 10 + consecutiveMatches;
                    consecutiveMatches++;
                    queryIndex++;
                } else {
                    consecutiveMatches = 0;
                }
                textIndex++;
            }

            if (queryIndex == query.Length) {
                score += 50;
            }

            return score;
        }

        private void UpdateSelectionFromFiltered() {
            if (_filteredSongIndices.Count > 0 && _filteredSelectedIndex < _filteredSongIndices.Count) {
                _selectedSong = _filteredSongIndices[_filteredSelectedIndex];
                UpdateScrollFromSelection();
            }
        }

        private void UpdateScrollFromSelection() {
            if (_selectedSong < _scrollOffset) {
                _scrollOffset = _selectedSong;
            } else if (_selectedSong >= _scrollOffset + VISIBLE_SONGS) {
                _scrollOffset = _selectedSong - VISIBLE_SONGS + 1;
            }
        }

        private void TryPreviewCurrentSong() {
            if (_selectedSong == _lastPreviewedSong) return;

            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;

            if (songManager == null || audioManager == null) return;

            var song = songManager.GetSong(_selectedSong);
            if (song == null) return;

            _lastPreviewedSong = _selectedSong;

            if (audioManager.IsPreviewPlaying) {
                audioManager.StopPreview();
            }

            _isLoadingAudio = true;
            _loadingStartTime = Time.time;
            audioManager.PlayPreview(song);
        }

        #endregion

        #region Online Songs

        private void HandleOnlineInput() {
            // Handle search mode toggle
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab)) {
                _isOnlineSearchMode = !_isOnlineSearchMode;
                if (_isOnlineSearchMode) {
                    _onlineSearchQuery = "";
                }
                return;
            }

            // Handle sort toggle with S
            if (!_isOnlineSearchMode && UnityEngine.Input.GetKeyDown(KeyCode.S)) {
                _onlineSort = _onlineSort == "newest" ? "popular" : "newest";
                LoadOnlineSongs();
                return;
            }

            // Handle refresh with R
            if (!_isOnlineSearchMode && UnityEngine.Input.GetKeyDown(KeyCode.R)) {
                LoadOnlineSongs();
                return;
            }

            if (_isOnlineSearchMode) {
                HandleOnlineSearchInput();
            } else {
                HandleOnlineNormalInput();
            }
        }

        private void HandleOnlineSearchInput() {
            if (TempoInput.GetActionDown<Action>(Action.Cancel) || UnityEngine.Input.GetKeyDown(KeyCode.Tab)) {
                _isOnlineSearchMode = false;
                return;
            }

            if (TempoInput.GetActionDown<Action>(Action.Confirm)) {
                if (!string.IsNullOrEmpty(_onlineSearchQuery)) {
                    SearchOnlineSongs(_onlineSearchQuery);
                    _isOnlineSearchMode = false;
                }
                return;
            }

            // Text input
            if (UnityEngine.Input.GetKeyDown(KeyCode.Backspace)) {
                if (_onlineSearchQuery.Length > 0) {
                    _onlineSearchQuery = _onlineSearchQuery.Substring(0, _onlineSearchQuery.Length - 1);
                }
            }

            string inputString = UnityEngine.Input.inputString;
            foreach (char c in inputString) {
                if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || c == ' ') {
                    _onlineSearchQuery += c;
                }
            }

            // Navigation in results while searching
            if (_onlineSongs.Count > 0) {
                if (TempoInput.GetActionDown<Action>(Action.Up)) {
                    _onlineSelectedIndex = Mathf.Max(0, _onlineSelectedIndex - 1);
                    UpdateOnlineScroll();
                } else if (TempoInput.GetActionDown<Action>(Action.Down)) {
                    _onlineSelectedIndex = Mathf.Min(_onlineSongs.Count - 1, _onlineSelectedIndex + 1);
                    UpdateOnlineScroll();
                }
            }
        }

        private void HandleOnlineNormalInput() {
            if (_onlineSongs.Count == 0) {
                if (TempoInput.GetActionDown<Action>(Action.Cancel)) {
                    Hide();
                }
                return;
            }

            if (TempoInput.GetActionDown(Action.Up)) {
                _onlineSelectedIndex = Mathf.Max(0, _onlineSelectedIndex - 1);
                UpdateOnlineScroll();
            } else if (TempoInput.GetActionDown(Action.Down)) {
                _onlineSelectedIndex = Mathf.Min(_onlineSongs.Count - 1, _onlineSelectedIndex + 1);
                UpdateOnlineScroll();
            } else if (UnityEngine.Input.GetKeyDown(KeyCode.PageUp)) {
                _onlineSelectedIndex = Mathf.Max(0, _onlineSelectedIndex - VISIBLE_SONGS);
                UpdateOnlineScroll();
            } else if (UnityEngine.Input.GetKeyDown(KeyCode.PageDown)) {
                _onlineSelectedIndex = Mathf.Min(_onlineSongs.Count - 1, _onlineSelectedIndex + VISIBLE_SONGS);
                UpdateOnlineScroll();
            } else if (TempoInput.GetActionDown(Action.Confirm)) {
                DownloadSelectedSong();
            } else if (TempoInput.GetActionDown<Action>(Action.Cancel)) {
                Hide();
            }
        }

        private void UpdateOnlineScroll() {
            if (_onlineSelectedIndex < _onlineScrollOffset) {
                _onlineScrollOffset = _onlineSelectedIndex;
            } else if (_onlineSelectedIndex >= _onlineScrollOffset + VISIBLE_SONGS) {
                _onlineScrollOffset = _onlineSelectedIndex - VISIBLE_SONGS + 1;
            }
        }

        private void LoadOnlineSongs() {
            _isOnlineLoading = true;
            _onlineError = null;

            _apiClient.GetSongs(_onlineSort, 1, (songs, error) => {
                _isOnlineLoading = false;
                if (error != null) {
                    _onlineError = error;
                } else {
                    _onlineSongs = songs ?? new List<OnlineSong>();
                    _onlineSelectedIndex = 0;
                    _onlineScrollOffset = 0;
                }
            });
        }

        private void SearchOnlineSongs(string query) {
            _isOnlineLoading = true;
            _onlineError = null;

            _apiClient.SearchSongs(query, (songs, error) => {
                _isOnlineLoading = false;
                if (error != null) {
                    _onlineError = error;
                } else {
                    _onlineSongs = songs ?? new List<OnlineSong>();
                    _onlineSelectedIndex = 0;
                    _onlineScrollOffset = 0;
                }
            });
        }

        private void DownloadSelectedSong() {
            if (_onlineSelectedIndex >= _onlineSongs.Count) return;

            var song = _onlineSongs[_onlineSelectedIndex];
            string songsFolder = GetSongsFolder();

            if (songsFolder == null) {
                _downloadStatus = "Error: Could not find songs folder";
                return;
            }

            _isDownloading = true;
            _downloadProgress = 0f;
            _downloadStatus = $"Downloading {song.Title}...";

            _apiClient.DownloadSong(song, songsFolder,
                (filePath, error) => {
                    _isDownloading = false;
                    if (error != null) {
                        _downloadStatus = $"Error: {error}";
                    } else {
                        _downloadStatus = $"Downloaded! Refresh local songs to play.";
                        // Trigger song reload
                        var songManager = RiqMenuSystemManager.Instance?.SongManager;
                        songManager?.ReloadSongs();
                    }
                },
                (progress) => {
                    _downloadProgress = progress;
                }
            );
        }

        private string GetSongsFolder() {
            string dataPath = Application.dataPath;
            string songsPath = Path.Combine(dataPath, "StreamingAssets", "RiqMenu");
            if (!Directory.Exists(songsPath)) {
                try {
                    Directory.CreateDirectory(songsPath);
                } catch {
                    return null;
                }
            }
            return songsPath;
        }

        #endregion

        #region Drawing

        private void DrawOverlay() {
            // Background
            GUI.color = Color.black;
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.color = Color.white;

            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;
            float boxWidth = 650f;
            float boxHeight = 550f;
            Rect boxRect = new Rect(centerX - boxWidth/2, centerY - boxHeight/2, boxWidth, boxHeight);

            GUI.color = Color.gray;
            GUI.Box(boxRect, "");
            GUI.color = Color.white;

            // Draw tabs
            DrawTabs(boxRect);

            // Draw content based on current tab
            if (_currentTab == OverlayTab.Local) {
                DrawLocalContent(boxRect);
            } else {
                DrawOnlineContent(boxRect);
            }
        }

        private void DrawTabs(Rect boxRect) {
            float tabY = boxRect.y + 5;
            float tabWidth = 100f;
            float tabHeight = 25f;

            // Local tab
            bool isLocalSelected = _currentTab == OverlayTab.Local;
            GUI.color = isLocalSelected ? Color.green : Color.gray;
            GUI.Box(new Rect(boxRect.x + 10, tabY, tabWidth, tabHeight), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(boxRect.x + 10, tabY + 3, tabWidth, tabHeight),
                $"<color={(isLocalSelected ? "white" : "gray")}><b>  Local</b></color>");

            // Online tab
            bool isOnlineSelected = _currentTab == OverlayTab.Online;
            GUI.color = isOnlineSelected ? Color.cyan : Color.gray;
            GUI.Box(new Rect(boxRect.x + 115, tabY, tabWidth, tabHeight), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(boxRect.x + 115, tabY + 3, tabWidth, tabHeight),
                $"<color={(isOnlineSelected ? "white" : "gray")}><b>  Online</b></color>");

            // Tab hint
            GUI.Label(new Rect(boxRect.x + boxRect.width - 130, tabY + 3, 120, tabHeight),
                "<color=gray><size=11>L/R to switch tabs</size></color>");
        }

        private void DrawLocalContent(Rect boxRect) {
            Rect contentRect = new Rect(boxRect.x, boxRect.y + 35, boxRect.width, boxRect.height - 35);

            // Title
            string title = _isSearchMode ? "Local Songs - Search" : "Local Songs";
            GUI.Label(new Rect(contentRect.x + 10, contentRect.y + 5, contentRect.width - 20, 25),
                $"<size=14><color=white><b>{title}</b></color></size>");

            if (_isSearchMode) {
                DrawLocalSearchBox(contentRect);
            }

            DrawLocalSongList(contentRect);
            DrawLocalFooter(contentRect);
        }

        private void DrawLocalSearchBox(Rect contentRect) {
            float searchY = contentRect.y + 30;

            GUI.color = Color.black;
            GUI.Box(new Rect(contentRect.x + 10, searchY, contentRect.width - 20, 22), "");
            GUI.color = Color.white;

            string displayText = _searchQuery + (Time.time % 1f < 0.5f ? "|" : "");
            GUI.Label(new Rect(contentRect.x + 15, searchY + 2, contentRect.width - 30, 20),
                $"<color=white>Search: {displayText}</color>");

            if (!string.IsNullOrEmpty(_searchQuery)) {
                GUI.Label(new Rect(contentRect.x + 10, searchY + 22, contentRect.width - 20, 18),
                    $"<color=yellow><size=11>Found {_filteredSongIndices.Count} songs</size></color>");
            }
        }

        private void DrawLocalSongList(Rect contentRect) {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            float listY = _isSearchMode ? contentRect.y + 75 : contentRect.y + 35;
            float songHeight = 24f;

            List<int> songsToDisplay;
            int totalCount;

            if (_isSearchMode && _filteredSongIndices.Count > 0) {
                songsToDisplay = _filteredSongIndices;
                totalCount = _filteredSongIndices.Count;
            } else if (_isSearchMode) {
                GUI.Label(new Rect(contentRect.x + 10, listY + 20, contentRect.width - 20, 20),
                    "<color=yellow>No results</color>");
                return;
            } else {
                songsToDisplay = Enumerable.Range(0, songManager.TotalSongs).ToList();
                totalCount = songManager.TotalSongs;
            }

            if (totalCount == 0) {
                GUI.Label(new Rect(contentRect.x + 10, listY + 20, contentRect.width - 20, 20),
                    "<color=yellow>No songs found. Add .riq/.bop files to the RiqMenu folder.</color>");
                return;
            }

            // Song count
            int displayIndex = _isSearchMode ? _filteredSelectedIndex : _selectedSong;
            GUI.Label(new Rect(contentRect.x + 10, listY, contentRect.width - 20, 20),
                $"<color=white><size=11>Song {displayIndex + 1} of {totalCount}</size></color>");

            listY += 22;

            int visibleStart = _isSearchMode
                ? Mathf.Max(0, _filteredSelectedIndex - VISIBLE_SONGS / 2)
                : _scrollOffset;
            int visibleEnd = Mathf.Min(visibleStart + VISIBLE_SONGS, songsToDisplay.Count);
            visibleStart = Mathf.Max(0, visibleEnd - VISIBLE_SONGS);

            for (int i = visibleStart; i < visibleEnd; i++) {
                int songIndex = songsToDisplay[i];
                var song = songManager.GetSong(songIndex);
                if (song == null) continue;

                float songY = listY + (i - visibleStart) * songHeight;
                bool isSelected = _isSearchMode ? (i == _filteredSelectedIndex) : (songIndex == _selectedSong);

                if (isSelected) {
                    GUI.color = Color.green;
                    GUI.Box(new Rect(contentRect.x + 15, songY, contentRect.width - 30, songHeight - 2), "");
                    GUI.color = Color.white;
                }

                string displayTitle = song.SongTitle;
                if (displayTitle.Length > 55) displayTitle = displayTitle.Substring(0, 52) + "...";

                string status = song.audioClip != null ? "♪" : "○";
                string statusColor = song.audioClip != null ? "green" : "gray";

                GUI.Label(new Rect(contentRect.x + 20, songY, 18, songHeight),
                    $"<color={statusColor}><size=13>{status}</size></color>");
                GUI.Label(new Rect(contentRect.x + 40, songY, contentRect.width - 60, songHeight),
                    $"<color=white>{displayTitle}</color>");
            }
        }

        private void DrawLocalFooter(Rect contentRect) {
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            float footerY = contentRect.y + contentRect.height - 85;

            // Audio status
            if (_isLoadingAudio) {
                GUI.Label(new Rect(contentRect.x + 10, footerY, contentRect.width - 20, 20),
                    "<color=yellow>Loading audio...</color>");
            } else if (audioManager?.IsPreviewPlaying == true) {
                string previewInfo = audioManager.CurrentPreviewSong?.SongTitle ?? "";
                if (previewInfo.Length > 45) previewInfo = previewInfo.Substring(0, 42) + "...";
                GUI.Label(new Rect(contentRect.x + 10, footerY, contentRect.width - 20, 20),
                    $"<color=yellow>Now Playing: {previewInfo}</color>");
            }

            // Autoplay
            string autoplayStatus = MixtapeLoaderCustom.autoplay ? "ON" : "OFF";
            string autoplayColor = MixtapeLoaderCustom.autoplay ? "green" : "red";
            GUI.Label(new Rect(contentRect.x + 10, footerY + 22, contentRect.width - 20, 20),
                $"<color={autoplayColor}>Autoplay: {autoplayStatus}</color> <color=gray>(P)</color>");

            // Controls
            string controls = _isSearchMode
                ? "Tab: exit search • Enter: play • Backspace: delete"
                : "Tab: search • Enter: play • Esc: close";
            GUI.Label(new Rect(contentRect.x + 10, footerY + 44, contentRect.width - 20, 20),
                $"<color=gray><size=11>{controls}</size></color>");
        }

        private void DrawOnlineContent(Rect boxRect) {
            Rect contentRect = new Rect(boxRect.x, boxRect.y + 35, boxRect.width, boxRect.height - 35);

            // Title with sort
            string sortLabel = _onlineSort == "newest" ? "Newest" : "Popular";
            GUI.Label(new Rect(contentRect.x + 10, contentRect.y + 5, contentRect.width - 20, 25),
                $"<size=14><color=white><b>Online Songs</b></color></size> <color=cyan><size=11>({sortLabel})</size></color>");

            if (_isOnlineSearchMode) {
                DrawOnlineSearchBox(contentRect);
            }

            // Download status
            if (_isDownloading || _downloadStatus != null) {
                DrawDownloadStatus(contentRect);
            }

            DrawOnlineSongList(contentRect);
            DrawOnlineFooter(contentRect);
        }

        private void DrawOnlineSearchBox(Rect contentRect) {
            float searchY = contentRect.y + 30;

            GUI.color = Color.black;
            GUI.Box(new Rect(contentRect.x + 10, searchY, contentRect.width - 20, 22), "");
            GUI.color = Color.white;

            string displayText = _onlineSearchQuery + (Time.time % 1f < 0.5f ? "|" : "");
            GUI.Label(new Rect(contentRect.x + 15, searchY + 2, contentRect.width - 30, 20),
                $"<color=white>Search: {displayText}</color> <color=gray><size=11>(Enter to search)</size></color>");
        }

        private void DrawDownloadStatus(Rect contentRect) {
            float statusY = contentRect.y + (_isOnlineSearchMode ? 55 : 30);

            if (_isDownloading) {
                int progressPercent = Mathf.RoundToInt(_downloadProgress * 100);
                GUI.Label(new Rect(contentRect.x + 10, statusY, contentRect.width - 20, 18),
                    $"<color=cyan>Downloading... {progressPercent}%</color>");
            } else if (_downloadStatus != null) {
                string color = _downloadStatus.StartsWith("Error") ? "red" : "green";
                GUI.Label(new Rect(contentRect.x + 10, statusY, contentRect.width - 20, 18),
                    $"<color={color}>{_downloadStatus}</color>");
            }
        }

        private void DrawOnlineSongList(Rect contentRect) {
            float listY = contentRect.y + 55;
            if (_isOnlineSearchMode) listY += 25;
            if (_isDownloading || _downloadStatus != null) listY += 20;

            float songHeight = 42f;

            if (_isOnlineLoading) {
                GUI.Label(new Rect(contentRect.x + 10, listY + 20, contentRect.width - 20, 20),
                    "<color=cyan>Loading...</color>");
                return;
            }

            if (_onlineError != null) {
                GUI.Label(new Rect(contentRect.x + 10, listY + 20, contentRect.width - 20, 20),
                    $"<color=red>Error: {_onlineError}</color>");
                return;
            }

            if (_onlineSongs.Count == 0) {
                GUI.Label(new Rect(contentRect.x + 10, listY + 20, contentRect.width - 20, 20),
                    "<color=yellow>No songs found</color>");
                return;
            }

            // Song count
            GUI.Label(new Rect(contentRect.x + 10, listY, contentRect.width - 20, 18),
                $"<color=white><size=11>Song {_onlineSelectedIndex + 1} of {_onlineSongs.Count}</size></color>");

            listY += 20;

            int visibleCount = 7;
            int visibleStart = _onlineScrollOffset;
            int visibleEnd = Mathf.Min(visibleStart + visibleCount, _onlineSongs.Count);

            for (int i = visibleStart; i < visibleEnd; i++) {
                var song = _onlineSongs[i];
                float songY = listY + (i - visibleStart) * songHeight;
                bool isSelected = i == _onlineSelectedIndex;

                if (isSelected) {
                    GUI.color = Color.cyan;
                    GUI.Box(new Rect(contentRect.x + 15, songY, contentRect.width - 30, songHeight - 4), "");
                    GUI.color = Color.white;
                }

                // File type badge
                string fileType = (song.FileType ?? "riq").ToUpper();
                string badgeColor = fileType == "BOP" ? "#ff9900" : "#00aaff";
                GUI.Label(new Rect(contentRect.x + 20, songY + 6, 35, 18),
                    $"<color={badgeColor}><size=11><b>{fileType}</b></size></color>");

                string displayTitle = song.DisplayTitle;
                if (displayTitle.Length > 45) displayTitle = displayTitle.Substring(0, 42) + "...";

                // Title (offset for badge)
                GUI.Label(new Rect(contentRect.x + 55, songY + 4, contentRect.width - 75, 20),
                    $"<color=white>{displayTitle}</color>");

                // Metadata line - build dynamically to avoid empty bullets
                string creator = song.Creator ?? song.UploaderName ?? "Unknown";
                var metaParts = new List<string> { $"by {creator}", song.FileSizeDisplay };
                if (song.Bpm.HasValue) metaParts.Add($"{song.Bpm:F0} BPM");
                metaParts.Add($"{song.DownloadCount} DLs");
                string meta = string.Join(" • ", metaParts);

                GUI.Label(new Rect(contentRect.x + 20, songY + 22, contentRect.width - 40, 18),
                    $"<color=gray><size=11>{meta}</size></color>");
            }
        }

        private void DrawOnlineFooter(Rect contentRect) {
            float footerY = contentRect.y + contentRect.height - 55;

            string controls = _isOnlineSearchMode
                ? "Tab: exit search • Enter: search online"
                : "Tab: search • Enter: download • S: sort • R: refresh • Esc: close";

            GUI.Label(new Rect(contentRect.x + 10, footerY, contentRect.width - 20, 20),
                $"<color=gray><size=11>{controls}</size></color>");

            GUI.Label(new Rect(contentRect.x + 10, footerY + 20, contentRect.width - 20, 20),
                "<color=gray><size=10>Songs from riqs.kabir.au</size></color>");
        }

        #endregion

        #region Public Methods

        public void Show() {
            if (_isVisible) return;

            _isVisible = true;
            _selectedSong = 0;
            _scrollOffset = 0;
            _lastPreviewedSong = -1;
            _showTime = Time.time;

            _isSearchMode = false;
            _isOnlineSearchMode = false;
            _searchQuery = "";
            _onlineSearchQuery = "";
            _downloadStatus = null;

            _currentTab = OverlayTab.Local;

            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.BlockInput();

            OnOverlayOpened?.Invoke();
            TryPreviewCurrentSong();
        }

        public void Hide() {
            if (!_isVisible) return;

            _isVisible = false;
            _lastPreviewedSong = -1;

            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.UnblockInput();

            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            audioManager?.StopPreview();

            OnOverlayClosed?.Invoke();
        }

        public void Toggle() {
            if (_isVisible) Hide();
            else Show();
        }

        public void NavigateToSong(int songIndex) {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            if (songIndex >= 0 && songIndex < songManager.TotalSongs) {
                _selectedSong = songIndex;
                UpdateScrollFromSelection();
            }
        }

        public int GetSelectedSongIndex() => _selectedSong;

        #endregion
    }
}
