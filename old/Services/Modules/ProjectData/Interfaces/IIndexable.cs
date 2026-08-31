namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IIndexable
    {
        string Id { get; }
        string Name { get; }

        string GetItemType();

        string GetDeepSummary();

        string GetBriefSummary();
    }
}
