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
/// 指标报表服务
/// </summary>
public interface IMetricReportService
{
    /// <summary>
    /// 获取指标定义（表为空时自动写入默认指标）
    /// </summary>
    Task<List<MetricDefinition>> GetMetricDefinitionsAsync();

    /// <summary>
    /// 生成指标报表（横向设备、纵向指标）
    /// </summary>
    Task<MetricReportResult> BuildAsync(MetricReportInput input);

    /// <summary>
    /// 保存指标定义（Id为0时新增）
    /// </summary>
    Task<bool> SaveMetricDefinitionAsync(MetricDefinition definition);

    /// <summary>
    /// 删除指标定义
    /// </summary>
    Task<bool> DeleteMetricDefinitionAsync(long id);
}
