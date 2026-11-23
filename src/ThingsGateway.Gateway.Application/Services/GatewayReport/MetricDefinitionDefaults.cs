//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using ThingsGateway.Foundation.Common.Log;

namespace ThingsGateway.Gateway.Application;

/// <summary>
/// 指标定义默认值（内嵌资源）
/// </summary>
internal static class MetricDefinitionDefaults
{
    private const string ResourceName = "seed_metric_definition.json";

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<MetricDefinition> Load()
    {
        try
        {
            var assembly = typeof(MetricDefinitionDefaults).Assembly;
            var name = assembly.GetManifestResourceNames().FirstOrDefault(a => a.EndsWith(ResourceName, StringComparison.OrdinalIgnoreCase));
            if (name == null) return new List<MetricDefinition>();
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) return new List<MetricDefinition>();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<List<MetricDefinition>>(json, _options) ?? new List<MetricDefinition>();
        }
        catch (Exception ex)
        {
            XTrace.WriteLine($"Load metric definition seed fail: {ex}");
            return new List<MetricDefinition>();
        }
    }

    public static string Serialize(IEnumerable<MetricDefinition> definitions)
    {
        return JsonSerializer.Serialize(definitions, _options);
    }
}
