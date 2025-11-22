using System;
using System.IO;
using System.Text.Json;

namespace RelayTest.Properties
{
    // Custom settings store (JSON in %AppData%\Simstarr\settings.json).
    internal sealed class Settings
    {
        private static Settings? _defaultInstance;
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Simstarr",
            "settings.json");

        private string _journalPath = string.Empty;
        private string _relayAddress = string.Empty;

        public static Settings Default
        {
            get
            {
                _defaultInstance ??= new Settings();
                _defaultInstance.Load();
                return _defaultInstance;
            }
        }

        public string JournalPath
        {
            get => _journalPath;
            set => _journalPath = value ?? string.Empty;
        }

        public string RelayAddress
        {
            get => _relayAddress;
            set => _relayAddress = value ?? string.Empty;
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var data = new
                {
                    JournalPath = _journalPath,
                    RelayAddress = _relayAddress
                };

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("JournalPath", out var jp))
                    _journalPath = jp.GetString() ?? string.Empty;
                if (root.TryGetProperty("RelayAddress", out var ra))
                    _relayAddress = ra.GetString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }
    }
}