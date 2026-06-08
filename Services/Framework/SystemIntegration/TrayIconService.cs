using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using TM.Framework.Notifications.SystemNotifications.SystemIntegration;
using Application = System.Windows.Application;

namespace TM.Services.Framework.SystemIntegration
{
    public class TrayIconService : IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        private const uint WM_MOUSEMOVE = 0x0200;

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

            System.Diagnostics.Debug.WriteLine($"[TrayIcon] {key}: {ex.Message}");
        }
        private NotifyIcon? _notifyIcon;
        private readonly SystemIntegrationSettings _settings;
        private Window? _mainWindow;

        public TrayIconService()
        {
            _settings = ServiceLocator.Get<SystemIntegrationSettings>();
        }

        public void Initialize(Window mainWindow)
        {
            BindMainWindow(mainWindow);

            if (_settings.ShowTrayIcon)
            {
                CreateTrayIcon();
                App.Log("[TrayIcon] 托盘图标已初始化");
            }

            _settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_settings.ShowTrayIcon))
                {
                    if (_settings.ShowTrayIcon)
                    {
                        CreateTrayIcon();
                    }
                    else
                    {
                        RemoveTrayIcon();
                    }
                }
            };
        }

        public void UpdateMainWindow(Window mainWindow)
        {
            if (_mainWindow != null)
            {
                _mainWindow.Closing -= OnMainWindowClosing;
                _mainWindow.Closed -= OnMainWindowClosed;
            }
            BindMainWindow(mainWindow);
            App.Log("[TrayIcon] 主窗口引用已更新");
        }

        private void BindMainWindow(Window mainWindow)
        {
            _mainWindow = mainWindow;
            if (_mainWindow != null)
            {
                _mainWindow.Closing += OnMainWindowClosing;
                _mainWindow.Closed += OnMainWindowClosed;
            }
        }

        private void CreateTrayIcon()
        {
            if (_notifyIcon != null)
                return;

            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Text = "天命",
                    Visible = true
                };

                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    _notifyIcon.Icon = Icon.ExtractAssociatedIcon(exePath);
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                    App.Log("[TrayIcon] 无法获取 EXE 路径，使用默认图标");
                }

                CreateContextMenu();

                _notifyIcon.MouseClick += OnTrayIconClick;
                _notifyIcon.MouseDoubleClick += OnTrayIconDoubleClick;

                App.Log("[TrayIcon] 托盘图标已创建");
            }
            catch (Exception ex)
            {
                App.Log($"[TrayIcon] 创建托盘图标失败: {ex.Message}");
            }
        }

        private void RemoveTrayIcon()
        {
            if (_notifyIcon != null)
            {
                try
                {
                    var icon = _notifyIcon;
                    _notifyIcon = null;

                    icon.Visible = false;

                    icon.MouseClick -= OnTrayIconClick;
                    icon.MouseDoubleClick -= OnTrayIconDoubleClick;

                    if (icon.ContextMenuStrip != null)
                    {
                        icon.ContextMenuStrip.Dispose();
                        icon.ContextMenuStrip = null;
                    }

                    if (icon.Icon != null)
                    {
                        icon.Icon.Dispose();
                        icon.Icon = null;
                    }

                    icon.Dispose();

                    for (int i = 0; i < 3; i++)
                    {
                        RefreshTrayArea();
                    }

                    App.Log("[TrayIcon] 托盘图标已彻底移除");
                }
                catch (Exception ex)
                {
                    App.Log($"[TrayIcon] 移除托盘图标时出错: {ex.Message}");
                }
            }
        }

        private static void RefreshTrayArea()
        {
            try
            {
                IntPtr systemTrayContainerHandle = FindWindow("Shell_TrayWnd", null);
                if (systemTrayContainerHandle == IntPtr.Zero) return;

                IntPtr systemTrayHandle = FindWindowEx(systemTrayContainerHandle, IntPtr.Zero, "TrayNotifyWnd", null);
                if (systemTrayHandle == IntPtr.Zero) return;

                IntPtr sysPagerHandle = FindWindowEx(systemTrayHandle, IntPtr.Zero, "SysPager", null);
                if (sysPagerHandle == IntPtr.Zero) return;

                string[] possibleNames = { "Notification Area", "User Promoted Notification Area", null! };

                foreach (var name in possibleNames)
                {
                    IntPtr notificationAreaHandle = FindWindowEx(sysPagerHandle, IntPtr.Zero, "ToolbarWindow32", name);
                    if (notificationAreaHandle != IntPtr.Zero)
                    {
                        RefreshToolbarWindow(notificationAreaHandle);
                    }
                }

                IntPtr notificationOverflowHandle = FindWindowEx(sysPagerHandle, IntPtr.Zero, "ToolbarWindow32", "Overflow Notification Area");
                if (notificationOverflowHandle != IntPtr.Zero)
                {
                    RefreshToolbarWindow(notificationOverflowHandle);
                }

                IntPtr overflowWindow = FindWindow("NotifyIconOverflowWindow", null);
                if (overflowWindow != IntPtr.Zero)
                {
                    IntPtr overflowToolbar = FindWindowEx(overflowWindow, IntPtr.Zero, "ToolbarWindow32", null);
                    if (overflowToolbar != IntPtr.Zero)
                    {
                        RefreshToolbarWindow(overflowToolbar);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[TrayIcon] 刷新托盘区域失败: {ex.Message}");
            }
        }

        private static void RefreshToolbarWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;

            try
            {
                RECT rect;
                if (GetClientRect(handle, out rect))
                {
                    for (int x = 0; x < rect.right; x += 4)
                    {
                        for (int y = 0; y < rect.bottom; y += 4)
                        {
                            SendMessage(handle, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)((y << 16) | x));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(RefreshToolbarWindow), ex);
                return;
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        private void CreateContextMenu()
        {
            if (_notifyIcon == null)
                return;

            var contextMenu = new ContextMenuStrip();

            var showHideItem = new ToolStripMenuItem("显示主窗口");
            showHideItem.Click += (s, e) => ShowMainWindow();
            contextMenu.Items.Add(showHideItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var settingsItem = new ToolStripMenuItem("设置");
            settingsItem.Click += (s, e) =>
            {
                ShowMainWindow();
                NavigateToSettings();
            };
            contextMenu.Items.Add(settingsItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += (s, e) => ExitApplication();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void OnTrayIconClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                ExecuteClickBehavior(_settings.SingleClickBehavior);
            });
        }

        private void OnTrayIconDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                ExecuteClickBehavior(_settings.DoubleClickBehavior);
            });
        }

        private void ExecuteClickBehavior(ClickBehavior behavior)
        {
            if (_mainWindow == null)
                return;

            switch (behavior)
            {
                case ClickBehavior.ShowWindow:
                    ShowMainWindow();
                    break;

                case ClickBehavior.HideWindow:
                    HideMainWindow();
                    break;

                case ClickBehavior.Toggle:
                    if (_mainWindow.Visibility == Visibility.Visible && _mainWindow.WindowState != WindowState.Minimized)
                    {
                        HideMainWindow();
                    }
                    else
                    {
                        ShowMainWindow();
                    }
                    break;

                case ClickBehavior.DoNothing:
                    break;
            }
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
                return;

            try
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
                App.Log("[TrayIcon] 显示主窗口");
            }
            catch (InvalidOperationException ex)
            {
                App.Log($"[TrayIcon] 显示主窗口失败（窗口已关闭）: {ex.Message}");
                _mainWindow = null;
            }
        }

        private void HideMainWindow()
        {
            if (_mainWindow == null)
                return;

            _mainWindow.Hide();
            App.Log("[TrayIcon] 隐藏主窗口");
        }

        private void OnMainWindowClosed(object? sender, EventArgs e)
        {
            _mainWindow = null;
            App.Log("[TrayIcon] 主窗口已关闭，引用已清空");
        }

        private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_settings.CloseToTray)
                return;

            e.Cancel = true;
            HideMainWindow();
            ShowTrayNotification("天命", "程序已最小化到系统托盘，继续在后台运行", ToolTipIcon.Info);
            App.Log("[TrayIcon] 关闭窗口 -> 隐藏到托盘");
        }

        public void ShowTrayNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 3000)
        {
            if (_notifyIcon == null)
                return;

            try
            {
                _notifyIcon.ShowBalloonTip(timeout, title, message, icon);
            }
            catch (Exception ex)
            {
                App.Log($"[TrayIcon] 显示托盘通知失败: {ex.Message}");
            }
        }

        private void NavigateToSettings()
        {
            try
            {
                var settingsWindow = new TM.Framework.UI.Windows.UnifiedWindow();
                settingsWindow.Owner = _mainWindow;

                if (settingsWindow.DataContext is TM.Framework.UI.Windows.UnifiedWindowViewModel viewModel)
                {
                    viewModel.CurrentMode = TM.Framework.UI.Windows.UnifiedWindowViewModel.WindowMode.Settings;
                }

                settingsWindow.Show();
                settingsWindow.Activate();

                App.Log("[TrayIcon] 已打开设置窗口");
            }
            catch (Exception ex)
            {
                App.Log($"[TrayIcon] 打开设置窗口失败: {ex.Message}");
            }
        }

        private void ExitApplication()
        {
            App.Log("[TrayIcon] 用户从托盘菜单退出程序");

            _settings.CloseToTray = false;

            Application.Current?.Dispatcher.BeginInvoke(
                (Action)(() => Application.Current.Shutdown()),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        public void Dispose()
        {
            RemoveTrayIcon();

            if (_mainWindow != null)
            {
                _mainWindow.Closing -= OnMainWindowClosing;
                _mainWindow.Closed -= OnMainWindowClosed;
            }

            App.Log("[TrayIcon] 托盘服务已释放");
            GC.SuppressFinalize(this);
        }
    }
}

