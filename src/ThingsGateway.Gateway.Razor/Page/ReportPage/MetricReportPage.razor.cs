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

namespace ThingsGateway.Gateway.Razor;

public partial class MetricReportPage
{
    private const string DefaultFileName = "指标报表";

    [Inject]
    [NotNull]
    private IStringLocalizer<ThingsGateway.Gateway.Razor._Imports>? GatewayLocalizer { get; set; }

    [Inject]
    [NotNull]
    private IMetricReportService? MetricReportService { get; set; }

    [Inject]
    [NotNull]
    private IGatewayExportService? GatewayExportService { get; set; }

    private readonly List<DeviceOption> _devices = new();
    private readonly List<MetricOption> _metrics = new();
    private readonly List<MetricConfigItem> _metricConfigs = new();
    private readonly List<string> _messages = new();
    private List<MetricDefinition> _allMetrics = new();

    private static readonly MetricTypeEnum[] MetricTypes = Enum.GetValues<MetricTypeEnum>();

    private static readonly string[] TimeInputFormats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss.FFFFFFF"];

    private DateTime _startTime = DateTime.Today;
    private DateTime _endTime = DateTime.Today.AddDays(1);
    private string _startTimeText = FormatTimeInput(DateTime.Today);
    private string _endTimeText = FormatTimeInput(DateTime.Today.AddDays(1));
    private string? _deviceSearch;
    private bool _busy;
    private bool _exporting;
    private bool _loaded;
    private bool _metricConfigOpen;
    private bool _savingMetricConfig;
    private MetricReportResult? _result;

    private IEnumerable<DeviceOption> VisibleDevices
    {
        get
        {
            var keyword = _deviceSearch?.Trim();
            if (string.IsNullOrEmpty(keyword)) return _devices;
            return _devices.Where(a => a.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_loaded)
        {
            await LoadOptionsAsync().ConfigureAwait(false);
        }
        await base.OnAfterRenderAsync(firstRender).ConfigureAwait(false);
    }

    private async Task LoadOptionsAsync()
    {
        try
        {
            var metrics = await MetricReportService!.GetMetricDefinitionsAsync().ConfigureAwait(false);
            var devices = (await GlobalData.GetCurrentUserDevices().ConfigureAwait(false))
                .Where(a => a.ReadOnlyVariableRuntimes is { Count: > 0 })
                .OrderBy(a => a.Name, NaturalStringComparer.Instance)
                .ToList();

            await InvokeAsync(() =>
            {
                _allMetrics = metrics;
                _metrics.Clear();
                foreach (var metric in metrics.Where(a => a.Visible))
                {
                    _metrics.Add(new MetricOption { Id = metric.Id, Name = metric.Name, Unit = metric.Unit });
                }

                _devices.Clear();
                foreach (var device in devices)
                {
                    _devices.Add(new DeviceOption { Id = device.Id, Name = device.Name });
                }

                if (_devices.Count == 0) _messages.Add(GatewayLocalizer!["NoCollectDeviceTip"]);
                _loaded = true;
                StateHasChanged();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await InvokeAsync(() =>
            {
                _messages.Add(ex.Message);
                _loaded = true;
                StateHasChanged();
            }).ConfigureAwait(false);
        }
    }

    private void SetRange(int days)
    {
        if (days == 0)
        {
            ApplyRange(DateTime.Today, DateTime.Today.AddDays(1));
        }
        else if (days == 1)
        {
            ApplyRange(DateTime.Today.AddDays(-1), DateTime.Today);
        }
        else
        {
            ApplyRange(DateTime.Today.AddDays(-days + 1), DateTime.Today.AddDays(1));
        }
    }

    private void SetThisMonth()
    {
        var today = DateTime.Today;
        var start = new DateTime(today.Year, today.Month, 1);
        ApplyRange(start, start.AddMonths(1));
    }

    private void ApplyRange(DateTime start, DateTime end)
    {
        _startTime = TrimToMinute(start);
        _endTime = TrimToMinute(end);
        _startTimeText = FormatTimeInput(_startTime);
        _endTimeText = FormatTimeInput(_endTime);
    }

    private void OnStartTimeChanged(ChangeEventArgs e)
    {
        if (ParseTimeInput(e.Value) is DateTime start)
        {
            _startTime = start;
            _startTimeText = FormatTimeInput(start);
        }
        else
        {
            _startTimeText = FormatTimeInput(_startTime);
        }
    }

    private void OnEndTimeChanged(ChangeEventArgs e)
    {
        if (ParseTimeInput(e.Value) is DateTime end)
        {
            _endTime = end;
            _endTimeText = FormatTimeInput(end);
        }
        else
        {
            _endTimeText = FormatTimeInput(_endTime);
        }
    }

    private static DateTime? ParseTimeInput(object? value)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (DateTime.TryParseExact(text, TimeInputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return TrimToMinute(parsed);
        }
        return null;
    }

    private static string FormatTimeInput(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    private static DateTime TrimToMinute(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);

    private void SelectAllDevices(bool selected)
    {
        foreach (var item in _devices) item.Selected = selected;
    }

    private void SelectAllMetrics(bool selected)
    {
        foreach (var item in _metrics) item.Selected = selected;
    }

    private void ClearMessages()
    {
        _messages.Clear();
    }

    private void OpenMetricConfig()
    {
        ClearMessages();
        _metricConfigs.Clear();
        foreach (var definition in _allMetrics)
        {
            _metricConfigs.Add(MetricConfigItem.FromDefinition(definition));
        }
        _metricConfigOpen = true;
    }

    private void CloseMetricConfig()
    {
        if (_savingMetricConfig) return;
        _metricConfigOpen = false;
        _metricConfigs.Clear();
        ClearMessages();
    }

    private void AddMetricConfig()
    {
        _metricConfigs.Add(new MetricConfigItem
        {
            MetricType = MetricTypeEnum.StateDuration,
            Decimals = 2,
            Visible = true,
            SortCode = _metricConfigs.Count == 0 ? 10 : _metricConfigs.Max(a => a.SortCode) + 10,
        });
    }

    private void ToggleRemoveMetric(MetricConfigItem item)
    {
        if (item.Id <= 0)
        {
            _metricConfigs.Remove(item);
            return;
        }
        item.Removed = !item.Removed;
    }

    private async Task SaveMetricConfigsAsync()
    {
        ClearMessages();
        if (_metricConfigs.Any(a => !a.Removed && string.IsNullOrWhiteSpace(a.Name)))
        {
            _messages.Add(GatewayLocalizer!["MetricNameRequiredTip"]);
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
            return;
        }

        _savingMetricConfig = true;
        await InvokeAsync(StateHasChanged).ConfigureAwait(false);

        var failed = false;
        try
        {
            foreach (var item in _metricConfigs.ToList())
            {
                if (item.Removed)
                {
                    if (item.Id > 0 && !await MetricReportService!.DeleteMetricDefinitionAsync(item.Id).ConfigureAwait(false))
                    {
                        failed = true;
                    }
                    continue;
                }
                if (!await MetricReportService!.SaveMetricDefinitionAsync(item.ToDefinition()).ConfigureAwait(false))
                {
                    failed = true;
                }
            }
        }
        catch (Exception ex)
        {
            _messages.Add(ex.Message);
            failed = true;
        }

        _savingMetricConfig = false;
        if (failed)
        {
            _messages.Add(GatewayLocalizer!["MetricSaveFailTip"]);
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
            return;
        }

        await ReloadMetricsAsync().ConfigureAwait(false);
        await InvokeAsync(() =>
        {
            _metricConfigOpen = false;
            _metricConfigs.Clear();
            _messages.Add(GatewayLocalizer!["MetricSaveSuccessTip"]);
            StateHasChanged();
        }).ConfigureAwait(false);
    }

    private async Task ReloadMetricsAsync()
    {
        var metrics = await MetricReportService!.GetMetricDefinitionsAsync().ConfigureAwait(false);
        await InvokeAsync(() =>
        {
            var selected = _metrics.Where(a => a.Selected).Select(a => a.Id).ToHashSet();
            _allMetrics = metrics;
            _metrics.Clear();
            foreach (var metric in metrics.Where(a => a.Visible))
            {
                _metrics.Add(new MetricOption
                {
                    Id = metric.Id,
                    Name = metric.Name,
                    Unit = metric.Unit,
                    Selected = selected.Count == 0 || selected.Contains(metric.Id),
                });
            }
            StateHasChanged();
        }).ConfigureAwait(false);
    }

    private string GetMetricTypeName(MetricTypeEnum type)
    {
        var text = GatewayLocalizer!["MetricType_" + type];
        return text.ResourceNotFound ? type.ToString() : text.Value;
    }

    private MetricReportInput? BuildInput(bool forExport)
    {
        ClearMessages();

        if (_endTime <= _startTime)
        {
            _messages.Add(GatewayLocalizer!["ReportTimeRangeInvalidTip"]);
            return null;
        }
        if (!_devices.Any(a => a.Selected))
        {
            _messages.Add(GatewayLocalizer!["ReportDeviceRequiredTip"]);
            return null;
        }
        if (!_metrics.Any(a => a.Selected))
        {
            _messages.Add(GatewayLocalizer!["ReportMetricRequiredTip"]);
            return null;
        }

        return new MetricReportInput
        {
            StartTime = _startTime,
            EndTime = _endTime,
            DeviceIds = _devices.Where(a => a.Selected).Select(a => a.Id).ToList(),
            MetricIds = _metrics.Where(a => a.Selected).Select(a => a.Id).ToList(),
            FileName = forExport
                ? $"{DefaultFileName}_{_startTime:yyyyMMddHHmm}_{_endTime:yyyyMMddHHmm}.xlsx"
                : null,
        };
    }

    private async Task PreviewAsync()
    {
        var input = BuildInput(false);
        if (input == null)
        {
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
            return;
        }

        _busy = true;
        _result = null;
        await InvokeAsync(StateHasChanged).ConfigureAwait(false);

        try
        {
            var result = await MetricReportService!.BuildAsync(input).ConfigureAwait(false);
            await InvokeAsync(() =>
            {
                _result = result;
                _messages.Clear();
                _messages.AddRange(result.Messages);
                if (!result.HasData && result.Messages.Count == 0)
                {
                    _messages.Add(GatewayLocalizer!["ReportNoDataTip"]);
                }
                StateHasChanged();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await InvokeAsync(() =>
            {
                _messages.Add(ex.Message);
                StateHasChanged();
            }).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
    }

    private async Task ExportAsync()
    {
        var input = BuildInput(true);
        if (input == null)
        {
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
            return;
        }

        _exporting = true;
        await InvokeAsync(StateHasChanged).ConfigureAwait(false);

        try
        {
            var success = await GatewayExportService!.OnMetricReportExport(input).ConfigureAwait(false);
            if (!success)
            {
                await InvokeAsync(() =>
                {
                    _messages.Add(GatewayLocalizer!["ReportExportFailTip"]);
                    StateHasChanged();
                }).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await InvokeAsync(() =>
            {
                _messages.Add(ex.Message);
                StateHasChanged();
            }).ConfigureAwait(false);
        }
        finally
        {
            _exporting = false;
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
    }

    private sealed class DeviceOption
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Selected { get; set; } = true;
    }

    private sealed class MetricOption
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public bool Selected { get; set; } = true;
    }

    private sealed class MetricConfigItem
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? GroupName { get; set; }
        public MetricTypeEnum MetricType { get; set; }
        public string? VariableName { get; set; }
        public string? DeviceNames { get; set; }
        public string? StateValue { get; set; }
        public string? Unit { get; set; }
        public int Decimals { get; set; } = 2;
        public int SortCode { get; set; }
        public bool Visible { get; set; } = true;
        public bool Removed { get; set; }

        public static MetricConfigItem FromDefinition(MetricDefinition definition)
        {
            return new MetricConfigItem
            {
                Id = definition.Id,
                Name = definition.Name,
                GroupName = definition.GroupName,
                MetricType = definition.MetricType,
                VariableName = definition.VariableName,
                DeviceNames = definition.DeviceNames,
                StateValue = definition.StateValue,
                Unit = definition.Unit,
                Decimals = definition.Decimals,
                SortCode = definition.SortCode,
                Visible = definition.Visible,
            };
        }

        public MetricDefinition ToDefinition()
        {
            return new MetricDefinition
            {
                Id = Id,
                Name = Name?.Trim() ?? string.Empty,
                GroupName = GroupName,
                MetricType = MetricType,
                VariableName = VariableName,
                DeviceNames = DeviceNames,
                StateValue = StateValue,
                Unit = Unit,
                Decimals = Decimals,
                SortCode = SortCode,
                Visible = Visible,
            };
        }
    }
}
