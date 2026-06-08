using System;
using System.IO;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using NAudio.Wave;
using TM.Framework.Notifications.SystemNotifications.SystemIntegration;
using TM.Framework.Notifications.Sound.SoundScheme;
using TM.Framework.Notifications.Sound.VolumeAndDevice;
using TM.Framework.Notifications.Sound.VoiceBroadcast;
using TM.Framework.Notifications.Sound.Services;

namespace TM.Services.Framework.Notification
{
    public class NotificationSoundService
    {
        private readonly SystemIntegrationSettings _sysSettings;
        private readonly TM.Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbSettings _dndSettings;
        private readonly VoiceBroadcastSettings _voiceSettings;
        private readonly VolumeAndDeviceSettings _volumeSettings;
        private readonly AudioEqualizerService _equalizerService;
        private readonly SoundSchemeSettings _schemeSettings;

        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioFileReader;
        private readonly object _ttsLock = new();
        private SpeechSynthesizer? _ttsSynth;

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

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

            System.Diagnostics.Debug.WriteLine($"[NotificationSoundService] {key}: {ex.Message}");
        }

        public NotificationSoundService(
            SystemIntegrationSettings sysSettings,
            TM.Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbSettings dndSettings,
            VoiceBroadcastSettings voiceSettings,
            VolumeAndDeviceSettings volumeSettings,
            AudioEqualizerService equalizerService,
            SoundSchemeSettings schemeSettings)
        {
            _sysSettings = sysSettings;
            _dndSettings = dndSettings;
            _voiceSettings = voiceSettings;
            _volumeSettings = volumeSettings;
            _equalizerService = equalizerService;
            _schemeSettings = schemeSettings;
        }

        public async Task PlayNotificationSound(ToastType type, bool isHighPriority = false)
        {
            await Task.Run(async () =>
            {
                try
                {
                    if (!_sysSettings.NotificationSound)
                    {
                        return;
                    }

                    if (_dndSettings.ShouldBlock(isHighPriority))
                    {
                        App.Log("[NotificationSoundService] 免打扰已拦截音效");
                        return;
                    }

                    var eventName = GetEventNameFromType(type);
                    var soundPath = await GetSoundPathForEventAsync(eventName).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
                    {
                        await PlaySoundFileAsync(soundPath).ConfigureAwait(false);
                    }
                    else
                    {
                        PlaySystemSound(type);
                    }

                }
                catch (Exception ex)
                {
                    App.Log($"[NotificationSoundService] 播放音效失败: {ex.Message}");
                }
            }).ConfigureAwait(false);
        }

        public void PlayTestSound()
        {
            try
            {
                System.Media.SystemSounds.Asterisk.Play();
                App.Log("[NotificationSoundService] 播放测试音效");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationSoundService] 播放测试音效失败: {ex.Message}");
            }
        }

        public async Task BroadcastNotification(string title, string message, bool isHighPriority = false)
        {
            await Task.Run(async () =>
            {
                try
                {
                    if (!_sysSettings.NotificationSound)
                    {
                        return;
                    }

                    if (_dndSettings.ShouldBlock(isHighPriority))
                    {
                        App.Log("[NotificationSoundService] 免打扰已拦截语音播报");
                        return;
                    }

                    await _voiceSettings.LoadSettingsAsync().ConfigureAwait(false);

                    if (!_voiceSettings.IsEnabled)
                    {
                        return;
                    }

                    string textToSpeak = $"{title}。{message}";
                    SpeakAsync(textToSpeak, _voiceSettings);

                    App.Log($"[NotificationSoundService] 播放语音播报: {textToSpeak}");
                }
                catch (Exception ex)
                {
                    App.Log($"[NotificationSoundService] 语音播报失败: {ex.Message}");
                }
            }).ConfigureAwait(false);
        }

        private async System.Threading.Tasks.Task PlaySoundFileAsync(string filePath)
        {
            try
            {
                StopCurrentSound();

                _audioFileReader = new AudioFileReader(filePath);
                _waveOut = new WaveOutEvent();

                await _volumeSettings.LoadSettingsAsync().ConfigureAwait(false);
                float volume = (float)(_volumeSettings.NotificationVolume / 100.0);
                _audioFileReader.Volume = volume;

                if (_equalizerService.IsEnabled)
                {
                    var settings = _equalizerService.GetCurrentSettings();
                    App.Log($"[NotificationSoundService] 均衡器效果已启用 - 低音:{settings.bass}dB 中低:{settings.midBass}dB 中:{settings.mid}dB 中高:{settings.midTreble}dB 高:{settings.treble}dB");
                }

                _waveOut.Init(_audioFileReader);
                _waveOut.PlaybackStopped += (s, e) =>
                {
                    _waveOut?.Dispose();
                    _audioFileReader?.Dispose();
                    _waveOut = null;
                    _audioFileReader = null;
                };

                _waveOut.Play();
                App.Log($"[NotificationSoundService] 播放音频文件: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationSoundService] 播放音频文件失败: {ex.Message}");

                System.Media.SystemSounds.Beep.Play();
            }
        }

        private void PlaySystemSound(ToastType type)
        {
            try
            {
                if (!_sysSettings.NotificationSound)
                    return;

                switch (type)
                {
                    case ToastType.Success:
                        System.Media.SystemSounds.Asterisk.Play();
                        break;
                    case ToastType.Warning:
                        System.Media.SystemSounds.Exclamation.Play();
                        break;
                    case ToastType.Error:
                        System.Media.SystemSounds.Hand.Play();
                        break;
                    case ToastType.Info:
                    default:
                        System.Media.SystemSounds.Beep.Play();
                        break;
                }

            }
            catch (Exception ex)
            {
                App.Log($"[NotificationSoundService] 播放系统音效失败: {ex.Message}");
            }
        }

        private void StopCurrentSound()
        {
            try
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
                _audioFileReader?.Dispose();
                _waveOut = null;
                _audioFileReader = null;
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationSoundService] 停止音效播放失败: {ex.Message}");
            }
        }

        private string GetEventNameFromType(ToastType type)
        {
            return type switch
            {
                ToastType.Success => "成功",
                ToastType.Warning => "警告",
                ToastType.Error => "错误",
                ToastType.Info => "信息提示",
                _ => "信息提示"
            };
        }

        private async System.Threading.Tasks.Task<string?> GetSoundPathForEventAsync(string eventName)
        {
            try
            {
                await _schemeSettings.LoadSettingsAsync().ConfigureAwait(false);

                if (_schemeSettings.EventSoundMappings.TryGetValue(eventName, out var soundName))
                {
                    if (string.IsNullOrWhiteSpace(soundName) || soundName == "无")
                    {
                        return null;
                    }

                    var customDir = StoragePathHelper.GetFilePath("Framework", "Notifications/Sound/SoundScheme/CustomSounds", "");
                    var filePath = Path.Combine(customDir, soundName + ".wav");
                    if (File.Exists(filePath))
                    {
                        return filePath;
                    }

                    filePath = Path.Combine(customDir, soundName);
                    if (File.Exists(filePath))
                    {
                        return filePath;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationSoundService] 获取事件音效路径失败: {ex.Message}");
                return null;
            }
        }

        private void SpeakAsync(string text, VoiceBroadcastSettings settings)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                lock (_ttsLock)
                {
                    _ttsSynth ??= new SpeechSynthesizer();
                    _ttsSynth.SetOutputToDefaultAudioDevice();
                    _ttsSynth.Rate = settings.Speed;
                    _ttsSynth.Volume = settings.Volume;
                    _ttsSynth.SpeakAsyncCancelAll();
                    _ttsSynth.SpeakAsync(text);
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(SpeakAsync), ex);
            }
        }
    }
}
