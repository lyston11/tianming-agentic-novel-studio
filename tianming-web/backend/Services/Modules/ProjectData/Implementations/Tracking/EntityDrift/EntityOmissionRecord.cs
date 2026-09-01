namespace TM.Services.Modules.ProjectData.Implementations
{
    public sealed record EntityOmissionRecord(
        string DimensionName,
        string DimensionCode,
        string EntityId,
        string EntityName,
        string ChangeFieldName);
}
