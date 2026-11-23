//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

namespace ThingsGateway.Gateway.Application;

/// <summary>
/// 指标报表查询参数
/// </summary>
public class MetricReportInput
{
    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.Today;

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime EndTime { get; set; } = DateTime.Today.AddDays(1);

    /// <summary>
    /// 设备ID，为空表示全部设备
    /// </summary>
    public List<long>? DeviceIds { get; set; }

    /// <summary>
    /// 指标ID，为空表示全部指标
    /// </summary>
    public List<long>? MetricIds { get; set; }

    /// <summary>
    /// 区间起点向前回溯小时数，用于获取区间起点的状态基线（默认24小时）
    /// </summary>
    public int LookbackHours { get; set; } = 24;

    /// <summary>
    /// 文件名，导出时使用
    /// </summary>
    public string? FileName { get; set; }
}

/// <summary>
/// 报表设备列
/// </summary>
public class MetricReportDevice
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 报表指标行
/// </summary>
public class MetricReportRow
{
    /// <summary>
    /// 指标ID
    /// </summary>
    public long MetricId { get; set; }

    /// <summary>
    /// 指标名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 指标分组
    /// </summary>
    public string? GroupName { get; set; }

    /// <summary>
    /// 单位
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// 计算类型
    /// </summary>
    public MetricTypeEnum MetricType { get; set; }

    /// <summary>
    /// 显示文本，与报表设备列一一对应，空字符串表示无数据
    /// </summary>
    public List<string> Displays { get; set; } = new();

    /// <summary>
    /// 数值，与报表设备列一一对应，为空表示该项不是数值类型或无数据
    /// </summary>
    public List<double?> Numbers { get; set; } = new();
}

/// <summary>
/// 指标报表结果（横向设备、纵向指标）
/// </summary>
public class MetricReportResult
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 设备列
    /// </summary>
    public List<MetricReportDevice> Devices { get; set; } = new();

    /// <summary>
    /// 指标行
    /// </summary>
    public List<MetricReportRow> Rows { get; set; } = new();

    /// <summary>
    /// 提示信息
    /// </summary>
    public List<string> Messages { get; set; } = new();

    /// <summary>
    /// 是否有数据
    /// </summary>
    public bool HasData => Devices.Count > 0 && Rows.Any(a => a.Displays.Any(b => !string.IsNullOrEmpty(b)));
}
