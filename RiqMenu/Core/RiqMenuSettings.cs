using UnityEngine;

namespace RiqMenu.Core
{
    /// <summary>
    /// Auto-restart mode for gameplay
    /// </summary>
    public enum AutoRestartMode
    {
        Off = 0,
        OnMiss = 1,
        OnNonPerfect = 2
    }

    /// <summary>
    /// Persistent settings for RiqMenu features
    /// </summary>
    public static class RiqMenuSettings
    {
        private const string ACCURACY_BAR_KEY = "RiqMenu_AccuracyBarEnabled";
        private const string AUTO_RESTART_KEY = "RiqMenu_AutoRestartMode";
        private const string PROGRESS_BAR_KEY = "RiqMenu_ProgressBarEnabled";

        private static bool? _accuracyBarEnabled;
        private static AutoRestartMode? _autoRestartMode;
        private static bool? _progressBarEnabled;

        /// <summary>
        /// Whether the accuracy bar is enabled during gameplay. Off by default.
        /// </summary>
        public static bool AccuracyBarEnabled
        {
            get
            {
                if (!_accuracyBarEnabled.HasValue)
                {
                    _accuracyBarEnabled = PlayerPrefs.GetInt(ACCURACY_BAR_KEY, 0) == 1;
                }
                return _accuracyBarEnabled.Value;
            }
            set
            {
                _accuracyBarEnabled = value;
                PlayerPrefs.SetInt(ACCURACY_BAR_KEY, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Auto-restart mode. Off by default.
        /// </summary>
        public static AutoRestartMode AutoRestartMode
        {
            get
            {
                if (!_autoRestartMode.HasValue)
                {
                    _autoRestartMode = (AutoRestartMode)PlayerPrefs.GetInt(AUTO_RESTART_KEY, 0);
                }
                return _autoRestartMode.Value;
            }
            set
            {
                _autoRestartMode = value;
                PlayerPrefs.SetInt(AUTO_RESTART_KEY, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Cycle to the next auto-restart mode
        /// </summary>
        public static AutoRestartMode CycleAutoRestartMode()
        {
            var current = AutoRestartMode;
            var next = (AutoRestartMode)(((int)current + 1) % 3);
            AutoRestartMode = next;
            return next;
        }

        /// <summary>
        /// Whether the progress bar is enabled during gameplay. Off by default.
        /// </summary>
        public static bool ProgressBarEnabled
        {
            get
            {
                if (!_progressBarEnabled.HasValue)
                {
                    _progressBarEnabled = PlayerPrefs.GetInt(PROGRESS_BAR_KEY, 0) == 1;
                }
                return _progressBarEnabled.Value;
            }
            set
            {
                _progressBarEnabled = value;
                PlayerPrefs.SetInt(PROGRESS_BAR_KEY, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
