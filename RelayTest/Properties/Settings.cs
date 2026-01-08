using System;
using System.IO;
using System.Text.Json;

namespace RelayTest.Properties
{
    internal sealed class Settings
    {
        private static Settings? _defaultInstance;
        private static bool _loaded;
        private static readonly object _sync = new();
        // Store config in the app folder so it can be opened with Notepad.
        private static readonly string ConfigDirectory = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "settings.json");
        // Legacy location in %AppData%\Simstarr for migration
        private static readonly string LegacyConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Simstarr");
        private static readonly string LegacyConfigPath = Path.Combine(LegacyConfigDirectory, "settings.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private string _journalPath = string.Empty;
        private string _relayAddress = string.Empty;
        private string _authToken = string.Empty;
        private string _appMode = "StandAlone";

        private int _fogShortMs = 3000; // default 3s
        private int _fogLongMs = 5000;  // default 5s

        // Per-relay journal match strings (4 relays)
        private readonly string[] _relayJournalFilters = new string[4] { string.Empty, string.Empty, string.Empty, string.Empty };

        // Per-relay selected event type name (4 relays)
        private readonly string[] _relayEventTypes = new string[4] { string.Empty, string.Empty, string.Empty, string.Empty };

        // Per-relay activation durations (milliseconds) ? new setting
        private readonly int[] _relayActivateMs = new int[4] { 3000, 3000, 3000, 3000 };

        // Shared activation cooldown (milliseconds)
        private int _activationCooldownMs = 5000;

        // SettingsForm window/persisted geometry
        private int _settingsFormWidth = 0;
        private int _settingsFormHeight = 0;
        private int _settingsFormLeft = int.MinValue;
        private int _settingsFormTop = int.MinValue;
        private string _settingsFormWindowState = "Normal";

        public static Settings Default
        {
            get
            {
                if (_defaultInstance == null)
                    _defaultInstance = new Settings();
                if (!_loaded)
                {
                    _defaultInstance.Load();
                    _loaded = true;
                }
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
            set => _relayAddress = value?.Trim() ?? string.Empty;
        }

        public string AuthToken
        {
            get => _authToken;
            set => _authToken = value?.Trim() ?? string.Empty;
        }

        public string AppMode
        {
            get => _appMode;
            set => _appMode = string.IsNullOrWhiteSpace(value) ? "StandAlone" : value.Trim();
        }

        // Fog durations (milliseconds). Clamped to sensible range.
        public int FogShortMs
        {
            get => _fogShortMs;
            set => _fogShortMs = ClampDuration(value, 3000);
        }

        public int FogLongMs
        {
            get => _fogLongMs;
            set => _fogLongMs = ClampDuration(value, 5000);
        }

        // Shared activation cooldown (milliseconds). Exposed and clamped like durations.
        public int ActivationCooldownMs
        {
            get => _activationCooldownMs;
            set => _activationCooldownMs = ClampDuration(value, 5000);
        }

        // Persisted SettingsForm geometry - width/height/left/top and window state (Normal/Maximized/Minimized)
        public int SettingsFormWidth
        {
            get => _settingsFormWidth;
            set => _settingsFormWidth = Math.Max(0, value);
        }

        public int SettingsFormHeight
        {
            get => _settingsFormHeight;
            set => _settingsFormHeight = Math.Max(0, value);
        }

        public int SettingsFormLeft
        {
            get => _settingsFormLeft;
            set => _settingsFormLeft = value;
        }

        public int SettingsFormTop
        {
            get => _settingsFormTop;
            set => _settingsFormTop = value;
        }

        public string SettingsFormWindowState
        {
            get => _settingsFormWindowState;
            set => _settingsFormWindowState = string.IsNullOrWhiteSpace(value) ? "Normal" : value.Trim();
        }

        // Expose relay journal filters as a copy to avoid external mutation of internal array.
        public string[] RelayJournalFilters
        {
            get => (string[])_relayJournalFilters.Clone();
            set
            {
                if (value == null)
                {
                    for (int i = 0; i < 4; i++) _relayJournalFilters[i] = string.Empty;
                    return;
                }
                for (int i = 0; i < 4; i++)
                    _relayJournalFilters[i] = i < value.Length && value[i] != null ? value[i].Trim() : string.Empty;
            }
        }

        // Expose relay event types (selected event name for each relay)
        public string[] RelayEventTypes
        {
            get => (string[])_relayEventTypes.Clone();
            set
            {
                if (value == null)
                {
                    for (int i = 0; i < 4; i++) _relayEventTypes[i] = string.Empty;
                    return;
                }
                for (int i = 0; i < 4; i++)
                    _relayEventTypes[i] = i < value.Length && value[i] != null ? value[i].Trim() : string.Empty;
            }
        }

        // Expose per-relay activation durations (milliseconds)
        public int[] RelayActivateMs
        {
            get => (int[])_relayActivateMs.Clone();
            set
            {
                if (value == null)
                {
                    for (int i = 0; i < 4; i++) _relayActivateMs[i] = 3000;
                    return;
                }
                for (int i = 0; i < 4; i++)
                {
                    var v = i < value.Length ? value[i] : 3000;
                    _relayActivateMs[i] = ClampDuration(v, 3000);
                }
            }
        }

        private int ClampDuration(int value, int fallback)
        {
            if (value < 500) return fallback;       // avoid absurdly small
            if (value > 60000) return 60000;        // 60s upper cap
            return value;
        }

        public void Save()
        {
            try
            {
                lock (_sync)
                {
                    var dir = Path.GetDirectoryName(ConfigPath) ?? ConfigDirectory;
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var data = new PersistedModel
                    {
                        JournalPath = _journalPath,
                        RelayAddress = _relayAddress,
                        AuthToken = _authToken,
                        AppMode = _appMode,
                        FogShortMs = _fogShortMs,
                        FogLongMs = _fogLongMs,
                        RelayJournalFilters = (string[])_relayJournalFilters.Clone(),
                        RelayEventTypes = (string[])_relayEventTypes.Clone(),
                        RelayActivateMs = (int[])_relayActivateMs.Clone(),
                        ActivationCooldownMs = _activationCooldownMs,
                        SettingsFormWidth = _settingsFormWidth == 0 ? null : _settingsFormWidth,
                        SettingsFormHeight = _settingsFormHeight == 0 ? null : _settingsFormHeight,
                        SettingsFormLeft = _settingsFormLeft == int.MinValue ? null : _settingsFormLeft,
                        SettingsFormTop = _settingsFormTop == int.MinValue ? null : _settingsFormTop,
                        SettingsFormWindowState = string.IsNullOrWhiteSpace(_settingsFormWindowState) ? null : _settingsFormWindowState
                    };

                    var json = JsonSerializer.Serialize(data, JsonOptions);
                    var tmp = ConfigPath + ".tmp";
                    File.WriteAllText(tmp, json);
                    // Atomic replace
                    if (File.Exists(ConfigPath))
                        File.Delete(ConfigPath);
                    File.Move(tmp, ConfigPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings to '{ConfigPath}': {ex.Message}");
            }
        }

        public void Reload()
        {
            lock (_sync)
            {
                _loaded = false;
                Load();
                _loaded = true;
            }
        }

        private void Load()
        {
            try
            {
                // If a local config file doesn't exist but a legacy one does, attempt migration.
                if (!File.Exists(ConfigPath) && File.Exists(LegacyConfigPath))
                {
                    try
                    {
                        var localDir = Path.GetDirectoryName(ConfigPath) ?? ConfigDirectory;
                        if (!Directory.Exists(localDir))
                            Directory.CreateDirectory(localDir);

                        File.Copy(LegacyConfigPath, ConfigPath);
                        System.Diagnostics.Debug.WriteLine($"Migrated settings from '{LegacyConfigPath}' to '{ConfigPath}'.");
                    }
                    catch (Exception mex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to migrate legacy settings: {mex.Message}");
                        // Continue ? still attempt to load whichever exists.
                    }
                }

                if (!File.Exists(ConfigPath))
                    return;

                lock (_sync)
                {
                    var json = File.ReadAllText(ConfigPath);
                    var model = JsonSerializer.Deserialize<PersistedModel>(json);
                    if (model == null) return;

                    _journalPath = model.JournalPath ?? string.Empty;
                    _relayAddress = model.RelayAddress ?? string.Empty;
                    _authToken = model.AuthToken ?? string.Empty;
                    _appMode = string.IsNullOrWhiteSpace(model.AppMode) ? "StandAlone" : model.AppMode.Trim();
                    if (model.FogShortMs.HasValue) _fogShortMs = ClampDuration(model.FogShortMs.Value, _fogShortMs);
                    if (model.FogLongMs.HasValue) _fogLongMs = ClampDuration(model.FogLongMs.Value, _fogLongMs);
                    if (model.ActivationCooldownMs.HasValue) _activationCooldownMs = ClampDuration(model.ActivationCooldownMs.Value, _activationCooldownMs);

                    if (model.RelayJournalFilters != null)
                    {
                        for (int i = 0; i < 4; i++)
                            _relayJournalFilters[i] = i < model.RelayJournalFilters.Length && model.RelayJournalFilters[i] != null
                                ? model.RelayJournalFilters[i].Trim()
                                : string.Empty;
                    }

                    if (model.RelayEventTypes != null)
                    {
                        for (int i = 0; i < 4; i++)
                            _relayEventTypes[i] = i < model.RelayEventTypes.Length && model.RelayEventTypes[i] != null
                                ? model.RelayEventTypes[i].Trim()
                                : string.Empty;
                    }

                    if (model.RelayActivateMs != null)
                    {
                        for (int i = 0; i < 4; i++)
                            _relayActivateMs[i] = i < model.RelayActivateMs.Length ? ClampDuration(model.RelayActivateMs[i], _relayActivateMs[i]) : _relayActivateMs[i];
                    }

                    if (model.SettingsFormWidth.HasValue) _settingsFormWidth = model.SettingsFormWidth.Value;
                    if (model.SettingsFormHeight.HasValue) _settingsFormHeight = model.SettingsFormHeight.Value;
                    if (model.SettingsFormLeft.HasValue) _settingsFormLeft = model.SettingsFormLeft.Value;
                    if (model.SettingsFormTop.HasValue) _settingsFormTop = model.SettingsFormTop.Value;
                    if (!string.IsNullOrWhiteSpace(model.SettingsFormWindowState)) _settingsFormWindowState = model.SettingsFormWindowState!;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings from '{ConfigPath}': {ex.Message}");
            }
        }

        // Model used for JSON persistence. Kept private and stable.
        private sealed class PersistedModel
        {
            public string? JournalPath { get; set; }
            public string? RelayAddress { get; set; }
            public string? AuthToken { get; set; }
            public string? AppMode { get; set; }
            public int? FogShortMs { get; set; }
            public int? FogLongMs { get; set; }
            public string[]? RelayJournalFilters { get; set; }
            public string[]? RelayEventTypes { get; set; }
            public int[]? RelayActivateMs { get; set; }
            public int? ActivationCooldownMs { get; set; }

            public int? SettingsFormWidth { get; set; }
            public int? SettingsFormHeight { get; set; }
            public int? SettingsFormLeft { get; set; }
            public int? SettingsFormTop { get; set; }
            public string? SettingsFormWindowState { get; set; }
        }
    }
}