using System.Collections.Generic;
using TM.Services.Framework.AI.Monitoring;

namespace TM.Services.Framework.AI.Interfaces.AI
{
    public interface IAIUsageStatisticsService
    {
        void RecordCall(ApiCallRecord record);

        IReadOnlyList<ApiCallRecord> GetAllRecords();

        StatisticsSummary GetSummary();

        IReadOnlyList<DailyStatistics> GetDailyStatistics(int days = 7);

        IReadOnlyDictionary<string, StatisticsSummary> GetStatisticsByModel();

        IReadOnlyList<ApiCallRecord> GetRecentRecords(int count);

        void ClearStatistics();
    }
}
