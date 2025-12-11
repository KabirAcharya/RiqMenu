using System.IO.Compression;
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

    static async Task<int> Main(string[] args)
    {
        Console.Title = "RiqMenu Installer";

        PrintHeader();

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

    static async Task<List<string>> FindGamePaths()
    {
        PrintYellow("Searching for Bits & Bops installations...");

        var steamLibraries = new List<string>
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            @"D:\Steam",
            @"D:\SteamLibrary",
            @"E:\Steam",
            @"E:\SteamLibrary"
        };

        // Try to find Steam library folders from registry
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(steamPath))
            {
                steamLibraries.Insert(0, steamPath);

                var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraryFile))
                {
                    var content = await File.ReadAllTextAsync(libraryFile);
                    var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                    foreach (Match match in matches)
                    {
                        var libPath = match.Groups[1].Value.Replace(@"\\", @"\");
                        if (!steamLibraries.Contains(libPath))
                            steamLibraries.Add(libPath);
                    }
                }
            }
        }
        catch
        {
            // Ignore registry errors
        }

        // Game folder names to look for
        var gameNames = new[] { "Bits & Bops", "Bits & Bops Demo" };
        var foundInstallations = new List<(string Path, string Name)>();

        foreach (var library in steamLibraries)
        {
            foreach (var gameName in gameNames)
            {
                var path = Path.Combine(library, "steamapps", "common", gameName);
                if (Directory.Exists(path) && Directory.GetFiles(path, "*.exe").Length > 0)
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
                    if (Regex.IsMatch(name, @"BepInEx_(x64|win_x64).*\.zip$", RegexOptions.IgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        version = tagName;
                        break;
                    }
                }
                if (downloadUrl != null) break;
            }
        }

        if (downloadUrl == null)
            throw new Exception("Could not find a suitable BepInEx 5.x release");

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
            CopyDirectory(tempExtract, gamePath);

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

        var pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
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

        foreach (var dataFolder in dataFolders)
        {
            var dataPath = Path.Combine(gamePath, dataFolder);
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
            var dataDir = Directory.GetDirectories(gamePath, "*_Data").FirstOrDefault();
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
