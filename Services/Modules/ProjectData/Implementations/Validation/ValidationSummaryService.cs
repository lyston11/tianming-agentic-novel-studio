using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ValidationSummaryService : IValidationSummaryService
    {
        private const string ModulePath = "Validate/ValidationSummary";
        private const string DataDirectoryName = "data";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex VolumeNumberRegex = new(@"^第(\d+)卷", RegexOptions.Compiled);

        private readonly VolumeDesignService _volumeDesignService;
        private List<ValidationSummaryData> _dataItems;
        private readonly object _dataLock = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private readonly object _saveQueueLock = new();
        private Task _saveTail = Task.CompletedTask;
        private int _dataVersion;

        private string DataDirectoryPath => Path.Combine(
            StoragePathHelper.GetModulesStoragePath(ModulePath),
            DataDirectoryName);

        public ValidationSummaryService(VolumeDesignService volumeDesignService)
        {
            _volumeDesignService = volumeDesignService;
            _dataItems = new List<ValidationSummaryData>();

            StoragePathHelper.EnsureDirectoryExists(DataDirectoryPath);

            _volumeDesignService.CategoryDeleted += OnVolumeCategoryDeleted;

            StoragePathHelper.CurrentProjectChanged += (_, _) =>
            {
                Interlocked.Increment(ref _dataVersion);
                lock (_dataLock) { _dataItems = new List<ValidationSummaryData>(); }
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await LoadDataAsync().ConfigureAwait(false);
                    TM.App.Log("[ValidationSummaryService] 项目切换，数据已重新加载");
                }).SafeFireAndForget(ex => TM.App.Log($"[ValidationSummaryService] 项目切换后台加载失败: {ex.Message}"));
            };

            System.Threading.Tasks.Task.Run(async () => await LoadDataAsync().ConfigureAwait(false))
                .SafeFireAndForget(ex => TM.App.Log($"[ValidationSummaryService] 后台加载失败: {ex.Message}"));
        }

        private void OnVolumeCategoryDeleted(object? sender, CategoryDeletedEventArgs e)
        {
            try
            {
                var categoryName = e.CategoryName;
                var categoryId = e.CategoryId;
                List<ValidationSummaryData> snapshot;
                lock (_dataLock) { snapshot = new List<ValidationSummaryData>(_dataItems); }
                var dataToDelete = snapshot.Where(d =>
                    (!string.IsNullOrWhiteSpace(categoryId) && d.CategoryId == categoryId) ||
                    (string.IsNullOrWhiteSpace(d.CategoryId) && d.Category == categoryName)).ToList();
                Interlocked.Increment(ref _dataVersion);
                lock (_dataLock)
                {
                    _dataItems.RemoveAll(d =>
                        (!string.IsNullOrWhiteSpace(categoryId) && d.CategoryId == categoryId) ||
                        (string.IsNullOrWhiteSpace(d.CategoryId) && d.Category == categoryName));
                }

                if (dataToDelete.Count > 0)
                {
                    foreach (var item in dataToDelete)
                    {
                        DeleteDataFile(item.Id);
                    }
                    TM.App.Log($"[ValidationSummaryService] 级联删除: 分类'{categoryName}'下的 {dataToDelete.Count} 条数据已删除");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationSummaryService] 级联删除失败: {ex.Message}");
            }
        }

        #region 数据操作

        public List<ValidationSummaryData> GetAllData()
        {
            lock (_dataLock) return _dataItems.ToList();
        }

        public ValidationSummaryData? GetDataById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            lock (_dataLock) return _dataItems.FirstOrDefault(d => d.Id == id);
        }

        public ValidationSummaryData? GetDataByVolumeNumber(int volumeNumber)
        {
            lock (_dataLock) return _dataItems.FirstOrDefault(d => d.TargetVolumeNumber == volumeNumber);
        }

        public void AddData(ValidationSummaryData data)
        {
            if (data == null)
                return;

            if (string.IsNullOrEmpty(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }

            EnsureDataCategoryId(data);

            data.CreatedTime = DateTime.Now;
            data.ModifiedTime = DateTime.Now;

            Interlocked.Increment(ref _dataVersion);
            lock (_dataLock) _dataItems.Add(data);
            _ = EnqueueSaveDataItem(data);

            TM.App.Log($"[ValidationSummaryService] 添加数据: {data.Name}");
        }

        private void EnsureDataCategoryId(ValidationSummaryData data)
        {
            if (data == null)
                return;

            if (string.IsNullOrWhiteSpace(data.Category) || !string.IsNullOrWhiteSpace(data.CategoryId))
                return;

            var categories = GetAllCategories();
            var category = categories.FirstOrDefault(c => string.Equals(c.Name, data.Category, StringComparison.Ordinal));
            if (category == null || string.IsNullOrWhiteSpace(category.Id))
                return;

            data.CategoryId = category.Id;
        }

        public void UpdateData(ValidationSummaryData data)
        {
            if (data == null)
                return;

            data.ModifiedTime = DateTime.Now;
            Interlocked.Increment(ref _dataVersion);
            _ = EnqueueSaveDataItem(data);

            TM.App.Log($"[ValidationSummaryService] 更新数据: {data.Name}");
        }

        public void DeleteData(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;

            ValidationSummaryData? data;
            Interlocked.Increment(ref _dataVersion);
            lock (_dataLock)
            {
                data = _dataItems.FirstOrDefault(d => d.Id == id);
                if (data != null) _dataItems.Remove(data);
            }
            if (data != null)
            {
                DeleteDataFile(id);
                TM.App.Log($"[ValidationSummaryService] 删除数据: {data.Name}");
            }
        }

        #endregion

        #region 分类操作（订阅自VolumeDesignService）

        public List<ValidationSummaryCategory> GetAllCategories()
        {
            _volumeDesignService.EnsureInitialized();
            return _volumeDesignService.GetAllVolumeDesigns()
                .OrderBy(v => v.VolumeNumber)
                .Select(v => new ValidationSummaryCategory
                {
                    Id = v.Id,
                    Name = v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle}".Trim() : v.Name,
                    Icon = "Icon.Books",
                    Order = v.VolumeNumber,
                    IsBuiltIn = false,
                    IsEnabled = v.IsEnabled
                }).ToList();
        }

        #endregion

        #region 卷校验专用

        public void SaveVolumeValidation(int volumeNumber, ValidationSummaryData data)
        {
            if (data == null)
                return;

            var categories = GetAllCategories();
            var volumeCategory = categories.FirstOrDefault(c => c.Order == volumeNumber);
            var categoryName = volumeCategory?.Name ?? $"第{volumeNumber}卷";

            data.TargetVolumeNumber = volumeNumber;
            data.TargetVolumeName = categoryName;
            data.Name = $"{categoryName}校验";
            data.Category = categoryName;
            if (volumeCategory != null && !string.IsNullOrWhiteSpace(volumeCategory.Id))
            {
                data.CategoryId = volumeCategory.Id;
            }
            data.LastValidatedTime = DateTime.Now;

            var existingData = GetDataByVolumeNumber(volumeNumber);
            if (existingData != null)
            {
                data.Id = existingData.Id;
                data.CreatedTime = existingData.CreatedTime;
                data.ModifiedTime = DateTime.Now;

                Interlocked.Increment(ref _dataVersion);
                lock (_dataLock)
                {
                    var index = _dataItems.FindIndex(d => d.Id == existingData.Id);
                    if (index >= 0)
                    {
                        _dataItems[index] = data;
                    }
                }

                _ = EnqueueSaveDataItem(data);
                TM.App.Log($"[ValidationSummaryService] 覆盖更新卷校验: {data.Name}");
            }
            else
            {
                AddData(data);
                TM.App.Log($"[ValidationSummaryService] 新增卷校验: {data.Name}");
            }
        }

        public int ParseVolumeNumber(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName))
                return -1;

            var match = VolumeNumberRegex.Match(categoryName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int volumeNumber))
            {
                return volumeNumber;
            }

            return -1;
        }

        #endregion

        #region 数据持久化

        public Task FlushPendingAsync(CancellationToken ct = default)
        {
            Task tail;
            lock (_saveQueueLock)
            {
                tail = _saveTail;
            }
            return tail.WaitAsync(ct);
        }

        private Task EnqueueSaveDataItem(ValidationSummaryData data)
        {
            lock (_saveQueueLock)
            {
                _saveTail = _saveTail.ContinueWith(async _ =>
                {
                    await SaveDataItem(data).ConfigureAwait(false);
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
                return _saveTail;
            }
        }

        private async Task LoadDataAsync()
        {
            var loadVersion = Volatile.Read(ref _dataVersion);
            var items = new List<ValidationSummaryData>();
            try
            {
                if (!Directory.Exists(DataDirectoryPath))
                {
                    return;
                }

                var files = Directory.GetFiles(DataDirectoryPath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        await using var stream = File.OpenRead(file);
                        var data = await JsonSerializer.DeserializeAsync<ValidationSummaryData>(stream, JsonOptions).ConfigureAwait(false);
                        if (data != null)
                        {
                            items.Add(data);
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ValidationSummaryService] 加载数据文件失败: {file}, {ex.Message}");
                    }
                }

                TM.App.Log($"[ValidationSummaryService] 加载数据: {items.Count} 条");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationSummaryService] 加载数据失败: {ex.Message}");
            }
            finally
            {
                if (loadVersion == Volatile.Read(ref _dataVersion))
                {
                    lock (_dataLock) _dataItems = items;
                }
            }
        }

        private async Task SaveDataItem(ValidationSummaryData data)
        {
            var acquired = false;
            try
            {
                await _saveLock.WaitAsync().ConfigureAwait(false);
                acquired = true;

                StoragePathHelper.EnsureDirectoryExists(DataDirectoryPath);

                var filePath = Path.Combine(DataDirectoryPath, $"{data.Id}.json");
                var tmpVss = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpVss))
                {
                    await JsonSerializer.SerializeAsync(stream, data, JsonOptions).ConfigureAwait(false);
                }
                File.Move(tmpVss, filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationSummaryService] 保存数据失败: {data.Name}, {ex.Message}");
            }
            finally
            {
                if (acquired)
                    _saveLock.Release();
            }
        }

        private void DeleteDataFile(string id)
        {
            var filePath = Path.Combine(DataDirectoryPath, $"{id}.json");
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationSummaryService] 删除数据文件失败: {id}, {ex.Message}");
            }
        }

        #endregion
    }
}
