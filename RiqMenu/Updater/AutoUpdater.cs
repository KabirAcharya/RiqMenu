using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using UnityEngine;

namespace RiqMenu.Updater
{
    /// <summary>
    /// Checks for RiqMenu updates on startup and auto-updates if a newer version is available.
    /// </summary>
    public static class AutoUpdater
    {
        private const string GitHubRepo = "KabirAcharya/RiqMenu";

        // Set to true to force update regardless of version (for testing)
        private const bool FORCE_UPDATE = false;

        private static ManualLogSource _logger;

        /// <summary>
        /// Check for updates and restart if an update is downloaded.
        /// Call this early in Awake().
        /// </summary>
        public static void CheckAndUpdate(ManualLogSource logger)
        {
            _logger = logger;

            try
            {
                _logger.LogInfo("Checking for updates...");
                DoUpdateCheck();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Update check failed: {ex.Message}");
            }
        }

        private static void DoUpdateCheck()
        {
            // Get current version
            System.Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            _logger.LogInfo($"Current version: {currentVersion}");

            // Check GitHub for latest version
            var (latestVersion, downloadUrl) = GetLatestRelease();
            if (latestVersion == null || downloadUrl == null)
            {
                _logger.LogInfo("Could not fetch latest release info");
                return;
            }

            _logger.LogInfo($"Latest version: {latestVersion}");

            // Compare versions
            if (!FORCE_UPDATE && latestVersion <= currentVersion)
            {
                _logger.LogInfo("RiqMenu is up to date");
                return;
            }

            if (FORCE_UPDATE)
            {
                _logger.LogWarning("FORCE_UPDATE enabled - forcing update");
            }

            _logger.LogInfo($"Update available: {currentVersion} -> {latestVersion}");

            // Find our DLL path
            string pluginPath = Assembly.GetExecutingAssembly().Location;
            string tempPath = pluginPath + ".update";

            // Download the update
            if (!DownloadFile(downloadUrl, tempPath))
            {
                _logger.LogError("Failed to download update");
                if (File.Exists(tempPath)) File.Delete(tempPath);
                return;
            }

            // Get Steam launch URL and game exe name for the wait loop
            string steamUrl = GetSteamLaunchUrl();
            string gameExeName = GetGameExeName();

            string batchPath = CreateUpdateScript(pluginPath, tempPath, steamUrl, gameExeName);

            _logger.LogInfo($"Update downloaded! Restarting to apply {latestVersion}...");

            // Start the batch script and quit
            var psi = new ProcessStartInfo
            {
                FileName = batchPath,
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            // Quit the game
            Application.Quit();
        }

        private static (System.Version version, string downloadUrl) GetLatestRelease()
        {
            try
            {
                string apiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "RiqMenu-Updater/1.0");

                    string json = client.DownloadString(apiUrl);

                    // Parse version from tag_name
                    var tagMatch = Regex.Match(json, @"""tag_name""\s*:\s*""v?([^""]+)""");
                    if (!tagMatch.Success) return (null, null);

                    string versionStr = tagMatch.Groups[1].Value;
                    if (!System.Version.TryParse(NormalizeVersion(versionStr), out System.Version version))
                        return (null, null);

                    // Find the DLL download URL
                    var urlMatch = Regex.Match(json, @"""browser_download_url""\s*:\s*""([^""]+\.dll)""");
                    if (!urlMatch.Success) return (null, null);

                    string downloadUrl = urlMatch.Groups[1].Value;

                    return (version, downloadUrl);
                }
            }
            catch
            {
                return (null, null);
            }
        }

        private static string NormalizeVersion(string version)
        {
            // Ensure version has at least 2 parts for Version.Parse
            var parts = version.Split('.');
            if (parts.Length == 1) return version + ".0";
            return version;
        }

        private static bool DownloadFile(string url, string destPath)
        {
            try
            {
                _logger.LogInfo($"Downloading update...");

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "RiqMenu-Updater/1.0");
                    client.DownloadFile(url, destPath);
                }

                _logger.LogInfo("Download complete");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Download failed: {ex.Message}");
                return false;
            }
        }

        private static bool IsDemo()
        {
            string dataPath = Application.dataPath;
            string gameRoot = Path.GetDirectoryName(dataPath);

            return gameRoot.Contains("Demo") ||
                   Directory.Exists(Path.Combine(gameRoot, "Bits & Bops Demo_Data"));
        }

        private static string GetSteamLaunchUrl()
        {
            // Steam App IDs
            // Bits & Bops Demo: 2200650
            // Bits & Bops: 1929290
            string appId = IsDemo() ? "2200650" : "1929290";
            return $"steam://rungameid/{appId}";
        }

        private static string GetGameExeName()
        {
            return IsDemo() ? "Bits & Bops Demo.exe" : "Bits & Bops.exe";
        }

        private static string CreateUpdateScript(string pluginPath, string tempPath, string steamUrl, string gameExeName)
        {
            string batchPath = Path.Combine(Path.GetTempPath(), "riqmenu_update.bat");

            // Batch script that:
            // 1. Waits for the game process to exit
            // 2. Replaces the DLL
            // 3. Restarts the game via Steam
            // 4. Deletes itself
            string batchContent = $@"@echo off
:waitloop
tasklist /FI ""IMAGENAME eq {gameExeName}"" 2>NUL | find /I /N ""{gameExeName}"">NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >nul
    goto waitloop
)
del ""{pluginPath}"" >nul 2>&1
move ""{tempPath}"" ""{pluginPath}"" >nul 2>&1
start """" ""{steamUrl}""
del ""%~f0""
";
            File.WriteAllText(batchPath, batchContent);
            return batchPath;
        }
    }
}
