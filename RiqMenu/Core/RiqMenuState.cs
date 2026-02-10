using UnityEngine.SceneManagement;

namespace RiqMenu.Core
{
    /// <summary>
    /// Shared state accessed by patches and systems across the mod.
    /// </summary>
    public static class RiqMenuState
    {
        public static Scene CurrentScene { get; set; }

        public static bool LaunchedFromRiqMenu { get; set; } = false;
        public static string LastLoadedSongPath { get; set; } = null;

        public static bool IsQuitting { get; set; } = false;
        public static bool IsTransitioning { get; set; } = false;

        public static bool IsOverlayVisible() {
            return RiqMenuSystemManager.Instance?.UIManager?.Overlay?.IsVisible ?? false;
        }

        public static bool IsInGameEditor() {
            try {
                string sceneName = CurrentScene.name;
                if (string.IsNullOrEmpty(sceneName)) return false;
                return sceneName.Contains("Editor") || sceneName.Contains("editor");
            } catch {
                return false;
            }
        }

        public static bool IsMenuScene(string sceneName) {
            return sceneName == SceneKey.TitleScreen.ToString() ||
                   sceneName == SceneKey.StageSelect.ToString() ||
                   sceneName == "StageSelectDemo" ||
                   sceneName == "Postcard" ||
                   sceneName == "Credits";
        }
    }
}
