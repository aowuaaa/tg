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

/// <summary>
/// 设备组态墙的单台设备
/// </summary>
public class DeviceWallItem
{
    /// <summary>
    /// 通道Id
    /// </summary>
    public long ChannelId { get; set; }

    /// <summary>
    /// 设备Id
    /// </summary>
    public long DeviceId { get; set; }

    /// <summary>
    /// 设备名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 通道名称
    /// </summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>
    /// 插件名称
    /// </summary>
    public string? PluginName { get; set; }

    /// <summary>
    /// 设备状态
    /// </summary>
    public DeviceStatusEnum DeviceStatus { get; set; }

    /// <summary>
    /// 是否为采集设备
    /// </summary>
    public bool? IsCollect { get; set; }

    /// <summary>
    /// 是否为内存设备
    /// </summary>
    public bool IsMemory { get; set; }

    /// <summary>
    /// 变量数量
    /// </summary>
    public int VariableTotal { get; set; }

    /// <summary>
    /// 在线变量数量
    /// </summary>
    public int VariableOnline { get; set; }

    /// <summary>
    /// 最近通讯时间
    /// </summary>
    public DateTime ActiveTime { get; set; }

    /// <summary>
    /// 最近错误信息
    /// </summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>
    /// 状态文案本地化键
    /// </summary>
    public string StatusKey => DeviceStatus switch
    {
        DeviceStatusEnum.OnLine => "Running",
        DeviceStatusEnum.OffLine => "Fault",
        DeviceStatusEnum.Pause => "Maintenance",
        _ => "Stopped",
    };

    /// <summary>
    /// 状态样式类
    /// </summary>
    public string StatusClass => DeviceStatus switch
    {
        DeviceStatusEnum.OnLine => "is-online",
        DeviceStatusEnum.OffLine => "is-fault",
        DeviceStatusEnum.Pause => "is-maintenance",
        _ => "is-stopped",
    };

    /// <summary>
    /// 采集质量指示灯样式类：全部在线绿、部分在线黄、全部掉线红、无变量灰
    /// </summary>
    public string LampClass
    {
        get
        {
            if (VariableTotal <= 0)
            {
                return "is-idle";
            }
            if (VariableOnline <= 0)
            {
                return "is-bad";
            }
            return VariableOnline >= VariableTotal ? "is-good" : "is-warn";
        }
    }

    /// <summary>
    /// 是否异常（非运行）
    /// </summary>
    public bool IsAbnormal => DeviceStatus != DeviceStatusEnum.OnLine;

    /// <summary>
    /// 设备图标
    /// </summary>
    public string Icon => IsMemory
        ? "fa-solid fa-memory"
        : IsCollect == true
            ? "fa-solid fa-industry"
            : IsCollect == false
                ? "fa-solid fa-microchip"
                : "fa-solid fa-server";

    /// <summary>
    /// 悬浮提示内容
    /// </summary>
    public string Tooltip => $"{Name}{Environment.NewLine}{ChannelName} · {PluginName}{Environment.NewLine}{VariableOnline}/{VariableTotal}";
}
