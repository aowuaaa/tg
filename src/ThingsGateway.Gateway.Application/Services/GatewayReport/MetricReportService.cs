//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;
using ThingsGateway.Foundation.Common.Log;

namespace ThingsGateway.Gateway.Application;

/// <summary>
/// 指标报表服务
/// </summary>
internal sealed class MetricReportService : IMetricReportService
{
    /// <summary>
    /// 单条曲线参与统计的最大点数，超过后等分降采样
    /// </summary>
    private const int MaxPointsPerSeries = 20000;

    /// <summary>
    /// 一次报表读取的最大点数，超过仅提示
    /// </summary>
    private const int MaxTotalPoints = 8000000;

    private static readonly char[] DeviceNameSeparators = [',', '，', ';', '；'];

    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public async Task<List<MetricDefinition>> GetMetricDefinitionsAsync()
    {
        try
        {
            using var db = DbContext.GetDB<MetricDefinition>();
            var data = await db.Queryable<MetricDefinition>().OrderBy(a => a.SortCode).ToListAsync().ConfigureAwait(false);
            if (data.Count > 0) return data;

            var defaults = MetricDefinitionDefaults.Load();
            if (defaults.Count == 0) return data;

            await _seedLock.WaitAsync().ConfigureAwait(false);
            try
            {
                data = await db.Queryable<MetricDefinition>().OrderBy(a => a.SortCode).ToListAsync().ConfigureAwait(false);
                if (data.Count > 0) return data;

                await db.Insertable(defaults).ExecuteCommandAsync().ConfigureAwait(false);
                return await db.Queryable<MetricDefinition>().OrderBy(a => a.SortCode).ToListAsync().ConfigureAwait(false);
            }
            finally
            {
                _seedLock.Release();
            }
        }
        catch (Exception ex)
        {
            XTrace.WriteLine($"Get metric definition fail: {ex}");
            return MetricDefinitionDefaults.Load();
        }
    }

    public async Task<MetricReportResult> BuildAsync(MetricReportInput input)
    {
        var result = new MetricReportResult();
        if (input == null)
        {
            result.Messages.Add("参数为空");
            return result;
        }

        var start = input.StartTime;
        var end = input.EndTime;
        result.StartTime = start;
        result.EndTime = end;

        if (end <= start)
        {
            result.Messages.Add("结束时间必须大于开始时间");
            return result;
        }
        if ((end - start).TotalDays > 31)
        {
            result.Messages.Add("查询区间超过31天，数据量较大，建议缩短区间");
        }

        var definitions = (await GetMetricDefinitionsAsync().ConfigureAwait(false)).Where(a => a.Visible).ToList();
        if (input.MetricIds is { Count: > 0 })
        {
            var metricIds = input.MetricIds.ToHashSet();
            definitions = definitions.Where(a => metricIds.Contains(a.Id)).ToList();
        }
        if (definitions.Count == 0)
        {
            result.Messages.Add("没有可用的指标定义");
            return result;
        }

        var allDevices = (await GlobalData.GetCurrentUserDevices().ConfigureAwait(false)).ToList();
        var deviceFilter = input.DeviceIds?.Where(a => a > 0).ToHashSet();

        var devices = allDevices
            .Where(a => a.ReadOnlyVariableRuntimes is { Count: > 0 })
            .Where(a => deviceFilter == null || deviceFilter.Count == 0 || deviceFilter.Contains(a.Id))
            .OrderBy(a => a.Name, NaturalStringComparer.Instance)
            .ToList();
        if (devices.Count == 0)
        {
            result.Messages.Add("没有可用的采集设备，请检查通道是否启用、设备是否已配置变量");
            return result;
        }

        var jobs = new List<MetricJob>();
        var skippedMetrics = new List<string>();
        foreach (var definition in definitions)
        {
            var regex = BuildWildcardRegex(definition.VariableName);
            var allowDevices = ParseDeviceNames(definition.DeviceNames);
            var isOnline = IsOnlineMetric(definition.MetricType);

            if (regex == null && !isOnline)
            {
                skippedMetrics.Add($"{definition.Name}(未配置变量名称)");
                continue;
            }

            var matched = false;
            foreach (var device in devices)
            {
                if (allowDevices.Count > 0 && !allowDevices.Contains(device.Name)) continue;

                var variables = new List<string>();
                if (regex != null)
                {
                    variables.AddRange(device.ReadOnlyVariableRuntimes!.Keys
                        .Where(a => regex.IsMatch(a))
                        .OrderBy(a => a, NaturalStringComparer.Instance));
                    if (variables.Count == 0) continue;
                }
                else
                {
                    // 在线类指标不依赖具体变量，任取一个变量作为在线状态探针
                    var probe = device.ReadOnlyVariableRuntimes!.Keys
                        .OrderBy(a => a, NaturalStringComparer.Instance)
                        .FirstOrDefault();
                    if (probe == null) continue;
                    variables.Add(probe);
                }

                matched = true;
                jobs.Add(new MetricJob(definition, device.Id, device.Name, variables));
            }

            if (!matched)
            {
                skippedMetrics.Add($"{definition.Name}(变量[{definition.VariableName}]未匹配到设备变量)");
            }
        }

        if (skippedMetrics.Count > 0)
        {
            var names = string.Join("，", skippedMetrics.Take(5));
            if (skippedMetrics.Count > 5) names += $" 等 {skippedMetrics.Count} 个";
            result.Messages.Add($"以下指标已跳过：{names}。变量名称需与设备变量名称一致，可使用 * 通配（如 *温度*）");
        }

        if (jobs.Count == 0)
        {
            result.Messages.Add("没有可生成的指标，请先在“指标配置”中为指标填写正确的变量名称");
            var sampleNames = devices
                .SelectMany(a => a.ReadOnlyVariableRuntimes!.Keys)
                .Distinct()
                .OrderBy(a => a, NaturalStringComparer.Instance)
                .Take(30)
                .ToList();
            if (sampleNames.Count > 0)
            {
                result.Messages.Add($"当前设备可用变量示例：{string.Join("、", sampleNames)}");
            }
            return result;
        }

        var usedDeviceIds = jobs.Select(a => a.DeviceId).ToHashSet();
        var usedDevices = devices.Where(a => usedDeviceIds.Contains(a.Id)).ToList();
        result.Devices = usedDevices.Select(a => new MetricReportDevice { Id = a.Id, Name = a.Name }).ToList();

        var series = await ReadSeriesAsync(allDevices, jobs, start, end, input.LookbackHours, result).ConfigureAwait(false);

        var onlineSeries = new Dictionary<string, List<ReportPoint>>();
        foreach (var group in series.GroupBy(a => a.Key.DeviceName).OrderBy(a => a.Key, NaturalStringComparer.Instance))
        {
            // 优先使用数据点最多的变量，避免探针变量恰好没有历史数据
            var probe = group
                .OrderByDescending(a => a.Value.Count)
                .ThenBy(a => a.Key.VariableName, NaturalStringComparer.Instance)
                .First();
            onlineSeries[group.Key] = probe.Value;
        }

        var downsampled = new HashSet<string>();
        foreach (var definition in definitions)
        {
            var definitionJobs = jobs.Where(a => ReferenceEquals(a.Definition, definition)).ToList();
            if (definitionJobs.Count == 0) continue;

            var row = new MetricReportRow
            {
                MetricId = definition.Id,
                Name = definition.Name,
                GroupName = definition.GroupName,
                Unit = definition.Unit,
                MetricType = definition.MetricType,
            };

            foreach (var device in usedDevices)
            {
                var job = definitionJobs.FirstOrDefault(a => a.DeviceId == device.Id);
                if (job == null)
                {
                    row.Displays.Add(string.Empty);
                    row.Numbers.Add(null);
                    continue;
                }

                List<ReportPoint>? deviceSeries = null;
                if (IsOnlineMetric(definition.MetricType))
                {
                    onlineSeries.TryGetValue(device.Name, out deviceSeries);
                }
                else if (job.Variables.Count > 0)
                {
                    if (series.TryGetValue((device.Name, job.Variables[0]), out var points))
                    {
                        if (points.Count > MaxPointsPerSeries)
                        {
                            points = MetricCalculator.Downsample(points, MaxPointsPerSeries);
                            downsampled.Add(device.Name + "|" + job.Variables[0]);
                        }
                        deviceSeries = points;
                    }
                }

                var (number, display) = ComputeValue(definition, deviceSeries, start, end);
                row.Numbers.Add(number);
                row.Displays.Add(display ?? string.Empty);
            }

            result.Rows.Add(row);
        }

        if (downsampled.Count > 0)
        {
            result.Messages.Add($"部分序列点数过多，已降采样到 {MaxPointsPerSeries} 点，时长类指标精度会略有损失");
        }

        return result;
    }

    public async Task<bool> SaveMetricDefinitionAsync(MetricDefinition definition)
    {
        if (definition == null) return false;

        var name = definition.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return false;

        definition.Name = name;
        definition.GroupName = NormalizeText(definition.GroupName);
        definition.VariableName = NormalizeText(definition.VariableName);
        definition.DeviceNames = NormalizeText(definition.DeviceNames);
        definition.StateValue = NormalizeText(definition.StateValue);
        definition.Unit = NormalizeText(definition.Unit);
        definition.Decimals = Math.Clamp(definition.Decimals, 0, 6);

        using var db = DbContext.GetDB<MetricDefinition>();
        if (definition.Id == 0)
        {
            definition.Id = CommonUtils.GetSingleId();
            return await db.Insertable(new List<MetricDefinition> { definition }).ExecuteCommandAsync().ConfigureAwait(false) > 0;
        }
        return await db.Updateable(new List<MetricDefinition> { definition }).ExecuteCommandAsync().ConfigureAwait(false) > 0;
    }

    public async Task<bool> DeleteMetricDefinitionAsync(long id)
    {
        if (id <= 0) return false;

        using var db = DbContext.GetDB<MetricDefinition>();
        return await db.Deleteable<MetricDefinition>().Where(a => a.Id == id).ExecuteCommandAsync().ConfigureAwait(false) > 0;
    }

    private static string? NormalizeText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// 读取历史数据并按 设备+变量 分组
    /// </summary>
    private static async Task<Dictionary<(string DeviceName, string VariableName), List<ReportPoint>>> ReadSeriesAsync(
        List<DeviceRuntime> allDevices,
        List<MetricJob> jobs,
        DateTime start,
        DateTime end,
        int lookbackHours,
        MetricReportResult result)
    {
        var series = new Dictionary<(string DeviceName, string VariableName), List<ReportPoint>>();

        var neededNames = jobs.SelectMany(a => a.Variables).Distinct().ToArray();
        if (neededNames.Length == 0)
        {
            result.Messages.Add("所选指标均未配置变量名称，无法读取历史数据；请打开“指标配置”为指标填写变量名称（离线时长/在线率可留空）");
            return series;
        }

        var dbDevices = allDevices.Where(a => a.Driver is IDBHistoryValueService).ToList();
        if (dbDevices.Count == 0)
        {
            result.Messages.Add("未找到数据库业务设备（DB插件），无法读取历史数据；请先添加数据库插件设备并开启历史存储");
            return series;
        }

        var lookback = TimeSpan.FromHours(Math.Clamp(lookbackHours, 0, 168));
        if (lookback > end - start) lookback = end - start;

        var input = new DBHistoryValuePageInput
        {
            StartTime = start - lookback,
            EndTime = end,
            VariableNames = neededNames,
        };

        foreach (var dbDevice in dbDevices)
        {
            var oper = await BusinessDatabaseUtil.GetDBHistoryValuesAsync(dbDevice.Id, input).ConfigureAwait(false);
            if (!oper.IsSuccess || oper.Content == null)
            {
                result.Messages.Add($"读取历史数据失败[{dbDevice.Name}]：{oper.ErrorMessage}");
                continue;
            }

            foreach (var item in oper.Content)
            {
                if (string.IsNullOrEmpty(item.DeviceName) || string.IsNullOrEmpty(item.Name)) continue;
                var key = (item.DeviceName, item.Name);
                if (!series.TryGetValue(key, out var list))
                {
                    list = new List<ReportPoint>();
                    series[key] = list;
                }
                list.Add(new ReportPoint(item.CollectTime, item.Value, item.IsOnline));
            }
        }

        foreach (var list in series.Values)
        {
            list.Sort((x, y) => x.Time.CompareTo(y.Time));
        }

        var total = series.Values.Sum(a => a.Count);
        if (total > MaxTotalPoints)
        {
            result.Messages.Add($"读取数据点 {total} 个，数据量较大，建议缩短区间或增大历史存储间隔");
        }

        return series;
    }

    private static bool IsOnlineMetric(MetricTypeEnum type)
    {
        return type is MetricTypeEnum.OfflineDuration or MetricTypeEnum.OnlineRate;
    }

    private static (double? Number, string? Display) ComputeValue(MetricDefinition definition, List<ReportPoint>? points, DateTime start, DateTime end)
    {
        if (points == null || points.Count == 0) return (null, null);

        switch (definition.MetricType)
        {
            case MetricTypeEnum.OfflineDuration:
                {
                    var segments = MetricCalculator.BuildSegments(points, start, end, MetricCalculator.ResolveMaxGap(points));
                    if (segments.Count == 0) return (null, null);
                    var hours = MetricCalculator.OfflineHours(segments, start, end);
                    return (hours, FormatNumber(hours, definition.Decimals));
                }
            case MetricTypeEnum.OnlineRate:
                {
                    var segments = MetricCalculator.BuildSegments(points, start, end, MetricCalculator.ResolveMaxGap(points));
                    if (segments.Count == 0) return (null, null);
                    var rate = MetricCalculator.OnlineRate(segments, start, end);
                    return (rate, FormatNumber(rate, definition.Decimals) + "%");
                }
            case MetricTypeEnum.StateDuration:
                {
                    var segments = MetricCalculator.BuildSegments(points, start, end, MetricCalculator.ResolveMaxGap(points));
                    if (segments.Count == 0) return (null, null);
                    var state = MetricCalculator.ParseStateValue(definition.StateValue);
                    var hours = MetricCalculator.StateHours(segments, state);
                    return (hours, FormatNumber(hours, definition.Decimals));
                }
            case MetricTypeEnum.StateCount:
                {
                    var segments = MetricCalculator.BuildSegments(points, start, end, MetricCalculator.ResolveMaxGap(points));
                    if (segments.Count == 0) return (null, null);
                    var state = MetricCalculator.ParseStateValue(definition.StateValue);
                    var count = MetricCalculator.CountStateEntries(segments, state);
                    return (count, count.ToString(CultureInfo.InvariantCulture));
                }
            case MetricTypeEnum.FirstTimestamp:
                {
                    var segments = MetricCalculator.BuildSegments(points, start, end, MetricCalculator.ResolveMaxGap(points));
                    if (segments.Count == 0) return (null, null);
                    var state = MetricCalculator.ParseStateValue(definition.StateValue);
                    var time = MetricCalculator.FirstStateTimestamp(segments, state);
                    return (null, FormatTime(time));
                }
            case MetricTypeEnum.LastTimestamp:
                {
                    var segments = MetricCalculator.BuildSegments(points, start, end, MetricCalculator.ResolveMaxGap(points));
                    if (segments.Count == 0) return (null, null);
                    var state = MetricCalculator.ParseStateValue(definition.StateValue);
                    var time = MetricCalculator.LastStateTimestamp(segments, state);
                    return (null, FormatTime(time));
                }
            case MetricTypeEnum.CounterDelta:
                {
                    var delta = MetricCalculator.CounterDelta(points, start, end);
                    if (delta == null) return (null, null);
                    var value = (double)delta.Value;
                    return (value, FormatNumber(value, definition.Decimals));
                }
            default:
                {
                    var (avg, max, min, last) = MetricCalculator.Aggregate(points, start, end);
                    decimal? value = definition.MetricType switch
                    {
                        MetricTypeEnum.Avg => avg,
                        MetricTypeEnum.Max => max,
                        MetricTypeEnum.Min => min,
                        MetricTypeEnum.LastValue => last,
                        _ => null,
                    };
                    if (value == null) return (null, null);
                    var number = (double)value.Value;
                    return (number, FormatNumber(number, definition.Decimals));
                }
        }
    }

    private static string FormatNumber(double value, int decimals)
    {
        return value.ToString("F" + Math.Clamp(decimals, 0, 6), CultureInfo.InvariantCulture);
    }

    private static string? FormatTime(DateTime? time)
    {
        return time?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static Regex? BuildWildcardRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var text = pattern.Trim();
        var regex = "^" + Regex.Escape(text).Replace("\\*", ".*") + "$";
        return new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static HashSet<string> ParseDeviceNames(string? deviceNames)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(deviceNames)) return result;
        foreach (var item in deviceNames.Split(DeviceNameSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = item.Trim();
            if (name.Length > 0) result.Add(name);
        }
        return result;
    }

    private sealed record MetricJob(MetricDefinition Definition, long DeviceId, string DeviceName, List<string> Variables);
}
