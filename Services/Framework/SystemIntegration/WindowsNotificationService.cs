using System;
using Microsoft.Toolkit.Uwp.Notifications;
using TM.Services.Framework.Notification;

namespace TM.Services.Framework.SystemIntegration
{
    public class WindowsNotificationService
    {
        private static bool _isEnabled = false;
        private const string AppName = "天命";

        private static readonly Lazy<TM.Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbSettings> _cachedDndSettings =
            new(() => TM.Framework.Common.Services.ServiceLocator.Get<TM.Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbSettings>());
        private static readonly Lazy<TM.Framework.Notifications.NotificationManagement.NotificationHistory.NotificationHistorySettings> _cachedHistorySettings =
            new(() => TM.Framework.Common.Services.ServiceLocator.Get<TM.Framework.Notifications.NotificationManagement.NotificationHistory.NotificationHistorySettings>());

        public static void Enable()
        {
            try
            {
                _isEnabled = true;
                App.Log("[WindowsNotification] Windows原生通知已启用");
            }
            catch (Exception ex)
            {
                App.Log($"[WindowsNotification] 启用失败: {ex.Message}");
                _isEnabled = false;
            }
        }

        public static void Disable()
        {
            _isEnabled = false;
            App.Log("[WindowsNotification] Windows原生通知已禁用");
        }

        public static bool IsEnabled => _isEnabled;

        public static void Show(string title, string message, WindowsNotificationType type = WindowsNotificationType.Info, bool isHighPriority = false)
        {
            try
            {
                var toastType = ConvertToToastType(type);

                var dndSettings = _cachedDndSettings.Value;
                bool isBlocked = dndSettings.IsCurrentlyActive;

                var historySettings = _cachedHistorySettings.Value;
                string typeStr = ConvertTypeToString(type);
                historySettings.AddRecord(title, message, typeStr, isBlocked || !_isEnabled);

                if (!_isEnabled)
                {
                    App.Log("[WindowsNotification] 未启用，跳过显示");
                    return;
                }

                if (isBlocked)
                {
                    App.Log($"[WindowsNotification] 免打扰已拦截通知: {title}");
                    return;
                }

                _ = ServiceLocator.Get<NotificationSoundService>().PlayNotificationSound(toastType, isHighPriority);

                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .AddAttributionText(AppName)
                    .Show();

                App.Log($"[WindowsNotification] 已显示Windows通知: {title}");
            }
            catch (Exception ex)
            {
                App.Log($"[WindowsNotification] 显示通知失败: {ex.Message}");
            }
        }

        private static TM.Framework.Common.Controls.Feedback.ToastType ConvertToToastType(WindowsNotificationType type)
        {
            return type switch
            {
                WindowsNotificationType.Success => TM.Framework.Common.Controls.Feedback.ToastType.Success,
                WindowsNotificationType.Warning => TM.Framework.Common.Controls.Feedback.ToastType.Warning,
                WindowsNotificationType.Error => TM.Framework.Common.Controls.Feedback.ToastType.Error,
                WindowsNotificationType.Info => TM.Framework.Common.Controls.Feedback.ToastType.Info,
                _ => TM.Framework.Common.Controls.Feedback.ToastType.Info
            };
        }

        private static string ConvertTypeToString(WindowsNotificationType type)
        {
            return type switch
            {
                WindowsNotificationType.Success => "成功",
                WindowsNotificationType.Warning => "警告",
                WindowsNotificationType.Error => "错误",
                WindowsNotificationType.Info => "信息",
                _ => "信息"
            };
        }

        public static void ShowSuccess(string title, string message)
        {
            Show(title, message, WindowsNotificationType.Success);
        }

        public static void ShowWarning(string title, string message)
        {
            Show(title, message, WindowsNotificationType.Warning);
        }

        public static void ShowError(string title, string message)
        {
            Show(title, message, WindowsNotificationType.Error);
        }

        public static void ShowInfo(string title, string message)
        {
            Show(title, message, WindowsNotificationType.Info);
        }

        public static void ClearAll()
        {
            try
            {
                ToastNotificationManagerCompat.History.Clear();
                App.Log("[WindowsNotification] 已清除所有Windows通知");
            }
            catch (Exception ex)
            {
                App.Log($"[WindowsNotification] 清除通知失败: {ex.Message}");
            }
        }

        public static void Clear(string tag)
        {
            try
            {
                ToastNotificationManagerCompat.History.Remove(tag);
                App.Log($"[WindowsNotification] 已清除通知: {tag}");
            }
            catch (Exception ex)
            {
                App.Log($"[WindowsNotification] 清除通知失败: {ex.Message}");
            }
        }
    }

    public enum WindowsNotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }
}
