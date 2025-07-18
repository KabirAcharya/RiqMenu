using System;
using UnityEngine;
using RiqMenu.Core;

namespace RiqMenu.UI
{
    /// <summary>
    /// Fullscreen overlay for custom song selection, styled like the cache loading screen
    /// </summary>
    public class SongsOverlay : MonoBehaviour
    {
        private bool _isVisible = false;
        private int _selectedSong = 0;
        private int _scrollOffset = 0;
        private int _lastPreviewedSong = -1;
        private float _showTime = 0f;
        private bool _isLoadingAudio = false;
        
        private const int VISIBLE_SONGS = 10;
        private const float INPUT_DELAY = 0.2f;
        
        public bool IsVisible => _isVisible;
        
        public event System.Action OnOverlayOpened;
        public event System.Action OnOverlayClosed;
        public event System.Action<int> OnSongSelected;
        
        private void Update()
        {
            if (!_isVisible) return;
            HandleInput();
        }

        /// <summary>
        /// Called by UIManager during OnGUI to render the overlay
        /// </summary>
        public void DrawOverlayGUI()
        {
            if (!_isVisible) return;
            DrawOverlay();
        }

        private void HandleInput()
        {
            // Prevent immediate input after showing overlay
            if (Time.time - _showTime < INPUT_DELAY) return;
            
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;
            
            int totalSongs = songManager.TotalSongs;
            
            if (UnityEngine.Input.GetKeyDown(KeyCode.UpArrow) || UnityEngine.Input.GetKeyDown(KeyCode.W))
            {
                _selectedSong = Mathf.Max(0, _selectedSong - 1);
                UpdateScrollFromSelection();
                TryPreviewCurrentSong();
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.DownArrow) || UnityEngine.Input.GetKeyDown(KeyCode.S))
            {
                _selectedSong = Mathf.Min(totalSongs - 1, _selectedSong + 1);
                UpdateScrollFromSelection();
                TryPreviewCurrentSong();
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.PageUp))
            {
                _selectedSong = Mathf.Max(0, _selectedSong - VISIBLE_SONGS);
                UpdateScrollFromSelection();
                TryPreviewCurrentSong();
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.PageDown))
            {
                _selectedSong = Mathf.Min(totalSongs - 1, _selectedSong + VISIBLE_SONGS);
                UpdateScrollFromSelection();
                TryPreviewCurrentSong();
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.P))
            {
                // Toggle autoplay
                MixtapeLoaderCustom.autoplay = !MixtapeLoaderCustom.autoplay;
                Debug.Log($"[SongsOverlay] Autoplay toggled: {MixtapeLoaderCustom.autoplay}");
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.Space))
            {
                if (_selectedSong < totalSongs)
                {
                    OnSongSelected?.Invoke(_selectedSong);
                    Hide();
                }
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
            }
        }

        private void UpdateScrollFromSelection()
        {
            if (_selectedSong < _scrollOffset)
            {
                _scrollOffset = _selectedSong;
            }
            else if (_selectedSong >= _scrollOffset + VISIBLE_SONGS)
            {
                _scrollOffset = _selectedSong - VISIBLE_SONGS + 1;
            }
        }

        private void TryPreviewCurrentSong()
        {
            // Only preview if we've changed songs
            if (_selectedSong == _lastPreviewedSong) return;
            
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            var audioPreloader = RiqMenuSystemManager.Instance?.AudioPreloader;
            
            if (songManager == null || audioManager == null || audioPreloader == null) return;
            
            var song = songManager.GetSong(_selectedSong);
            if (song == null) return;
            
            _lastPreviewedSong = _selectedSong;
            
            // Only stop preview if one is currently playing
            if (audioManager.IsPreviewPlaying)
            {
                audioManager.StopPreview();
            }
            
            // Check if song is loaded in RAM for instant playback
            if (song.audioClip != null)
            {
                Debug.Log($"[SongsOverlay] Playing {song.SongTitle} from RAM");
                audioManager.PlayPreview(song);
                _isLoadingAudio = false;
            }
            else if (audioPreloader.IsPreloading)
            {
                // Preload still in progress
                _isLoadingAudio = true;
            }
            else
            {
                // Preload complete but song not in RAM
                _isLoadingAudio = false;
                Debug.LogWarning($"[SongsOverlay] Song {song.SongTitle} not found in RAM");
            }
        }

        private void DrawOverlay()
        {
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
            
            GUI.Label(new Rect(boxRect.x + 10, boxRect.y + 10, boxRect.width - 20, 30), 
                "<size=16><color=white><b>RiqMenu - Custom Songs</b></color></size>");
            
            DrawSongList(boxRect);
            DrawFooterInfo(boxRect);
        }

        private void DrawSongList(Rect boxRect)
        {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;
            
            int totalSongs = songManager.TotalSongs;
            float listY = boxRect.y + 50;
            float songHeight = 25f;
            
            GUI.Label(new Rect(boxRect.x + 10, listY, boxRect.width - 20, 20),
                $"<color=white>Song {_selectedSong + 1} of {totalSongs} • ↑/↓ to navigate • Enter to play</color>");
            
            listY += 30;
            int visibleStart = _scrollOffset;
            int visibleEnd = Mathf.Min(visibleStart + VISIBLE_SONGS, totalSongs);
            
            for (int i = visibleStart; i < visibleEnd; i++)
            {
                var song = songManager.GetSong(i);
                if (song == null) continue;
                
                float songY = listY + (i - visibleStart) * songHeight;
                Rect songRect = new Rect(boxRect.x + 20, songY, boxRect.width - 40, songHeight - 2);
                
                if (i == _selectedSong)
                {
                    GUI.color = Color.green;
                    GUI.Box(new Rect(songRect.x - 5, songRect.y, songRect.width + 10, songRect.height), "");
                    GUI.color = Color.white;
                }
                
                string displayTitle = song.SongTitle;
                if (displayTitle.Length > 50)
                {
                    displayTitle = displayTitle.Substring(0, 47) + "...";
                }
                
                string status = song.audioClip != null ? "♪" : "○";
                string statusColor = song.audioClip != null ? "green" : "gray";
                
                GUI.Label(new Rect(songRect.x, songRect.y, 20, songRect.height),
                    $"<color={statusColor}><size=14>{status}</size></color>");
                
                GUI.Label(new Rect(songRect.x + 25, songRect.y, songRect.width - 25, songRect.height),
                    $"<color=white>{displayTitle}</color>");
            }
            
            if (_scrollOffset > 0)
            {
                GUI.Label(new Rect(boxRect.x + boxRect.width - 30, listY - 25, 20, 20),
                    "<color=yellow>▲</color>");
            }
            if (visibleEnd < totalSongs)
            {
                GUI.Label(new Rect(boxRect.x + boxRect.width - 30, listY + VISIBLE_SONGS * songHeight, 20, 20),
                    "<color=yellow>▼</color>");
            }
        }

        private void DrawFooterInfo(Rect boxRect)
        {
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            
            // Audio status line
            if (_isLoadingAudio)
            {
                GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 70, boxRect.width - 20, 20),
                    "<color=yellow>⌛ Loading audio...</color>");
            }
            else if (audioManager != null && audioManager.IsPreviewPlaying)
            {
                string previewInfo = audioManager.CurrentPreviewSong?.SongTitle ?? "Unknown";
                if (previewInfo.Length > 40)
                {
                    previewInfo = previewInfo.Substring(0, 37) + "...";
                }
                GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 70, boxRect.width - 20, 20),
                    $"<color=yellow>♪ Now Playing: {previewInfo}</color>");
            }
            else
            {
                GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 70, boxRect.width - 20, 20),
                    "<color=gray>Select a song to preview audio</color>");
            }
            
            // Autoplay status line
            string autoplayStatus = MixtapeLoaderCustom.autoplay ? "ON" : "OFF";
            string autoplayColor = MixtapeLoaderCustom.autoplay ? "green" : "red";
            GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 50, boxRect.width - 20, 20),
                     $"<color={autoplayColor}>Autoplay: {autoplayStatus}</color> <color=gray>(P to toggle)</color>");
            
            // Controls help line
            GUI.Label(new Rect(boxRect.x + 10, boxRect.y + boxRect.height - 30, boxRect.width - 20, 20), 
                     "<color=gray><size=12>F1 to toggle • Esc to close • Enter to play • F2 to stop audio</size></color>");
        }

        public void Show()
        {
            if (_isVisible) return;
            
            _isVisible = true;
            _selectedSong = 0;
            _scrollOffset = 0;
            _lastPreviewedSong = -1;
            _showTime = Time.time;
            
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.BlockInput();
            
            OnOverlayOpened?.Invoke();
            TryPreviewCurrentSong();
        }

        public void Hide()
        {
            if (!_isVisible) return;
            
            _isVisible = false;
            _lastPreviewedSong = -1;
            
            var inputManager = RiqMenuSystemManager.Instance?.InputManager;
            inputManager?.UnblockInput();
            
            var audioManager = RiqMenuSystemManager.Instance?.AudioManager;
            audioManager?.StopPreview();
            
            OnOverlayClosed?.Invoke();
        }

        public void Toggle()
        {
            if (_isVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void NavigateToSong(int songIndex)
        {
            var songManager = RiqMenuSystemManager.Instance?.SongManager;
            if (songManager == null) return;
            
            if (songIndex >= 0 && songIndex < songManager.TotalSongs)
            {
                _selectedSong = songIndex;
                UpdateScrollFromSelection();
            }
        }

        public int GetSelectedSongIndex()
        {
            return _selectedSong;
        }
    }
}