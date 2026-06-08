using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.Settings
{
    public class SettingsManager
    {

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SettingsManager] {key}: {ex.Message}");
        }

        private readonly string _configDirectory;
        private readonly string _configFile;
        private Dictionary<string, object> _settings;
        private readonly object _settingsLock = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        public SettingsManager()
        {
            _configFile = StoragePathHelper.GetFilePath(
                "Framework",
                "UI/Workspaces",
                "workspace_config.json"
            );
            _configDirectory = Path.GetDirectoryName(_configFile) ?? "";

            _settings = new Dictionary<string, object>();
            _ = LoadSettingsAsync();
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    string json = await File.ReadAllTextAsync(_configFile).ConfigureAwait(false);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                    if (settings != null)
                    {
                        var loaded = new Dictionary<string, object>();
                        foreach (var kvp in settings)
                            loaded[kvp.Key] = kvp.Value;

                        lock (_settingsLock)
                            _settings = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce("LoadSettingsAsync", ex);
            }
        }

        private async Task SaveSettings()
        {
            var acquired = false;
            try
            {
                await _saveLock.WaitAsync().ConfigureAwait(false);
                acquired = true;

                if (!Directory.Exists(_configDirectory))
                {
                    Directory.CreateDirectory(_configDirectory);
                }

                var options = JsonHelper.Default;

                byte[] jsonBytes;
                lock (_settingsLock)
                    jsonBytes = JsonSerializer.SerializeToUtf8Bytes(_settings, options);

                var tmp = _configFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(tmp, jsonBytes).ConfigureAwait(false);
                File.Move(tmp, _configFile, overwrite: true);
            }
            catch (Exception ex)
            {
                DebugLogOnce("SaveSettings", ex);
            }
            finally
            {
                if (acquired)
                    _saveLock.Release();
            }
        }

        public T Get<T>(string key, T defaultValue)
        {
            try
            {
                object? value;
                lock (_settingsLock)
                    _settings.TryGetValue(key, out value);

                if (value != null)
                {
                    if (value is JsonElement jsonElement)
                    {
                        var deserialized = JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                        if (deserialized != null)
                        {
                            lock (_settingsLock)
                            {
                                if (_settings.TryGetValue(key, out var current) && current is not JsonElement)
                                    return (T)current;
                                _settings[key] = deserialized;
                            }
                            return deserialized;
                        }
                        return defaultValue;
                    }
                    return (T)value;
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce($"Get:{key}", ex);
            }
            return defaultValue;
        }

        public void Set<T>(string key, T value)
        {
            try
            {
                lock (_settingsLock)
                    _settings[key] = value ?? throw new ArgumentNullException(nameof(value));
                SaveSettings().SafeFireAndForget(ex => TM.App.Log($"[SettingsManager] {ex.Message}"));
            }
            catch (Exception ex)
            {
                DebugLogOnce($"Set:{key}", ex);
            }
        }

        public void Remove(string key)
        {
            try
            {
                var removed = false;
                lock (_settingsLock)
                    removed = _settings.Remove(key);

                if (removed)
                {
                    SaveSettings().SafeFireAndForget(ex => TM.App.Log($"[SettingsManager] {ex.Message}"));
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce($"Remove:{key}", ex);
            }
        }

        public bool Contains(string key)
        {
            lock (_settingsLock)
                return _settings.ContainsKey(key);
        }

        public void Clear()
        {
            lock (_settingsLock)
                _settings.Clear();
            SaveSettings().SafeFireAndForget(ex => TM.App.Log($"[SettingsManager] {ex.Message}"));
        }
    }
}

