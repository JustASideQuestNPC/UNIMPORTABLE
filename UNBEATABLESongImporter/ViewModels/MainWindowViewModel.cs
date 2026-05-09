using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syroot.Windows.IO;

namespace UNBEATABLESongImporter.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private string _customSongsDirectoryName = "";
    [ObservableProperty] private string _songDropZoneText = "song drop zone";
    [ObservableProperty] private bool _canImportSongs = false;
    
    private DirectoryInfo? _customSongsDirectory;

    private static List<string> _allowedAudioTypes = ["mp3", "wav"];
    private static List<string> _allowedImageTypes = ["png", "jpg", "jpeg"];
    private static List<string> _allowedDifficulties = [
        "Beginner",
        "Easy", // "normal" in-game
        "Normal", // "hard" in-game
        "Hard", // "expert" in-game
        "UNBEATABLE",
        "STAR"
    ];
    

    [GeneratedRegex(@"(?:\[)(.*)(?:\])", RegexOptions.Compiled)]
    private static partial Regex GetDifficultyRegex();

    public MainWindowViewModel()
    {
        AsyncInit();
    }

    private async Task AsyncInit()
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, "config.json");
        if (File.Exists(configPath))
        {
            // i should really do null checks for everything here, but i'm tired and the config file isn't essential, so
            // i'm just going to catch everything and then pray it was an error with the file and not my code
            var fileContents = await File.ReadAllTextAsync(configPath);
            try
            {
                var configJson = JsonSerializer.Deserialize<JsonObject>(fileContents);
                if (configJson == null)
                {
                    throw new JsonException("Config file is null.");
                }
                var customSongsDirectoryName = configJson["customSongsDirectory"];
                if (customSongsDirectoryName != null)
                {
                    CustomSongsDirectoryName = customSongsDirectoryName.ToString();
                    _customSongsDirectory = new DirectoryInfo(customSongsDirectoryName.ToString());
                }
            }
            catch (JsonException e)
            {
                Console.WriteLine("Error parsing config file: " + e.Message);
            }
        }
        
        if (_customSongsDirectory is not { Exists: true })
        {
            // look for the default path for all game data
            var gameDataDirectoryPath = Path.GetFullPath(
                "../LocalLow/D-CELL GAMES/UNBEATABLE/",
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            
            if (Directory.Exists(gameDataDirectoryPath))
            {
                // if you've never used custom songs before, create the directory
                if (!Directory.Exists(Path.Combine(gameDataDirectoryPath, "CustomSongs")))
                {
                    Directory.CreateDirectory(Path.Combine(gameDataDirectoryPath, "CustomSongs"));
                }
                _customSongsDirectory = new DirectoryInfo(Path.Combine(gameDataDirectoryPath, "CustomSongs"));
                CustomSongsDirectoryName = Path.GetFullPath(Path.Combine(gameDataDirectoryPath, "CustomSongs"));
            }
        }

        CanImportSongs = (_customSongsDirectory is { Exists: true });
    }

    public async Task OnWindowClosed(object? sender, WindowClosingEventArgs e)
    {
        if (CustomSongsDirectoryName != "")
        {
            var configJson = new JsonObject { ["customSongsDirectory"] = CustomSongsDirectoryName };
            var configPath = Path.Combine(Environment.CurrentDirectory, "config.json");
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(configJson));
        }
    }

    [RelayCommand]
    private async Task ChooseCustomSongsDirectory()
    {
        if (App.TopLevel == null)
        {
            Console.WriteLine("No top level window!");
            return;
        }

        var folders = await App.TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose custom songs directory"
        });
 
        if (folders.Count > 0)
        {
            _customSongsDirectory = new DirectoryInfo(folders[0].Path.ToString());
            CustomSongsDirectoryName = Uri.UnescapeDataString(folders[0].Path.AbsolutePath);
            CanImportSongs = (_customSongsDirectory is { Exists: true });
            Console.WriteLine("Custom songs directory: " + _customSongsDirectory);
        }
    }

    [RelayCommand]
    private async Task ChooseSongsToImport()
    {
        if (App.TopLevel == null)
        {
            Console.WriteLine("No top level window!");
            return;
        }
        
        
        var downloadsFolder = await App.TopLevel.StorageProvider.TryGetFolderFromPathAsync(KnownFolders.Downloads.Path);
        if (downloadsFolder == null)
        {
            // this should NOT be possible
            Console.WriteLine("No downloads folder!");
            return;
        }

        var files = await App.TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose songs to import",
            SuggestedStartLocation = downloadsFolder,
        });

        if (files.Count > 0)
        {
            foreach (var file in files)
            {
                var path = file.Path.AbsolutePath;
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ImportSong(path);
                }
            }
        }
    }

    public void OnDirectoryPathTextBoxChange()
    {
        if (CustomSongsDirectoryName != "")
        {
            _customSongsDirectory = new DirectoryInfo(CustomSongsDirectoryName);
            if (!_customSongsDirectory.Exists)
            {
                _customSongsDirectory = null;
            }
        }
        
        CanImportSongs = (_customSongsDirectory is { Exists: true });
    }

    public void ImportSong(string path)
    {
        path = Uri.UnescapeDataString(path);
        
        Console.WriteLine($"Importing song archive: {path}");

        using var archive = ZipFile.OpenRead(path);
        var hasAudio = false;
        var difficulties = new List<string>();
        List<ZipArchiveEntry> files = [];
        
        Console.WriteLine($"{archive.Entries.Count} entries in archive:");
        foreach (var entry in archive.Entries)
        {
            if (_allowedAudioTypes.Contains(Path.GetExtension(entry.Name)?.TrimStart('.').ToLowerInvariant()))
            {
                if (hasAudio)
                {
                    SongDropZoneText = "could not import song:\nfound more than 1 audio file";
                    return;
                }
                hasAudio = true;
                files.Add(entry);
            }
            else if (_allowedImageTypes.Contains(Path.GetExtension(entry.Name)?.TrimStart('.').ToLowerInvariant()) &&
                     Path.GetFileNameWithoutExtension(entry.Name) == "cover")
            {
                files.Add(entry);
            }
            else if (Path.GetExtension(entry.Name)?.TrimStart('.') == "txt")
            {
                var difficulty = GetDifficultyRegex().Match(entry.Name).Groups[1].Value;
                if (_allowedDifficulties.Contains(difficulty))
                {
                    difficulties.Add(difficulty);
                    files.Add(entry);
                }
            }
        }

        if (!hasAudio)
        {
            SongDropZoneText = "could not import song:\nno audio files found";
            return;
        }

        if (difficulties.Count == 0)
        {
            SongDropZoneText = "could not import song:\nno chart files found";
            return;
        }
        
        Console.WriteLine($"Song is valid with difficulty(ies): {string.Join(", ", difficulties)}");
        var outputFolderPath = Path.Combine(_customSongsDirectory.FullName, Path.GetFileNameWithoutExtension(path));
        Console.WriteLine($"Output folder: {outputFolderPath}");
        
        if (!Directory.Exists(outputFolderPath))
        {
            Directory.CreateDirectory(outputFolderPath);
        }
        
        foreach (var entry in files)
        {
            var destinationPath = Path.Combine(outputFolderPath, entry.Name);
            Console.WriteLine($"Extracting {entry.Name} to {destinationPath}");
            entry.ExtractToFile(destinationPath);
        }
        SongDropZoneText = "song imported!";
    }
}