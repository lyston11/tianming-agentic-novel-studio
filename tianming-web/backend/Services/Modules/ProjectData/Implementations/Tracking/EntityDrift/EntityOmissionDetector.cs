using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class EntityOmissionDetector
    {
        private readonly GuideManager _guideManager;

        public EntityOmissionDetector(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        public async Task<IReadOnlyList<EntityOmissionRecord>> DetectAsync(
            string content,
            ChapterChanges changes,
            int recentVolumes = 5)
        {
            if (string.IsNullOrWhiteSpace(content) || changes == null)
                return Array.Empty<EntityOmissionRecord>();

            var cfg = LayeredContextConfig.TakeSnapshot();
            var minNameLength = cfg.DriftMinNameLength;
            var omissions = new List<EntityOmissionRecord>();

            var perDimensionTasks = EntityDimensionRegistry.All.Select(async descriptor =>
            {
                try
                {
                    var declaredIds = descriptor.ExtractDeclaredIds(changes);

                    var entities = await descriptor.LoadRecentEntitiesAsync(_guideManager, recentVolumes).ConfigureAwait(false);
                    if (entities.Count == 0)
                        return Array.Empty<EntityOmissionRecord>();

                    var local = new List<EntityOmissionRecord>();
                    foreach (var entity in entities)
                    {
                        if (declaredIds.Contains(entity.Id))
                            continue;

                        if (string.IsNullOrWhiteSpace(entity.Name) || entity.Name.Length < minNameLength)
                            continue;

                        if (!EntityNameNormalizeHelper.NameExistsInContent(content, entity.Name))
                            continue;

                        local.Add(new EntityOmissionRecord(
                            descriptor.DimensionName,
                            descriptor.DimensionCode,
                            entity.Id,
                            entity.Name,
                            descriptor.ChangeFieldName));
                    }
                    return (IReadOnlyList<EntityOmissionRecord>)local;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EntityOmissionDetector] {descriptor.DimensionName}维度扫描失败（非致命）: {ex.Message}");
                    return Array.Empty<EntityOmissionRecord>();
                }
            }).ToList();

            var results = await Task.WhenAll(perDimensionTasks).ConfigureAwait(false);
            foreach (var batch in results)
                omissions.AddRange(batch);

            return omissions;
        }
    }
}
