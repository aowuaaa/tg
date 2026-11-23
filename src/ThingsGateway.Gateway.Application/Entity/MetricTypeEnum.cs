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
/// 指标计算类型
/// </summary>
public enum MetricTypeEnum
{
    /// <summary>
    /// 状态持续时长（运行时间/停机时间），单位小时
    /// </summary>
    StateDuration = 0,

    /// <summary>
    /// 状态进入次数（运行次数/报警次数）
    /// </summary>
    StateCount = 1,

    /// <summary>
    /// 累计值差值（产量/累计流量），自动识别复位
    /// </summary>
    CounterDelta = 2,

    /// <summary>
    /// 首次进入状态时刻
    /// </summary>
    FirstTimestamp = 3,

    /// <summary>
    /// 最后一次进入状态时刻
    /// </summary>
    LastTimestamp = 4,

    /// <summary>
    /// 平均值
    /// </summary>
    Avg = 5,

    /// <summary>
    /// 最大值
    /// </summary>
    Max = 6,

    /// <summary>
    /// 最小值
    /// </summary>
    Min = 7,

    /// <summary>
    /// 末次值
    /// </summary>
    LastValue = 8,

    /// <summary>
    /// 在线率（百分比）
    /// </summary>
    OnlineRate = 9,

    /// <summary>
    /// 离线时长，单位小时
    /// </summary>
    OfflineDuration = 10,
}
