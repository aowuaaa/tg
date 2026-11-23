//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

namespace ThingsGateway.Gateway.Razor;

public partial class TopologyPage
{
    /// <summary>
    /// 每排设备数量
    /// </summary>
    private const int _columns = 8;

    [Inject]
    [NotNull]
    private IStringLocalizer<ThingsGateway.Gateway.Razor._Imports>? GatewayLocalizer { get; set; }

    [Inject]
    [NotNull]
    private DrawerService? DrawerService { get; set; }

    private readonly List<DeviceWallItem> _items = new();
    private readonly Dictionary<long, DeviceWallItem> _itemMap = new();

    private List<List<DeviceWallItem>> Rows { get; } = new();

    private CancellationTokenSource? _cts;
    private bool _loading = true;
    private bool _onlyAbnormal;
    private DateTime _now = DateTime.Now;

    private int _runningCount;
    private int _faultCount;
    private int _maintenanceCount;
    private int _stoppedCount;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            GlobalData.DeviceStatusChangeEvent += OnDeviceStatusChanged;
            await RefreshAsync().ConfigureAwait(false);
            _loading = false;
            _cts = new CancellationTokenSource();
            _ = RefreshLoopAsync(_cts.Token);
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
        await base.OnAfterRenderAsync(firstRender).ConfigureAwait(false);
    }

    public void Dispose()
    {
        GlobalData.DeviceStatusChangeEvent -= OnDeviceStatusChanged;
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        var tick = 0;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (++tick % 3 == 0)
                {
                    await RefreshAsync().ConfigureAwait(false);
                }
                await InvokeAsync(() =>
                {
                    _now = DateTime.Now;
                    StateHasChanged();
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnDeviceStatusChanged(DeviceRuntime deviceRuntime, DeviceBasicData deviceBasicData)
    {
        if (deviceRuntime == null) return;
        _ = InvokeAsync(() =>
        {
            if (!_itemMap.TryGetValue(deviceRuntime.Id, out var item)) return;
            if (!GlobalData.TryGetDeviceRuntime(deviceRuntime.Id, out var current)) return;
            FillItem(item, current, current.ChannelRuntime);
            RebuildRows();
            StateHasChanged();
        });
    }

    private async Task RefreshAsync()
    {
        var channels = await GlobalData.GetCurrentUserChannels().ConfigureAwait(false);
        await InvokeAsync(() => ApplyChannels(channels)).ConfigureAwait(false);
    }

    private void ApplyChannels(IEnumerable<ChannelRuntime>? channels)
    {
        var list = new List<DeviceWallItem>();
        foreach (var channel in channels ?? Enumerable.Empty<ChannelRuntime>())
        {
            if (channel?.ReadDeviceRuntimes == null) continue;
            foreach (var deviceRuntime in channel.ReadDeviceRuntimes.Values)
            {
                if (deviceRuntime == null) continue;
                if (!_itemMap.TryGetValue(deviceRuntime.Id, out var item))
                {
                    item = new DeviceWallItem { DeviceId = deviceRuntime.Id };
                    _itemMap[deviceRuntime.Id] = item;
                }
                FillItem(item, deviceRuntime, channel);
                list.Add(item);
            }
        }

        foreach (var removedId in _itemMap.Keys.Except(list.Select(a => a.DeviceId)).ToList())
        {
            _itemMap.Remove(removedId);
        }

        list.Sort(static (a, b) =>
        {
            var result = string.Compare(a.ChannelName, b.ChannelName, StringComparison.OrdinalIgnoreCase);
            return result != 0 ? result : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        _items.Clear();
        _items.AddRange(list);

        _runningCount = _items.Count(a => a.DeviceStatus == DeviceStatusEnum.OnLine);
        _faultCount = _items.Count(a => a.DeviceStatus == DeviceStatusEnum.OffLine);
        _maintenanceCount = _items.Count(a => a.DeviceStatus == DeviceStatusEnum.Pause);
        _stoppedCount = _items.Count(a => a.DeviceStatus is DeviceStatusEnum.Default);

        RebuildRows();
    }

    private void RebuildRows()
    {
        var source = _onlyAbnormal ? _items.Where(a => a.IsAbnormal) : _items;
        Rows.Clear();
        foreach (var chunk in source.Chunk(_columns))
        {
            Rows.Add(chunk.ToList());
        }
    }

    private static void FillItem(DeviceWallItem item, DeviceRuntime deviceRuntime, ChannelRuntime? channelRuntime)
    {
        item.ChannelId = deviceRuntime.ChannelId;
        item.Name = deviceRuntime.Name;
        item.ChannelName = string.IsNullOrEmpty(deviceRuntime.ChannelName)
            ? channelRuntime?.Name ?? string.Empty
            : deviceRuntime.ChannelName;
        item.PluginName = deviceRuntime.PluginName;
        item.DeviceStatus = deviceRuntime.DeviceStatus;
        item.IsCollect = deviceRuntime.IsCollect;
        item.IsMemory = deviceRuntime.IsMemory || deviceRuntime.Id == MemoryConst.MemoryDeviceId;
        item.ActiveTime = deviceRuntime.ActiveTime;
        item.LastErrorMessage = deviceRuntime.LastErrorMessage;

        var variables = GetDeviceVariables(deviceRuntime);
        var total = 0;
        var online = 0;
        foreach (var variable in variables)
        {
            total++;
            if (variable.IsOnline) online++;
        }
        item.VariableTotal = total;
        item.VariableOnline = online;
    }

    private static IEnumerable<VariableRuntime> GetDeviceVariables(DeviceRuntime deviceRuntime)
    {
        if (deviceRuntime.Id == MemoryConst.MemoryDeviceId)
        {
            return GlobalData.MemoryVariableRuntimes.Select(a => a.Value);
        }
        if (deviceRuntime.IsCollect == true)
        {
            return deviceRuntime.ReadOnlyVariableRuntimes?.Select(a => a.Value) ?? Enumerable.Empty<VariableRuntime>();
        }
        return deviceRuntime.Driver?.IdVariableRuntimes?.Select(a => a.Value) ?? Enumerable.Empty<VariableRuntime>();
    }

    private void ToggleOnlyAbnormal()
    {
        _onlyAbnormal = !_onlyAbnormal;
        RebuildRows();
    }

    private async Task ShowDeviceDetailAsync(DeviceWallItem item)
    {
        if (item == null) return;
        if (!GlobalData.TryGetDeviceRuntime(item.DeviceId, out var deviceRuntime))
        {
            return;
        }

        var variables = GetDeviceVariables(deviceRuntime);
        var selectModel = new ChannelDeviceTreeItem
        {
            Id = item.DeviceId,
            DeviceRuntimeId = item.DeviceId,
            ChannelRuntimeId = item.ChannelId,
            ChannelDevicePluginType = ChannelDevicePluginTypeEnum.Device
        };

        await DrawerService.Show(new DrawerOption()
        {
            Class = "h-100",
            Width = "80%",
            Placement = Placement.Right,
            ChildContent = BootstrapDynamicComponent.CreateComponent<VariableRuntimeInfo>(new Dictionary<string, object?>
            {
                { nameof(VariableRuntimeInfo.Items), variables },
                { nameof(VariableRuntimeInfo.SelectModel), selectModel },
            }).Render(),
            ShowBackdrop = true,
            AllowResize = true,
            IsBackdrop = true
        }).ConfigureAwait(false);
    }
}
