using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using TM.Framework.UI.Workspace.Services;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SystemPlugin
    {
        private readonly PanelCommunicationService _comm = ServiceLocator.Get<PanelCommunicationService>();

        private static void DebugLogOnce(string key, Exception ex)
            => TM.Framework.Common.Helpers.InfoLogDedup.DebugLogOnce(key, ex, "SystemPlugin");

        [KernelFunction("GetCurrentTime")]
        [Description("获取当前系统时间")]
        public Task<string> GetCurrentTimeAsync()
        {
            return Task.FromResult(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        [KernelFunction("GetProjectInfo")]
        [Description("获取当前项目的基本信息")]
        public Task<string> GetProjectInfoAsync()
        {
            TM.App.Log("[SystemPlugin] GetProjectInfo");

            try
            {
                var projectRoot = StoragePathHelper.GetProjectRoot();
                var projectName = Path.GetFileName(projectRoot);

                return Task.FromResult($@"# 项目信息
- 项目名称: {projectName}
- 项目路径: {projectRoot}
- 当前时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"[获取失败] {ex.Message}");
            }
        }

        [KernelFunction("NotifyUser")]
        [Description("向用户显示通知消息")]
        public Task<string> NotifyUserAsync(
            [Description("通知标题")] string title,
            [Description("通知内容")] string message)
        {
            TM.App.Log($"[SystemPlugin] NotifyUser: {title}");

            try
            {
                GlobalToast.Info(title, message);
                return Task.FromResult("[已通知]");
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(NotifyUserAsync), ex);
                return Task.FromResult("[通知失败]");
            }
        }

        [KernelFunction("RefreshChapterList")]
        [Description("刷新左栏的章节列表")]
        public Task<string> RefreshChapterListAsync()
        {
            TM.App.Log("[SystemPlugin] RefreshChapterList");

            try
            {
                _comm.PublishRefreshChapterList();
                return Task.FromResult("[已刷新]");
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(RefreshChapterListAsync), ex);
                return Task.FromResult("[刷新失败]");
            }
        }
    }
}
