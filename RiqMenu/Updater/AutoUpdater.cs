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
        private const string DllLocationLogFile = "riqmenu_dll_location.txt";

        // Set RIQMENU_FORCE_UPDATE=1 env var to force update (for testing)
        private static bool FORCE_UPDATE => Environment.GetEnvironmentVariable("RIQMENU_FORCE_UPDATE") == "1";

        private static ManualLogSource _logger;
        private static bool _isSteamInstall;
        private static Platform _platform;

        private enum Platform
        {
            Windows,
            Linux,
            MacOS
        }

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

            // Detect platform
            _platform = DetectPlatform();
            _logger.LogInfo($"Platform: {_platform}");

            // Find our DLL path and detect install type
            string pluginPath = Assembly.GetExecutingAssembly().Location;
            _isSteamInstall = DetectSteamInstall();

            // Log DLL location to temp file
            LogDllLocation(pluginPath);

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

            string tempPath = pluginPath + ".update";

            // Download the update
            if (!DownloadFile(downloadUrl, tempPath))
            {
                _logger.LogError("Failed to download update");
                if (File.Exists(tempPath)) File.Delete(tempPath);
                return;
            }

            // Get game process name for the wait loop
            string gameProcessName = GetGameProcessName();

            string scriptPath = CreateUpdateScript(pluginPath, tempPath, gameProcessName);

            _logger.LogInfo($"Update downloaded! Restarting to apply {latestVersion}...");

            // Start the update script and quit
            ProcessStartInfo psi;
            if (_platform == Platform.Windows)
            {
                psi = new ProcessStartInfo
                {
                    FileName = scriptPath,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
            else
            {
                // Unix: run bash script
                psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{scriptPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
            }
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

        private static Platform DetectPlatform()
        {
            // Use RuntimeInformation if available (.NET Standard 2.0+)
            // Fall back to Environment.OSVersion for older frameworks
            if (Application.platform == RuntimePlatform.WindowsPlayer ||
                Application.platform == RuntimePlatform.WindowsEditor)
            {
                return Platform.Windows;
            }
            else if (Application.platform == RuntimePlatform.LinuxPlayer ||
                     Application.platform == RuntimePlatform.LinuxEditor)
            {
                return Platform.Linux;
            }
            else if (Application.platform == RuntimePlatform.OSXPlayer ||
                     Application.platform == RuntimePlatform.OSXEditor)
            {
                return Platform.MacOS;
            }

            // Fallback detection using path separators and OS checks
            string path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (path.Contains("\\"))
                return Platform.Windows;
            if (Directory.Exists("/Applications"))
                return Platform.MacOS;
            return Platform.Linux;
        }

        private static bool IsDemo()
        {
            string dataPath = Application.dataPath;
            string gameRoot = GetGameRoot();

            return gameRoot.Contains("Demo") ||
                   Directory.Exists(Path.Combine(gameRoot, "Bits & Bops Demo_Data"));
        }

        private static string GetGameRoot()
        {
            string dataPath = Application.dataPath;

            // On macOS, dataPath is inside the .app bundle: Game.app/Contents/Data
            // We need to go up to get the directory containing the .app
            if (_platform == Platform.MacOS && dataPath.Contains(".app"))
            {
                // Find the .app directory and go one level up
                int appIndex = dataPath.IndexOf(".app", StringComparison.OrdinalIgnoreCase);
                string appPath = dataPath.Substring(0, appIndex + 4);
                return Path.GetDirectoryName(appPath);
            }

            return Path.GetDirectoryName(dataPath);
        }

        private static bool DetectSteamInstall()
        {
            string gameRoot = GetGameRoot();
            string gameRootLower = gameRoot.ToLowerInvariant();

            // Check for steam_appid.txt in game root (Steam games have this)
            if (File.Exists(Path.Combine(gameRoot, "steam_appid.txt")))
            {
                _logger.LogInfo("Detected Steam install (steam_appid.txt found)");
                return true;
            }

            // Check if path contains steamapps/common (typical Steam library structure)
            // Works on all platforms
            if (gameRootLower.Contains("steamapps") && gameRootLower.Contains("common"))
            {
                _logger.LogInfo("Detected Steam install (Steam library path)");
                return true;
            }

            // Platform-specific Steam API library checks
            // Unity games store these in {Game}_Data/Plugins/{arch}/ folder
            string dataPath = Application.dataPath; // e.g., "Bits & Bops_Data"
            string pluginsPath = Path.Combine(dataPath, "Plugins");

            switch (_platform)
            {
                case Platform.Windows:
                    // Check in Plugins/x86_64 and Plugins/x86 (Unity locations)
                    if (File.Exists(Path.Combine(pluginsPath, "x86_64", "steam_api64.dll")) ||
                        File.Exists(Path.Combine(pluginsPath, "x86", "steam_api.dll")) ||
                        File.Exists(Path.Combine(pluginsPath, "steam_api64.dll")) ||
                        File.Exists(Path.Combine(pluginsPath, "steam_api.dll")) ||
                        // Also check game root as fallback
                        File.Exists(Path.Combine(gameRoot, "steam_api64.dll")) ||
                        File.Exists(Path.Combine(gameRoot, "steam_api.dll")))
                    {
                        _logger.LogInfo("Detected Steam install (Steam API DLL found)");
                        return true;
                    }
                    break;

                case Platform.Linux:
                    if (File.Exists(Path.Combine(pluginsPath, "x86_64", "libsteam_api.so")) ||
                        File.Exists(Path.Combine(pluginsPath, "libsteam_api.so")) ||
                        File.Exists(Path.Combine(gameRoot, "libsteam_api.so")))
                    {
                        _logger.LogInfo("Detected Steam install (libsteam_api.so found)");
                        return true;
                    }
                    break;

                case Platform.MacOS:
                    // Check in Plugins folder and Frameworks
                    if (File.Exists(Path.Combine(pluginsPath, "libsteam_api.dylib")) ||
                        File.Exists(Path.Combine(gameRoot, "libsteam_api.dylib")))
                    {
                        _logger.LogInfo("Detected Steam install (libsteam_api.dylib found)");
                        return true;
                    }
                    // Also check inside the .app bundle Frameworks
                    string appContents = Path.Combine(dataPath, "..");
                    if (File.Exists(Path.Combine(appContents, "Frameworks", "libsteam_api.dylib")))
                    {
                        _logger.LogInfo("Detected Steam install (libsteam_api.dylib found in Frameworks)");
                        return true;
                    }
                    break;
            }

            _logger.LogInfo("Detected non-Steam install");
            return false;
        }

        private static void LogDllLocation(string pluginPath)
        {
            try
            {
                string logPath = Path.Combine(Path.GetTempPath(), DllLocationLogFile);
                string installType = _isSteamInstall ? "Steam" : "Non-Steam";
                string gameType = IsDemo() ? "Demo" : "Full";
                string logContent = $"DLL Location: {pluginPath}\nInstall Type: {installType}\nGame Version: {gameType}\nPlatform: {_platform}\nTimestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                File.WriteAllText(logPath, logContent);
                _logger.LogInfo($"DLL location logged to: {logPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to log DLL location: {ex.Message}");
            }
        }

        private static string GetSteamLaunchUrl()
        {
            // Steam App IDs
            // Bits & Bops Demo: 2200650
            // Bits & Bops: 1929290
            string appId = IsDemo() ? "2200650" : "1929290";
            return $"steam://rungameid/{appId}";
        }

        private static string GetGameExePath()
        {
            string gameRoot = GetGameRoot();
            bool isDemo = IsDemo();

            switch (_platform)
            {
                case Platform.Windows:
                    return Path.Combine(gameRoot, isDemo ? "Bits & Bops Demo.exe" : "Bits & Bops.exe");

                case Platform.Linux:
                    // Linux executables typically don't have extensions
                    return Path.Combine(gameRoot, isDemo ? "Bits & Bops Demo" : "Bits & Bops");

                case Platform.MacOS:
                    // macOS uses .app bundles
                    return Path.Combine(gameRoot, isDemo ? "Bits & Bops Demo.app" : "Bits & Bops.app");

                default:
                    return Path.Combine(gameRoot, isDemo ? "Bits & Bops Demo" : "Bits & Bops");
            }
        }

        private static string GetGameProcessName()
        {
            // Returns the process name to look for (without extension on Unix)
            bool isDemo = IsDemo();

            switch (_platform)
            {
                case Platform.Windows:
                    return isDemo ? "Bits & Bops Demo.exe" : "Bits & Bops.exe";

                case Platform.Linux:
                case Platform.MacOS:
                    // On Unix, process names don't include the extension
                    return isDemo ? "Bits & Bops Demo" : "Bits & Bops";

                default:
                    return isDemo ? "Bits & Bops Demo" : "Bits & Bops";
            }
        }

        private static string GetGameExeName()
        {
            return IsDemo() ? "Bits & Bops Demo.exe" : "Bits & Bops.exe";
        }

        private static string CreateUpdateScript(string pluginPath, string tempPath, string gameProcessName)
        {
            switch (_platform)
            {
                case Platform.Windows:
                    return CreateWindowsUpdateScript(pluginPath, tempPath, gameProcessName);
                case Platform.Linux:
                case Platform.MacOS:
                    return CreateUnixUpdateScript(pluginPath, tempPath, gameProcessName);
                default:
                    return CreateWindowsUpdateScript(pluginPath, tempPath, gameProcessName);
            }
        }

        private static string CreateWindowsUpdateScript(string pluginPath, string tempPath, string gameExeName)
        {
            string batchPath = Path.Combine(Path.GetTempPath(), "riqmenu_update.bat");

            // Determine how to launch the game after update
            string launchCommand;
            if (_isSteamInstall)
            {
                string steamUrl = GetSteamLaunchUrl();
                launchCommand = $@"start """" ""{steamUrl}""";
            }
            else
            {
                string gameExePath = GetGameExePath();
                launchCommand = $@"start """" ""{gameExePath}""";
            }

            // Batch script that:
            // 1. Waits for the game process to exit
            // 2. Replaces the DLL
            // 3. Restarts the game via Steam URL or direct exe
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
{launchCommand}
del ""%~f0""
";
            File.WriteAllText(batchPath, batchContent);
            return batchPath;
        }

        private static string CreateUnixUpdateScript(string pluginPath, string tempPath, string gameProcessName)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), "riqmenu_update.sh");

            // Determine how to launch the game after update
            string launchCommand;
            if (_isSteamInstall)
            {
                string steamUrl = GetSteamLaunchUrl();
                if (_platform == Platform.MacOS)
                {
                    launchCommand = $"open \"{steamUrl}\"";
                }
                else
                {
                    // Linux - use xdg-open or steam command
                    launchCommand = $"xdg-open \"{steamUrl}\" 2>/dev/null || steam \"{steamUrl}\" 2>/dev/null &";
                }
            }
            else
            {
                string gameExePath = GetGameExePath();
                if (_platform == Platform.MacOS)
                {
                    launchCommand = $"open \"{gameExePath}\"";
                }
                else
                {
                    launchCommand = $"\"{gameExePath}\" &";
                }
            }

            // Shell script that:
            // 1. Waits for the game process to exit
            // 2. Replaces the DLL
            // 3. Restarts the game via Steam URL or direct exe
            // 4. Deletes itself
            string scriptContent = $@"#!/bin/bash

# Wait for game process to exit
while pgrep -f ""{gameProcessName}"" > /dev/null 2>&1; do
    sleep 1
done

# Replace the DLL
rm -f ""{pluginPath}"" 2>/dev/null
mv ""{tempPath}"" ""{pluginPath}"" 2>/dev/null

# Relaunch the game
{launchCommand}

# Delete this script
rm -f ""$0""
";
            File.WriteAllText(scriptPath, scriptContent);

            // Make the script executable
            try
            {
                var chmod = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(chmod)?.WaitForExit();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to chmod script: {ex.Message}");
            }

            return scriptPath;
        }
    }
}
