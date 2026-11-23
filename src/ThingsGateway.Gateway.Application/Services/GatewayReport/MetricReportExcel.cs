//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using System.Data;

using MiniExcelLibs;

namespace ThingsGateway.Gateway.Application;

/// <summary>
/// 指标报表导出
/// </summary>
public static class MetricReportExcel
{
    /// <summary>
    /// 导出文件名
    /// </summary>
    public static string GetFileName(MetricReportInput? input, MetricReportResult result)
    {
        var defaultName = $"指标报表_{result.StartTime:yyyyMMddHHmm}_{result.EndTime:yyyyMMddHHmm}.xlsx";
        if (string.IsNullOrWhiteSpace(input?.FileName)) return defaultName;

        var name = Path.GetFileName(input!.FileName!.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? defaultName : name;
    }

    /// <summary>
    /// 生成xlsx内容，第一行为设备名称，第一列为指标名称
    /// </summary>
    public static byte[] Build(MetricReportResult result)
    {
        using var table = ToDataTable(result);
        using var stream = new MemoryStream();
        stream.SaveAs(table);
        return stream.ToArray();
    }

    /// <summary>
    /// 转为DataTable
    /// </summary>
    public static DataTable ToDataTable(MetricReportResult result)
    {
        var table = new DataTable("指标报表");
        table.Columns.Add("分组", typeof(string));
        table.Columns.Add("指标", typeof(string));
        table.Columns.Add("单位", typeof(string));
        foreach (var device in result.Devices)
        {
            var name = device.Name;
            var index = 2;
            while (table.Columns.Contains(name))
            {
                name = $"{device.Name}({++index})";
            }
            table.Columns.Add(name, typeof(object));
        }

        foreach (var row in result.Rows)
        {
            var dataRow = table.NewRow();
            dataRow["分组"] = row.GroupName ?? string.Empty;
            dataRow["指标"] = row.Name;
            dataRow["单位"] = row.Unit ?? string.Empty;

            for (var i = 0; i < result.Devices.Count; i++)
            {
                var number = i < row.Numbers.Count ? row.Numbers[i] : null;
                var display = i < row.Displays.Count ? row.Displays[i] : string.Empty;

                if (number.HasValue)
                {
                    dataRow[i + 3] = number.Value;
                }
                else if (!string.IsNullOrEmpty(display))
                {
                    dataRow[i + 3] = display;
                }
                else
                {
                    dataRow[i + 3] = DBNull.Value;
                }
            }

            table.Rows.Add(dataRow);
        }

        return table;
    }
}
