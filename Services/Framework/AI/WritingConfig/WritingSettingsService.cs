using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.WritingConfig;

public class WritingSettingsService
{
    private readonly string _filePath;
    private WritingSettings _settings = new();
    private int _polishFallbackNotified;
    private int _settingsVersion;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    public event EventHandler? SettingsChanged;

    public WritingSettingsService()
    {
        _filePath = StoragePathHelper.GetFilePath("Services", "Framework/AI/WritingConfig", "writing_settings.json");
        _ = LoadAsync();
    }

    public WritingSettings Settings => _settings;

    public string? GetBackupChatConfigId() => _settings.BackupChatConfigId;

    public string? GetPolishConfigId() => _settings.PolishConfigId;

    public bool TryMarkPolishFallbackNotified()
    {
        return Interlocked.Exchange(ref _polishFallbackNotified, 1) == 0;
    }

    public void Update(Action<WritingSettings> updater)
    {
        var next = new WritingSettings
        {
            BackupChatConfigId = _settings.BackupChatConfigId,
            PolishConfigId = _settings.PolishConfigId,
            HumanizePickerEnabled = _settings.HumanizePickerEnabled,
            HumanizeGuardCosineThreshold = _settings.HumanizeGuardCosineThreshold,
            HumanizeGuardWindowChars = _settings.HumanizeGuardWindowChars,
            HumanizePickerChapterTimeoutMs = _settings.HumanizePickerChapterTimeoutMs,
        };
        updater(next);
        Save(next);
    }

    public void NormalizeAgainstAvailableIds(IEnumerable<string> availableIds)
    {
        var idSet = new HashSet<string>(availableIds.Where(id => !string.IsNullOrWhiteSpace(id)));
        var next = new WritingSettings
        {
            BackupChatConfigId = !string.IsNullOrWhiteSpace(_settings.BackupChatConfigId) && idSet.Contains(_settings.BackupChatConfigId)
                ? _settings.BackupChatConfigId
                : null,
            PolishConfigId = !string.IsNullOrWhiteSpace(_settings.PolishConfigId) && idSet.Contains(_settings.PolishConfigId)
                ? _settings.PolishConfigId
                : null,
            HumanizePickerEnabled = _settings.HumanizePickerEnabled,
            HumanizeGuardCosineThreshold = _settings.HumanizeGuardCosineThreshold,
            HumanizeGuardWindowChars = _settings.HumanizeGuardWindowChars,
            HumanizePickerChapterTimeoutMs = _settings.HumanizePickerChapterTimeoutMs,
        };

        var changed =
            !string.Equals(next.BackupChatConfigId, _settings.BackupChatConfigId, StringComparison.Ordinal) ||
            !string.Equals(next.PolishConfigId, _settings.PolishConfigId, StringComparison.Ordinal);

        if (changed)
            Save(next);
    }

    public void Save(WritingSettings settings)
    {
        _settings = settings ?? new WritingSettings();
        var json = JsonSerializer.Serialize(_settings, JsonHelper.CnDefault);
        var logLine = $"[WritingSettingsService] 保存完成 Backup={_settings.BackupChatConfigId} Polish={_settings.PolishConfigId}";
        var filePath = _filePath;
        Interlocked.Increment(ref _settingsVersion);
        _ = Task.Run(async () =>
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var tmp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, filePath, overwrite: true);
                TM.App.Log(logLine);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WritingSettingsService] 保存失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        });
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        var loadVersion = Volatile.Read(ref _settingsVersion);
        try
        {
            if (File.Exists(_filePath))
            {
                var loaded = JsonSerializer.Deserialize<WritingSettings>(await File.ReadAllTextAsync(_filePath).ConfigureAwait(false)) ?? new WritingSettings();
                if (loadVersion == Volatile.Read(ref _settingsVersion))
                {
                    _settings = loaded;
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[WritingSettingsService] 加载失败: {ex.Message}");
            if (loadVersion == Volatile.Read(ref _settingsVersion))
                _settings = new WritingSettings();
        }
    }
}
