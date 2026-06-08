namespace TM.Services.Modules.ProjectData.Models.Common
{
    public interface IContextStringProvider
    {
        string ToContextString();

        string ToBriefContextString();
    }
}
