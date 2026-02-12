using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RiqMenuInstaller;

class Program
{
    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "RiqMenu-Installer/1.0" } }
    };

    private const string BepInExRepo = "BepInEx/BepInEx";
    private const string RiqMenuRepo = "KabirAcharya/RiqMenu";

    // Track user's choice for reinstalling BepInEx across multiple installations
    private static bool? _reinstallBepInExChoice = null;

    private enum Platform
    {
        Windows,
        Linux,
        MacOS
    }

    private static Platform _platform;

    private static Platform DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Platform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Platform.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Platform.MacOS;

        // Fallback
        return Platform.Windows;
    }

    static async Task<int> Main(string[] args)
    {
        _platform = DetectPlatform();

        // Console.Title only works reliably on Windows
        if (_platform == Platform.Windows)
        {
            try { Console.Title = "RiqMenu Installer"; } catch { }
        }

        PrintHeader();
        PrintCyan($"Platform: {_platform}");

        try
        {
            // Find game path(s)
            var gamePaths = args.Length > 0
                ? new List<string> { args[0] }
                : await FindGamePaths();

            if (gamePaths.Count == 0)
            {
                PrintError("Could not find Bits & Bops installation.");
                return 1;
            }

            var songsPaths = new List<string>();

            foreach (var gamePath in gamePaths)
            {
                if (gamePaths.Count > 1)
                {
                    Console.WriteLine();
                    PrintCyan($"=== Installing to: {Path.GetFileName(gamePath)} ===");
                }

                Console.WriteLine();

                // Install BepInEx
                await InstallBepInEx(gamePath);

                Console.WriteLine();

                // Install RiqMenu
                await InstallRiqMenu(gamePath);

                // Create songs folder - find the correct Data folder
                var songsPath = CreateSongsFolder(gamePath);
                if (songsPath != null)
                {
                    PrintCyan($"Songs folder ready: {songsPath}");
                    songsPaths.Add(songsPath);
                }
            }

            Console.WriteLine();
            PrintSuccess();

            Console.WriteLine();
            PrintCyan("Place your .riq/.bop files in:");
            foreach (var path in songsPaths)
            {
                Console.WriteLine($"  {path}");
            }
            Console.WriteLine();
            PrintCyan("Launch Bits & Bops and select 'Custom Songs'");
            PrintCyan("from the title screen, or press F1 in-game.");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            PrintError($"Installation failed: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
        }

        return 0;
    }

    static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("       RiqMenu Installer v0.1");
        Console.WriteLine("========================================");
        Console.ResetColor();
        Console.WriteLine();
    }

    static void PrintSuccess()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("========================================");
        Console.WriteLine("      Installation Complete!");
        Console.WriteLine("========================================");
        Console.ResetColor();
    }

    static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    static void PrintYellow(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    static void PrintGreen(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    static void PrintCyan(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    static List<string> GetDefaultSteamLibraries()
    {
        var libraries = new List<string>();

        switch (_platform)
        {
            case Platform.Windows:
                libraries.AddRange(new[]
                {
                    @"C:\Program Files (x86)\Steam",
                    @"C:\Program Files\Steam",
                    @"D:\Steam",
                    @"D:\SteamLibrary",
                    @"E:\Steam",
                    @"E:\SteamLibrary"
                });
                break;

            case Platform.Linux:
                var homeLinux = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                libraries.AddRange(new[]
                {
                    Path.Combine(homeLinux, ".steam", "steam"),
                    Path.Combine(homeLinux, ".steam", "debian-installation"),
                    Path.Combine(homeLinux, ".local", "share", "Steam"),
                    "/usr/share/steam",
                    "/usr/local/share/steam"
                });
                // Flatpak Steam
                var flatpakSteam = Path.Combine(homeLinux, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam");
                if (Directory.Exists(flatpakSteam))
                    libraries.Add(flatpakSteam);
                break;

            case Platform.MacOS:
                var homeMac = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                libraries.Add(Path.Combine(homeMac, "Library", "Application Support", "Steam"));
                break;
        }

        return libraries;
    }

    static async Task AddSteamLibraryFolders(List<string> steamLibraries)
    {
        string? primarySteamPath = null;

        // Try to find Steam path from registry (Windows only)
        if (_platform == Platform.Windows && OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                primarySteamPath = key?.GetValue("SteamPath") as string;
            }
            catch { }
        }

        // On Linux/macOS, use the first existing library as primary
        if (primarySteamPath == null)
        {
            primarySteamPath = steamLibraries.FirstOrDefault(Directory.Exists);
        }

        if (!string.IsNullOrEmpty(primarySteamPath))
        {
            if (!steamLibraries.Contains(primarySteamPath))
                steamLibraries.Insert(0, primarySteamPath);

            // Parse libraryfolders.vdf for additional libraries
            var libraryFile = Path.Combine(primarySteamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFile))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(libraryFile);
                    var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                    foreach (Match match in matches)
                    {
                        var libPath = match.Groups[1].Value;
                        // Handle escaped backslashes on Windows
                        if (_platform == Platform.Windows)
                            libPath = libPath.Replace(@"\\", @"\");

                        if (!steamLibraries.Any(l => l.Equals(libPath, StringComparison.OrdinalIgnoreCase)))
                            steamLibraries.Add(libPath);
                    }
                }
                catch { }
            }
        }
    }

    static bool HasGameExecutable(string path)
    {
        switch (_platform)
        {
            case Platform.Windows:
                return Directory.GetFiles(path, "*.exe").Length > 0;

            case Platform.Linux:
                // Look for executable files (no extension typically)
                var linuxExes = new[] { "Bits & Bops", "Bits & Bops Demo", "Bits & Bops.x86_64", "Bits & Bops Demo.x86_64" };
                return linuxExes.Any(exe => File.Exists(Path.Combine(path, exe)));

            case Platform.MacOS:
                // Look for .app bundles
                return Directory.GetDirectories(path, "*.app").Length > 0;

            default:
                return Directory.GetFiles(path, "*.exe").Length > 0;
        }
    }

    static async Task<List<string>> FindGamePaths()
    {
        PrintYellow("Searching for Bits & Bops installations...");

        var steamLibraries = GetDefaultSteamLibraries();

        // Try to find additional Steam library folders
        await AddSteamLibraryFolders(steamLibraries);

        // Game folder names to look for
        var gameNames = new[] { "Bits & Bops", "Bits & Bops Demo" };
        var foundInstallations = new List<(string Path, string Name)>();

        foreach (var library in steamLibraries)
        {
            foreach (var gameName in gameNames)
            {
                var path = Path.Combine(library, "steamapps", "common", gameName);
                if (Directory.Exists(path) && HasGameExecutable(path))
                {
                    if (!foundInstallations.Any(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    {
                        foundInstallations.Add((path, gameName));
                    }
                }
            }
        }

        if (foundInstallations.Count == 0)
        {
            // Ask user for path
            Console.WriteLine();
            PrintYellow("Could not find Bits & Bops automatically.");
            PrintYellow("Please enter the full path to your Bits & Bops folder:");
            Console.Write("> ");
            var userPath = Console.ReadLine()?.Trim().Trim('"');

            if (!string.IsNullOrEmpty(userPath) && Directory.Exists(userPath))
            {
                return new List<string> { userPath };
            }
            return new List<string>();
        }

        if (foundInstallations.Count == 1)
        {
            var (path, name) = foundInstallations[0];
            PrintGreen($"Found {name} at: {path}");
            return new List<string> { path };
        }

        // Multiple installations found - let user choose
        Console.WriteLine();
        PrintCyan("Found multiple Bits & Bops installations:");
        Console.WriteLine();
        for (int i = 0; i < foundInstallations.Count; i++)
        {
            var (path, name) = foundInstallations[i];
            Console.WriteLine($"  [{i + 1}] {name}");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"      {path}");
            Console.ResetColor();
        }
        Console.WriteLine($"  [A] All installations");
        Console.WriteLine($"  [M] Manual path entry");
        Console.WriteLine();
        Console.Write($"Select installation (1-{foundInstallations.Count}, A, or M): ");

        var input = Console.ReadLine()?.Trim().ToLower();

        if (input == "a" || input == "all")
        {
            PrintGreen("Selected: All installations");
            return foundInstallations.Select(i => i.Path).ToList();
        }

        if (input == "m" || input == "manual")
        {
            Console.WriteLine();
            PrintYellow("Enter the full path to your Bits & Bops folder:");
            Console.Write("> ");
            var userPath = Console.ReadLine()?.Trim().Trim('"');

            if (!string.IsNullOrEmpty(userPath) && Directory.Exists(userPath))
            {
                return new List<string> { userPath };
            }
            PrintError("Invalid path");
            return new List<string>();
        }

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= foundInstallations.Count)
        {
            var selected = foundInstallations[choice - 1];
            PrintGreen($"Selected: {selected.Name}");
            return new List<string> { selected.Path };
        }

        // Default to all
        PrintYellow("Invalid selection, installing to all");
        return foundInstallations.Select(i => i.Path).ToList();
    }

    static async Task InstallBepInEx(string gamePath)
    {
        var bepinexPath = Path.Combine(gamePath, "BepInEx");

        if (Directory.Exists(bepinexPath))
        {
            PrintCyan("BepInEx is already installed.");

            // Use cached choice if available
            if (_reinstallBepInExChoice.HasValue)
            {
                if (!_reinstallBepInExChoice.Value)
                {
                    PrintYellow("Skipping BepInEx installation.");
                    return;
                }
            }
            else
            {
                Console.Write("Do you want to reinstall/update it? (y/N): ");
                var response = Console.ReadLine()?.Trim().ToLower();
                _reinstallBepInExChoice = (response == "y");

                if (!_reinstallBepInExChoice.Value)
                {
                    PrintYellow("Skipping BepInEx installation.");
                    return;
                }
            }
        }

        PrintYellow("Fetching latest BepInEx 5.x release...");

        using var bepinexStream = await _http.GetStreamAsync(
            $"https://api.github.com/repos/{BepInExRepo}/releases");
        using var bepinexDoc = await JsonDocument.ParseAsync(bepinexStream);
        var releases = bepinexDoc.RootElement;

        if (releases.GetArrayLength() == 0)
            throw new Exception("Could not fetch BepInEx releases");

        // Determine the BepInEx asset pattern for this platform
        string bepinexPattern = _platform switch
        {
            Platform.Windows => @"BepInEx_(x64|win_x64).*\.zip$",
            Platform.Linux => @"BepInEx_(unix|linux_x64).*\.zip$",
            Platform.MacOS => @"BepInEx_(unix|macos_x64).*\.zip$",
            _ => @"BepInEx_(x64|win_x64).*\.zip$"
        };

        // Find latest stable 5.x release
        string? downloadUrl = null;
        string? version = null;

        foreach (var release in releases.EnumerateArray())
        {
            var tagName = release.GetProperty("tag_name").GetString() ?? "";
            var prerelease = release.GetProperty("prerelease").GetBoolean();

            if (!prerelease && Regex.IsMatch(tagName, @"^v?5\."))
            {
                var assets = release.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (Regex.IsMatch(name, bepinexPattern, RegexOptions.IgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        version = tagName;
                        break;
                    }
                }
                if (downloadUrl != null) break;

                // Fallback: For Linux/macOS, also try the generic "unix" build
                if (downloadUrl == null && _platform != Platform.Windows)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (Regex.IsMatch(name, @"BepInEx_unix.*\.zip$", RegexOptions.IgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            version = tagName;
                            break;
                        }
                    }
                }
                if (downloadUrl != null) break;
            }
        }

        if (downloadUrl == null)
            throw new Exception($"Could not find a suitable BepInEx 5.x release for {_platform}");

        PrintYellow($"Downloading BepInEx {version}...");

        var tempZip = Path.Combine(Path.GetTempPath(), "bepinex_temp.zip");
        var tempExtract = Path.Combine(Path.GetTempPath(), "bepinex_extract");

        try
        {
            // Download
            var bytes = await _http.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(tempZip, bytes);

            PrintYellow("Extracting BepInEx...");

            // Extract
            if (Directory.Exists(tempExtract))
                Directory.Delete(tempExtract, true);

            ZipFile.ExtractToDirectory(tempZip, tempExtract);

            // Copy to game folder
            PrintYellow("Installing BepInEx to game folder...");

            // On macOS, we need to install to the .app bundle's Contents folder
            string installPath = gamePath;
            if (_platform == Platform.MacOS)
            {
                var appBundle = Directory.GetDirectories(gamePath, "*.app").FirstOrDefault();
                if (appBundle != null)
                {
                    installPath = Path.Combine(appBundle, "Contents");
                    Directory.CreateDirectory(installPath);
                }
            }

            CopyDirectory(tempExtract, installPath);

            // On Unix, make the run_bepinex.sh script executable
            if (_platform != Platform.Windows)
            {
                var runScript = Path.Combine(installPath, "run_bepinex.sh");
                if (File.Exists(runScript))
                {
                    try
                    {
                        var chmod = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{runScript}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        System.Diagnostics.Process.Start(chmod)?.WaitForExit();
                        PrintYellow("Made run_bepinex.sh executable");
                    }
                    catch { }
                }

                // Print Unix-specific instructions
                PrintYellow("Note: On Linux/macOS, you may need to run the game via run_bepinex.sh");
                PrintYellow("You can also set Launch Options in the game properties in Steam launcher to \"./run_bepinex.sh %command%\"");
            }

            PrintGreen($"BepInEx {version} installed successfully!");
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
            if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
        }
    }

    static async Task InstallRiqMenu(string gamePath)
    {
        PrintYellow("Fetching latest RiqMenu release...");

        using var riqmenuStream = await _http.GetStreamAsync(
            $"https://api.github.com/repos/{RiqMenuRepo}/releases");
        using var riqmenuDoc = await JsonDocument.ParseAsync(riqmenuStream);
        var releases = riqmenuDoc.RootElement;

        if (releases.GetArrayLength() == 0)
            throw new Exception("No RiqMenu releases found. Please check the GitHub repository.");

        var latest = releases[0];
        var version = latest.GetProperty("tag_name").GetString();

        string? downloadUrl = null;
        string? fileName = null;

        var assets = latest.GetProperty("assets");
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                fileName = name;
                break;
            }
        }

        if (downloadUrl == null)
            throw new Exception("Could not find RiqMenu.dll in the latest release");

        // Determine plugins path based on platform
        string pluginsPath;
        if (_platform == Platform.MacOS)
        {
            var appBundle = Directory.GetDirectories(gamePath, "*.app").FirstOrDefault();
            if (appBundle != null)
            {
                pluginsPath = Path.Combine(appBundle, "Contents", "BepInEx", "plugins");
            }
            else
            {
                pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
            }
        }
        else
        {
            pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
        }
        Directory.CreateDirectory(pluginsPath);

        PrintYellow($"Downloading RiqMenu {version}...");

        var bytes = await _http.GetByteArrayAsync(downloadUrl);
        var destPath = Path.Combine(pluginsPath, fileName!);
        await File.WriteAllBytesAsync(destPath, bytes);

        PrintGreen($"RiqMenu {version} installed successfully!");
    }

    static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    static string? CreateSongsFolder(string gamePath)
    {
        // Find the Data folder - could be "Bits & Bops_Data" or "Bits & Bops Demo_Data"
        var dataFolders = new[] { "Bits & Bops_Data", "Bits & Bops Demo_Data" };

        // On macOS, the Data folder is inside the .app bundle
        string searchPath = gamePath;
        if (_platform == Platform.MacOS)
        {
            var appBundle = Directory.GetDirectories(gamePath, "*.app").FirstOrDefault();
            if (appBundle != null)
            {
                // Unity macOS structure: Game.app/Contents/Resources/Data
                var resourcesData = Path.Combine(appBundle, "Contents", "Resources", "Data");
                if (Directory.Exists(resourcesData))
                {
                    var songsPath = Path.Combine(resourcesData, "StreamingAssets", "RiqMenu");
                    if (!Directory.Exists(songsPath))
                    {
                        Directory.CreateDirectory(songsPath);
                    }
                    return songsPath;
                }

                // Alternative Unity structure: Game.app/Contents/Data
                var contentsData = Path.Combine(appBundle, "Contents", "Data");
                if (Directory.Exists(contentsData))
                {
                    var songsPath = Path.Combine(contentsData, "StreamingAssets", "RiqMenu");
                    if (!Directory.Exists(songsPath))
                    {
                        Directory.CreateDirectory(songsPath);
                    }
                    return songsPath;
                }
            }
        }

        foreach (var dataFolder in dataFolders)
        {
            var dataPath = Path.Combine(searchPath, dataFolder);
            if (Directory.Exists(dataPath))
            {
                var songsPath = Path.Combine(dataPath, "StreamingAssets", "RiqMenu");
                if (!Directory.Exists(songsPath))
                {
                    Directory.CreateDirectory(songsPath);
                }
                return songsPath;
            }
        }

        // Fallback: look for any *_Data folder
        try
        {
            var dataDir = Directory.GetDirectories(searchPath, "*_Data").FirstOrDefault();
            if (dataDir != null)
            {
                var songsPath = Path.Combine(dataDir, "StreamingAssets", "RiqMenu");
                if (!Directory.Exists(songsPath))
                {
                    Directory.CreateDirectory(songsPath);
                }
                return songsPath;
            }
        }
        catch
        {
            // Ignore errors
        }

        PrintError("Could not find game Data folder");
        return null;
    }
}
