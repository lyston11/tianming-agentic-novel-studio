using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Context;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IRelationStrengthSourceService
    {
        Task<IReadOnlyList<RelationStrengthFact>> LoadRelationStrengthFactsAsync();

        void InvalidateCache();
    }

    public sealed record RelationStrengthFact(
        string LeftId,
        string RightId,
        RelationStrength Strength);
}
