using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService : IContextService
    {
        #region Helpers

        private async Task<List<T>> LoadDataListAsync<T>(string relativePath, string fileName)
        {
            try
            {
                var filePath = Path.Combine(StoragePathHelper.GetStorageRoot(), relativePath, fileName);

                if (!File.Exists(filePath))
                {
                    TM.App.Log($"[ContextService] 文件不存在: {filePath}");
                    return new List<T>();
                }

                await using var stream = File.OpenRead(filePath);
                var data = await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions).ConfigureAwait(false);
                return data ?? new List<T>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] 加载数据失败 [{relativePath}/{fileName}]: {ex.Message}");
                return new List<T>();
            }
        }

        private async Task<List<T>> LoadPackagedDataAsync<T>(string configPath, string fileName, string dataKey)
        {
            var items = new List<T>();
            try
            {
                var filePath = Path.Combine(configPath, fileName);

                if (!File.Exists(filePath))
                {
                    return items;
                }

                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var dataProp))
                {
                    if (dataProp.TryGetProperty(dataKey, out var keyProp))
                    {
                        if (keyProp.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var fileProp in keyProp.EnumerateObject())
                            {
                                if (string.Equals(fileProp.Name, "categories", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (fileProp.Value.ValueKind == JsonValueKind.Array)
                                {
                                    var arrayJson = fileProp.Value.GetRawText();
                                    var arrayItems = JsonSerializer.Deserialize<List<T>>(arrayJson, JsonOptions);
                                    if (arrayItems != null) items.AddRange(arrayItems);
                                }
                            }
                        }
                        else if (keyProp.ValueKind == JsonValueKind.Array)
                        {
                            var arrayJson = keyProp.GetRawText();
                            var arrayItems = JsonSerializer.Deserialize<List<T>>(arrayJson, JsonOptions);
                            if (arrayItems != null) items.AddRange(arrayItems);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] 加载打包数据失败 [{fileName}/{dataKey}]: {ex.Message}");
            }

            return items;
        }

        private string GetProjectConfigPath(string moduleName)
        {
            return StoragePathHelper.GetProjectConfigPath(moduleName);
        }

        private string GetFunctionPath(string functionName)
        {
            var path = NavigationConfigParser.GetStoragePath(functionName);
            if (string.IsNullOrEmpty(path))
            {
                TM.App.Log($"[ContextService] [!] 未找到功能路径: {functionName}");
            }
            return path;
        }

        private string GetDataFileName(string functionName)
        {
            var snakeCase = ToSnakeCase(functionName);

            if (functionName is "Outline" or "Chapter" or "Blueprint" or "VolumeDesign")
            {
                return $"{snakeCase}_data.json";
            }

            return $"{snakeCase}.json";
        }

        public async Task<string> GetVolumeDesignContextStringAsync()
        {
            var coreTask = GetCoreDesignContextAsync();
            var outlineTask = BuildOutlineStringAsync();
            var volumeDesignTask = BuildVolumeDesignStringAsync();
            await Task.WhenAll(coreTask, outlineTask, volumeDesignTask).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"volume_design\">");
            sb.Append(await coreTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await outlineTask.ConfigureAwait(false));
            sb.AppendLine();
            sb.Append(await volumeDesignTask.ConfigureAwait(false));
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        public async Task<string> GetVolumeDesignStructureContextAsync(string? volumeKey)
        {
            var materialsTask = BuildCreativeMaterialsStringAsync(MaterialScope.Plot);
            var worldviewTask = BuildWorldviewStringAsync();
            var charTask = BuildCharacterStructureStringAsync();
            var factionTask = BuildFactionMinimalStringAsync();
            var locTask = BuildLocationSummaryStringAsync();
            var plotTask = BuildPlotRulesStructureStringAsync(volumeKey);
            var outlineTask = BuildOutlineStringAsync();
            await Task.WhenAll(materialsTask, worldviewTask, charTask, factionTask, locTask, plotTask, outlineTask).ConfigureAwait(false);

            var materialsResult = await materialsTask.ConfigureAwait(false);
            var worldviewResult = await worldviewTask.ConfigureAwait(false);
            var charResult = await charTask.ConfigureAwait(false);
            var factionResult = await factionTask.ConfigureAwait(false);
            var locResult = await locTask.ConfigureAwait(false);
            var plotResult = await plotTask.ConfigureAwait(false);
            var outlineResult = await outlineTask.ConfigureAwait(false);

            var totalChars = materialsResult.Length + worldviewResult.Length + charResult.Length + factionResult.Length + locResult.Length + plotResult.Length + outlineResult.Length;
            if (totalChars > ContextCharBudget)
            {
                TM.App.Log($"[ContextService] OPT-017 VolumeDesignContext 降级: {totalChars} chars > {ContextCharBudget}");
                plotResult = await BuildPlotRulesOutlineMinStringAsync(volumeKey).ConfigureAwait(false);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<design_context for=\"volume_design\">");
            sb.Append(materialsResult);
            sb.AppendLine();
            sb.Append(worldviewResult);
            sb.Append(charResult);
            sb.Append(factionResult);
            sb.Append(locResult);
            sb.Append(plotResult);
            sb.AppendLine();
            sb.Append(outlineResult);
            sb.AppendLine("</design_context>");
            return sb.ToString();
        }

        private string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = new System.Text.StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (char.IsUpper(c) && i > 0)
                {
                    result.Append('_');
                }
                result.Append(char.ToLower(c));
            }
            return result.ToString();
        }

        private async Task<List<T>> LoadFunctionDataAsync<T>(string functionName)
        {
            var cacheKey = $"{FuncDataCacheLayer}_{functionName}_{typeof(T).Name}";
            var cached = await _sessionCache.GetOrLoadAsync(cacheKey, async () =>
            {
                var path = GetFunctionPath(functionName);
                var fileName = GetDataFileName(functionName);
                return await LoadDataListAsync<T>(path, fileName).ConfigureAwait(false);
            }).ConfigureAwait(false);
            return cached ?? new List<T>();
        }

        private bool TryParseChapterId(string chapterId, out int volumeNumber, out int chapterNumber)
        {
            volumeNumber = 0;
            chapterNumber = 0;

            if (string.IsNullOrEmpty(chapterId))
                return false;

            if (chapterId.StartsWith("vol", StringComparison.Ordinal) && chapterId.Contains("_ch"))
            {
                var parts = chapterId.Replace("vol", "").Split("_ch");
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out volumeNumber) &&
                    int.TryParse(parts[1], out chapterNumber))
                {
                    return true;
                }
            }

            if (chapterId.Contains('-'))
            {
                var parts = chapterId.Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out volumeNumber) &&
                    int.TryParse(parts[1], out chapterNumber))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
