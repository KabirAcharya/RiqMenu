using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RiqMenu.Core;

namespace RiqMenu.UI
{
    /// <summary>
    /// Fullscreen overlay for custom song selection and preview
    /// </summary>
    public class SongsOverlay : MonoBehaviour {
        private bool _isVisible = false;
        private int _selectedSong = 0;
        private int _scrollOffset = 0;
        private int _lastPreviewedSong = -1;
        private float _showTime = 0f;
        private bool _isLoadingAudio = false;
        private float _loadingStartTime = 0f;

        // Search functionality
        private bool _isSearchMode = false;
        private string _searchQuery = "";
        private List<int> _filteredSongIndices = new List<int>();
        private int _filteredSelectedIndex = 0;

        private const int VISIBLE_SONGS = 10;
        private const float INPUT_DELAY = 0.2f;

        public bool IsVisible => _isVisible;

        public event System.Action OnOverlayOpened;
        public event System.Action OnOverlayClosed;
        public event System.Action<int> OnSongSelected;

        private void Update() {
            if (!_isVisible) return;
            // If we were loading and playback started, clear loading indicator
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            if (_isLoadingAudio && audioManager != null && audioManager.IsPreviewPlaying) {
                _isLoadingAudio = false;
            }
            // Timeout stuck loading indicators after a short grace period
            if (_isLoadingAudio && Time.time - _loadingStartTime > 3f) {
                _isLoadingAudio = false;
            }
            HandleInput();
        }

        /// <summary>
        /// Called by UIManager during OnGUI to render the overlay
        /// </summary>
        public void DrawOverlayGUI() {
            if (!_isVisible) return;
            DrawOverlay();
        }

        private void HandleInput() {
            // Prevent immediate input after showing overlay
            if (Time.time - _showTime < INPUT_DELAY) return;

            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            // Handle Tab key to toggle search mode
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab)) {
                ToggleSearchMode();
                return;
            }

            // Handle search mode input
            if (_isSearchMode) {
                HandleSearchInput();
                return;
            }

            // Handle normal navigation
            HandleNormalInput(songManager.TotalSongs);
        }

        private void HandleSearchInput() {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            // Play selected song with Confirm action
            if (TempoInput.GetActionDown<global::Action>(global::Action.Confirm)) {
                if (_filteredSongIndices.Count > 0 && _filteredSelectedIndex < _filteredSongIndices.Count) {
                    int actualSongIndex = _filteredSongIndices[_filteredSelectedIndex];
                    OnSongSelected?.Invoke(actualSongIndex);
                    Hide();
                }
                return;
            }

            // Exit search mode with Cancel action or Tab
            if (TempoInput.GetActionDown<global::Action>(global::Action.Cancel) || UnityEngine.Input.GetKeyDown(KeyCode.Tab)) {
                ExitSearchMode();
                return;
            }

            // Handle text input for search
            bool searchUpdated = false;

            // Handle backspace
            if (UnityEngine.Input.GetKeyDown(KeyCode.Backspace)) {
                if (_searchQuery.Length > 0) {
                    _searchQuery = _searchQuery.Substring(0, _searchQuery.Length - 1);
                    searchUpdated = true;
                }
            }

            // Handle Ctrl+A to clear search
            if (UnityEngine.Input.GetKeyDown(KeyCode.A) && (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))) {
                if (_searchQuery.Length > 0) {
                    _searchQuery = "";
                    searchUpdated = true;
                }
            }

            // Handle character input
            string inputString = UnityEngine.Input.inputString;
            foreach (char c in inputString) {
                if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || c == ' ') {
                    _searchQuery += c;
                    searchUpdated = true;
                }
            }

            // Update search results if query changed
            if (searchUpdated) {
                UpdateSearchResults();
            }

            // Handle navigation in search results
            if (_filteredSongIndices.Count > 0) {
                if (TempoInput.GetActionDown<global::Action>(global::Action.Up)) {
                    _filteredSelectedIndex = Mathf.Max(0, _filteredSelectedIndex - 1);
                    UpdateSelectionFromFiltered();
                    TryPreviewCurrentSong();
                } else if (TempoInput.GetActionDown<global::Action>(global::Action.Down)) {
                    _filteredSelectedIndex = Mathf.Min(_filteredSongIndices.Count - 1, _filteredSelectedIndex + 1);
                    UpdateSelectionFromFiltered();
                    TryPreviewCurrentSong();
                }
            }
        }

        private void HandleNormalInput(int totalSongs) {
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
                // Toggle autoplay
                MixtapeLoaderCustom.autoplay = !MixtapeLoaderCustom.autoplay;
                Debug.Log($"[SongsOverlay] Autoplay toggled: {MixtapeLoaderCustom.autoplay}");
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
                Debug.Log("[SongsOverlay] Search mode activated");
            } else {
                Debug.Log("[SongsOverlay] Search mode deactivated");
            }
        }

        private void ExitSearchMode() {
            _isSearchMode = false;
            _searchQuery = "";
            _filteredSongIndices.Clear();
            _filteredSelectedIndex = 0;
            Debug.Log("[SongsOverlay] Exited search mode");
        }

        private void UpdateSearchResults() {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            _filteredSongIndices.Clear();
            _filteredSelectedIndex = 0;

            if (string.IsNullOrEmpty(_searchQuery)) {
                return;
            }

            // Perform fuzzy search
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

            // Sort by score (higher is better) and take the results
            _filteredSongIndices = searchResults
                .OrderByDescending(x => x.score)
                .Select(x => x.index)
                .ToList();

            // Update selection to first result if available
            if (_filteredSongIndices.Count > 0) {
                UpdateSelectionFromFiltered();
            }
        }

        private int CalculateFuzzyScore(string text, string query) {
            if (string.IsNullOrEmpty(query)) return 0;
            if (string.IsNullOrEmpty(text)) return 0;

            // Exact match gets highest score
            if (text.Contains(query)) {
                return 1000 + (query.Length * 10);
            }

            // Fuzzy matching - characters in order
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

            // Bonus for matching all characters
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
            // Only preview if we've changed songs
            if (_selectedSong == _lastPreviewedSong) return;

            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;

            if (songManager == null || audioManager == null) return;

            var song = songManager.GetSong(_selectedSong);
            if (song == null) return;

            _lastPreviewedSong = _selectedSong;

            // Only stop preview if one is currently playing
            if (audioManager.IsPreviewPlaying) {
                audioManager.StopPreview();
            }

            // Delegate to AudioManager: stream preview on-demand
            _isLoadingAudio = true;
            _loadingStartTime = Time.time;
            audioManager.PlayPreview(song);
        }

        private void DrawOverlay() {
            GUI.color = Color.black;
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.color = Color.white;

            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;
            float boxWidth = 600f;
            float boxHeight = 500f;
            Rect boxRect = new Rect(centerX - boxWidth/2, centerY - boxHeight/2, boxWidth, boxHeight);

            GUI.color = Color.gray;
            GUI.Box(boxRect, "");
            GUI.color = Color.white;

            // Title with search mode indicator
            string title = _isSearchMode ? "RiqMenu - Search Mode" : "RiqMenu - Custom Songs";
            GUI.Label(new Rect(boxRect.x + 10, boxRect.y + 10, boxRect.width - 20, 30),
                $"<size=16><color=white><b>{title}</b></color></size>");

            // Draw search box if in search mode
            if (_isSearchMode) {
                DrawSearchBox(boxRect);
            }

            DrawSongList(boxRect);
            DrawFooterInfo(boxRect);
        }

        private void DrawSearchBox(Rect boxRect) {
            float searchY = boxRect.y + 40;
            float searchHeight = 25f;

            // Search box background
            GUI.color = Color.black;
            GUI.Box(new Rect(boxRect.x + 10, searchY, boxRect.width - 20, searchHeight), "");
            GUI.color = Color.white;

            // Search text with cursor
            string displayText = _searchQuery;
            if (Time.time % 1f < 0.5f) { // Blinking cursor
                displayText += "|";
            }

            GUI.Label(new Rect(boxRect.x + 15, searchY + 3, boxRect.width - 30, searchHeight),
                $"<color=white>Search: {displayText}</color>");

            // Search results count
            if (_isSearchMode && !string.IsNullOrEmpty(_searchQuery)) {
                GUI.Label(new Rect(boxRect.x + 10, searchY + 25, boxRect.width - 20, 20),
                    $"<color=yellow>Found {_filteredSongIndices.Count} songs</color>");
            }
        }

        private void DrawSongList(Rect boxRect) {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            // Adjust list position if in search mode
            float listY = _isSearchMode ? boxRect.y + 90 : boxRect.y + 50;
            float songHeight = 25f;

            // Get the list of songs to display
            List<int> songsToDisplay;
            int totalDisplayCount;

            if (_isSearchMode && _filteredSongIndices.Count > 0) {
                songsToDisplay = _filteredSongIndices;
                totalDisplayCount = _filteredSongIndices.Count;

                GUI.Label(new Rect(boxRect.x + 10, listY, boxRect.width - 20, 20),
                    $"<color=white>Result {_filteredSelectedIndex + 1} of {totalDisplayCount} • ↑/↓ to navigate • Enter to play</color>");
            } else if (_isSearchMode) {
                // No search results
                GUI.Label(new Rect(boxRect.x + 10, listY, boxRect.width - 20, 20),
                    $"<color=yellow>No results found</color>");
                return;
            } else {
                // Normal mode - show all songs
                songsToDisplay = Enumerable.Range(0, songManager.TotalSongs).ToList();
                totalDisplayCount = songManager.TotalSongs;

                GUI.Label(new Rect(boxRect.x + 10, listY, boxRect.width - 20, 20),
                    $"<color=white>Song {_selectedSong + 1} of {totalDisplayCount} • ↑/↓ to navigate • Enter to play</color>");
            }

            listY += 30;

            // Calculate visible range
            int visibleStart, visibleEnd;
            if (_isSearchMode) {
                visibleStart = Mathf.Max(0, _filteredSelectedIndex - VISIBLE_SONGS / 2);
                visibleEnd = Mathf.Min(visibleStart + VISIBLE_SONGS, songsToDisplay.Count);
                visibleStart = Mathf.Max(0, visibleEnd - VISIBLE_SONGS);
            } else {
                visibleStart = _scrollOffset;
                visibleEnd = Mathf.Min(visibleStart + VISIBLE_SONGS, songsToDisplay.Count);
            }

            for (int i = visibleStart; i < visibleEnd; i++) {
                int songIndex = songsToDisplay[i];
                var song = songManager.GetSong(songIndex);
                if (song == null) continue;

                float songY = listY + (i - visibleStart) * songHeight;
                Rect songRect = new Rect(boxRect.x + 20, songY, boxRect.width - 40, songHeight - 2);

                // Highlight selected song
                bool isSelected = _isSearchMode ? (i == _filteredSelectedIndex) : (songIndex == _selectedSong);
                if (isSelected) {
                    GUI.color = Color.green;
                    GUI.Box(new Rect(songRect.x - 5, songRect.y, songRect.width + 10, songRect.height), "");
                    GUI.color = Color.white;
                }

                string displayTitle = song.SongTitle;
                if (displayTitle.Length > 50) {
                    displayTitle = displayTitle.Substring(0, 47) + "...";
                }

                // Highlight search matches
                if (_isSearchMode && !string.IsNullOrEmpty(_searchQuery)) {
                    displayTitle = HighlightSearchMatch(displayTitle, _searchQuery);
                }

                string status = song.audioClip != null ? "♪" : "○";
                string statusColor = song.audioClip != null ? "green" : "gray";

                GUI.Label(new Rect(songRect.x, songRect.y, 20, songRect.height),
                    $"<color={statusColor}><size=14>{status}</size></color>");

                GUI.Label(new Rect(songRect.x + 25, songRect.y, songRect.width - 25, songRect.height),
                    $"<color=white>{displayTitle}</color>");
            }

            if (_scrollOffset > 0) {
                GUI.Label(new Rect(boxRect.x + boxRect.width - 30, listY - 25, 20, 20),
                    "<color=yellow>▲</color>");
            }
            if (visibleEnd < totalDisplayCount) {
                GUI.Label(new Rect(boxRect.x + boxRect.width - 30, listY + VISIBLE_SONGS * songHeight, 20, 20),
                    "<color=yellow>▼</color>");
            }
        }

        private string HighlightSearchMatch(string text, string query) {
            if (string.IsNullOrEmpty(query)) return text;

            // Simple highlighting - wrap matched substring in yellow color
            int index = text.ToLower().IndexOf(query.ToLower());
            if (index >= 0) {
                string before = text.Substring(0, index);
                string match = text.Substring(index, query.Length);
                string after = text.Substring(index + query.Length);
                return $"{before}<color=yellow>{match}</color>{after}";
            }
            return text;
        }

        private void DrawFooterInfo(Rect boxRect) {
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;

            // Audio status line
            if (_isLoadingAudio) {
                GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 70, boxRect.width - 20, 20),
                    "<color=yellow>⌛ Loading audio...</color>");
            } else if (audioManager != null && audioManager.IsPreviewPlaying) {
                string previewInfo = audioManager.CurrentPreviewSong?.SongTitle ?? "Unknown";
                if (previewInfo.Length > 40) {
                    previewInfo = previewInfo.Substring(0, 37) + "...";
                }
                GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 70, boxRect.width - 20, 20),
                    $"<color=yellow>♪ Now Playing: {previewInfo}</color>");
            } else {
                GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 70, boxRect.width - 20, 20),
                    "<color=gray>Select a song to preview audio</color>");
            }

            // Autoplay status line
            string autoplayStatus = MixtapeLoaderCustom.autoplay ? "ON" : "OFF";
            string autoplayColor = MixtapeLoaderCustom.autoplay ? "green" : "red";
            GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 50, boxRect.width - 20, 20),
                     $"<color={autoplayColor}>Autoplay: {autoplayStatus}</color> <color=gray>(P to toggle)</color>");

            // Controls help line
            if (_isSearchMode) {
                GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 30, boxRect.width - 20, 20),
                         "<color=gray><size=12>Tab/Esc to exit • Enter to play • Backspace to delete • Ctrl+A to clear</size></color>");
            } else {
                GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 30, boxRect.width - 20, 20),
                         "<color=gray><size=12>F1 to toggle • Esc to close • Enter to play • Tab to search • F2 to stop audio</size></color>");
            }
        }

        public void Show() {
            if (_isVisible) return;

            _isVisible = true;
            _selectedSong = 0;
            _scrollOffset = 0;
            _lastPreviewedSong = -1;
            _showTime = Time.time;

            // Reset search state
            _isSearchMode = false;
            _searchQuery = "";
            _filteredSongIndices.Clear();
            _filteredSelectedIndex = 0;

            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.BlockInput();

            OnOverlayOpened?.Invoke();
            TryPreviewCurrentSong();
        }

        public void Hide() {
            if (!_isVisible) return;

            _isVisible = false;
            _lastPreviewedSong = -1;

            // Reset search state
            _isSearchMode = false;
            _searchQuery = "";
            _filteredSongIndices.Clear();
            _filteredSelectedIndex = 0;

            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.UnblockInput();

            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            audioManager?.StopPreview();

            OnOverlayClosed?.Invoke();
        }

        public void Toggle() {
            if (_isVisible) {
                Hide();
            } else {
                Show();
            }
        }

        public void NavigateToSong(int songIndex) {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;

            if (songIndex >= 0 && songIndex < songManager.TotalSongs) {
                _selectedSong = songIndex;
                UpdateScrollFromSelection();
            }
        }

        public int GetSelectedSongIndex() {
            return _selectedSong;
        }
    }
}
