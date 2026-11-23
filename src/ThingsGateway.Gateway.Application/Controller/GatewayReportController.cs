//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ThingsGateway.Gateway.Application;

/// <summary>
/// 指标报表
/// </summary>
[ApiDescriptionSettings(false)]
[Route("api/gatewayReport")]
[IgnoreRolePermission]
[Authorize]
public class GatewayReportController : ControllerBase
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IMetricReportService _metricReportService;

    public GatewayReportController(IMetricReportService metricReportService)
    {
        _metricReportService = metricReportService;
    }

    /// <summary>
    /// 指标定义列表
    /// </summary>
    [HttpGet("metrics")]
    public async Task<List<MetricDefinition>> GetMetricsAsync()
    {
        return await _metricReportService.GetMetricDefinitionsAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 预览报表
    /// </summary>
    [HttpPost("preview")]
    public async Task<MetricReportResult> PreviewAsync([FromBody] MetricReportInput input)
    {
        return await _metricReportService.BuildAsync(input).ConfigureAwait(false);
    }

    /// <summary>
    /// 导出报表
    /// </summary>
    [HttpPost("export")]
    public async Task<IActionResult> ExportAsync([FromBody] MetricReportInput input)
    {
        var result = await _metricReportService.BuildAsync(input).ConfigureAwait(false);
        var bytes = MetricReportExcel.Build(result);
        var fileName = MetricReportExcel.GetFileName(input, result);
        return File(bytes, ContentType, fileName);
    }
}
