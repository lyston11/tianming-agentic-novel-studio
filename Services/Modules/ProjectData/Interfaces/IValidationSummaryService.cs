using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IValidationSummaryService
    {
        #region 数据操作

        List<ValidationSummaryData> GetAllData();

        ValidationSummaryData? GetDataById(string id);

        ValidationSummaryData? GetDataByVolumeNumber(int volumeNumber);

        void AddData(ValidationSummaryData data);

        void UpdateData(ValidationSummaryData data);

        void DeleteData(string id);

        #endregion

        #region 分类操作（订阅自VolumeDesignService，只读）

        List<ValidationSummaryCategory> GetAllCategories();

        #endregion

        #region 卷校验专用

        void SaveVolumeValidation(int volumeNumber, ValidationSummaryData data);

        Task FlushPendingAsync(CancellationToken ct = default);

        int ParseVolumeNumber(string categoryName);

        #endregion
    }
}
