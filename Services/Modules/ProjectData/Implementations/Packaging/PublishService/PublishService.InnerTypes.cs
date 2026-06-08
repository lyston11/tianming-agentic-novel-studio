using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class PublishService : IPublishService
    {
        #region 内部类

        private class PackageMapping
        {
            public string ModuleType { get; }
            public string SubModule { get; }
            public string[] SubDirectories { get; }
            public string TargetFile { get; }

            public PackageMapping(string moduleType, string subModule, string[] subDirectories, string targetFile)
            {
                ModuleType = moduleType;
                SubModule = subModule;
                SubDirectories = subDirectories;
                TargetFile = targetFile;
            }
        }

        private class DataIntegrityResult
        {
            public bool IsValid { get; set; }
            public string Message { get; set; } = string.Empty;

            public static DataIntegrityResult Valid() => new() { IsValid = true };
            public static DataIntegrityResult WithWarnings(string message) => new() { IsValid = false, Message = message };
        }

        #endregion
    }
}
