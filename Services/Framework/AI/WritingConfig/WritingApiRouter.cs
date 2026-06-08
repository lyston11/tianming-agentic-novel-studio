using System;
using System.Linq;
using System.Threading;
using System.Windows;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.WritingConfig;

public class WritingApiRouter
{
    private readonly WritingSettingsService _writingSettingsService;
    private readonly IAIConfigurationService _aiConfigurationService;

    private volatile bool _isUsingBackup;
    private DateTime? _backupActivatedAt;
    private Timer? _recoveryTimer;
    private readonly object _stateLock = new();
    private string? _primaryBeforeBackupId;
    private int _recoveryAttempts;
    private static readonly int[] RecoveryLadder = [5, 10, 30, 60];

    public bool IsUsingBackup => _isUsingBackup;

    public DateTime? BackupActivatedAt => _backupActivatedAt;

    public event EventHandler? StatusChanged;

    public WritingApiRouter(WritingSettingsService writingSettingsService, IAIConfigurationService aiConfigurationService)
    {
        _writingSettingsService = writingSettingsService;
        _aiConfigurationService = aiConfigurationService;

        try
        {
            _aiConfigurationService.ConfigurationsChanged += (_, _) =>
            {
                _writingSettingsService.NormalizeAgainstAvailableIds(
                    _aiConfigurationService.GetAllConfigurations()
                        .Where(c => c.IsEnabled)
                        .Select(c => c.Id));
            };
            _writingSettingsService.NormalizeAgainstAvailableIds(
                _aiConfigurationService.GetAllConfigurations()
                    .Where(c => c.IsEnabled)
                    .Select(c => c.Id));
        }
        catch { }
    }

    public string? GetEffectiveChatConfigId()
    {
        if (_isUsingBackup)
        {
            var backupId = _writingSettingsService.Settings.BackupChatConfigId;
            if (!string.IsNullOrWhiteSpace(backupId))
            {
                var backup = _aiConfigurationService.GetAllConfigurations()
                    .FirstOrDefault(c => c.Id == backupId && c.IsEnabled);
                if (backup != null)
                    return backupId;
                TM.App.Log("[WritingApiRouter] 备用配置不存在或已禁用，回退到主配置");
            }
        }
        return _aiConfigurationService.GetActiveConfiguration()?.Id;
    }

    public void TryActivateBackupForFailedConfig(string? failedConfigId)
    {
        if (string.IsNullOrWhiteSpace(failedConfigId)) return;

        var primaryId = _aiConfigurationService.GetActiveConfiguration()?.Id;
        if (!string.Equals(failedConfigId, primaryId, StringComparison.Ordinal)) return;
        OnPrimaryFailed();
    }

    public void OnPrimaryFailed()
    {
        var settings = _writingSettingsService.Settings;
        var backupId = settings.BackupChatConfigId;
        if (string.IsNullOrWhiteSpace(backupId)) return;
        var recoveryMinutes = RecoveryLadder[0];

        var backup = _aiConfigurationService.GetAllConfigurations()
            .FirstOrDefault(c => c.Id == backupId && c.IsEnabled);
        if (backup == null)
        {
            TM.App.Log("[WritingApiRouter] 备用配置不存在或未启用，无法切换");
            return;
        }
        var backupName = backup.Name;
        var current = _aiConfigurationService.GetActiveConfiguration();

        lock (_stateLock)
        {
            if (_isUsingBackup) return;

            _primaryBeforeBackupId = current?.Id;
            _isUsingBackup = true;
            _backupActivatedAt = DateTime.Now;

            _recoveryTimer?.Dispose();
            _recoveryTimer = new Timer(_ => TryRecoverToPrimary(), null,
                TimeSpan.FromMinutes(recoveryMinutes), Timeout.InfiniteTimeSpan);
        }

        try
        {
            _aiConfigurationService.SetActiveConfiguration(backup);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _isUsingBackup = false;
                _backupActivatedAt = null;
                _primaryBeforeBackupId = null;
            }
            TM.App.Log($"[WritingApiRouter] 切换到备用配置失败: {ex.Message}");
            return;
        }

        TM.App.Log($"[WritingApiRouter] 主API Key耗尽，已切换全局激活配置到备用: {backupName}，{recoveryMinutes}分钟后自动恢复");

        Application.Current?.Dispatcher.BeginInvoke(() =>
            GlobalToast.Warning("已切换备用API",
                $"主API Key耗尽，已自动切换至备用模型（{backupName}），将在 {recoveryMinutes} 分钟后尝试恢复。"));

        RaiseStatusChanged();
    }

    public void ManualReset()
    {
        lock (_stateLock)
        {
            _recoveryTimer?.Dispose();
            _recoveryTimer = null;
        }
        TryRecoverToPrimary(forceEnable: true);
    }

    private void TryRecoverToPrimary(bool forceEnable = false)
    {
        var primaryId = _primaryBeforeBackupId;
        var recoveryLadder = RecoveryLadder;

        UserConfiguration? primary = null;
        if (!string.IsNullOrWhiteSpace(primaryId))
        {
            primary = _aiConfigurationService.GetAllConfigurations()
                .FirstOrDefault(c => c.Id == primaryId && c.IsEnabled);

            if (primary == null && forceEnable)
            {
                primary = _aiConfigurationService.GetAllConfigurations()
                    .FirstOrDefault(c => c.Id == primaryId);
                if (primary != null)
                {
                    primary.IsEnabled = true;
                    _aiConfigurationService.UpdateConfiguration(primary);
                    TM.App.Log($"[WritingApiRouter] 手动恢复: 强制启用主配置 {primary.Name}");
                }
            }
        }

        lock (_stateLock)
        {
            if (!_isUsingBackup) return;

            if (primary == null)
            {
                var ladderIndex = Math.Min(_recoveryAttempts, recoveryLadder.Length - 1);
                _recoveryAttempts++;
                var delay = TimeSpan.FromMinutes(recoveryLadder[ladderIndex]);
                TM.App.Log($"[WritingApiRouter] 主配置不存在/未启用，第{_recoveryAttempts}次重试，{delay.TotalMinutes:F0}分钟后再试");
                _recoveryTimer?.Dispose();
                _recoveryTimer = new Timer(_ => TryRecoverToPrimary(), null, delay, Timeout.InfiniteTimeSpan);
                return;
            }
        }

        try
        {
            _aiConfigurationService.SetActiveConfiguration(primary);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[WritingApiRouter] 恢复主配置失败: {ex.Message}，继续保持备用并延后重试");
            lock (_stateLock)
            {
                if (!_isUsingBackup) return;
                var ladderIndex = Math.Min(_recoveryAttempts, recoveryLadder.Length - 1);
                _recoveryAttempts++;
                var delay = TimeSpan.FromMinutes(recoveryLadder[ladderIndex]);
                _recoveryTimer?.Dispose();
                _recoveryTimer = new Timer(_ => TryRecoverToPrimary(), null, delay, Timeout.InfiniteTimeSpan);
            }
            return;
        }

        lock (_stateLock)
        {
            if (!_isUsingBackup) return;
            _isUsingBackup = false;
            _backupActivatedAt = null;
            _primaryBeforeBackupId = null;
            _recoveryAttempts = 0;
            _recoveryTimer?.Dispose();
            _recoveryTimer = null;
        }

        TM.App.Log($"[WritingApiRouter] 已恢复全局激活配置到主API: {primary.Name}");

        Application.Current?.Dispatcher.BeginInvoke(() =>
            GlobalToast.Success("主API已恢复", $"已自动恢复到主对话API：{primary.Name}"));

        RaiseStatusChanged();
    }

    private void RaiseStatusChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        dispatcher.BeginInvoke(new Action(() => StatusChanged?.Invoke(this, EventArgs.Empty)));
    }
}
