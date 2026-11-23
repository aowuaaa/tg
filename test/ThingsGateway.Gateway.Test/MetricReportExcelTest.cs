using ThingsGateway.Gateway.Application;

using Xunit;

namespace ThingsGateway.Gateway.Test;

public class MetricReportExcelTest
{
    private static MetricReportResult CreateResult()
    {
        var result = new MetricReportResult
        {
            StartTime = new DateTime(2024, 1, 1),
            EndTime = new DateTime(2024, 1, 2),
        };
        result.Devices.Add(new MetricReportDevice { Id = 1, Name = "设备1" });
        result.Rows.Add(new MetricReportRow
        {
            Name = "运行时间",
            GroupName = "运行",
            Unit = "h",
            Displays = { "1.50" },
            Numbers = { 1.5d },
        });
        return result;
    }

    [Fact]
    public void ToDataTable_Should_Keep_Numeric_Value()
    {
        var table = MetricReportExcel.ToDataTable(CreateResult());

        Assert.Equal(4, table.Columns.Count);
        Assert.Equal("设备1", table.Columns[3].ColumnName);
        Assert.Equal(1, table.Rows.Count);
        Assert.Equal(1.5d, table.Rows[0][3]);
    }

    [Fact]
    public void Build_Should_Return_Xlsx_Bytes()
    {
        var bytes = MetricReportExcel.Build(CreateResult());

        Assert.NotEmpty(bytes);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public void GetFileName_Should_Strip_Path()
    {
        var result = CreateResult();
        var input = new MetricReportInput { FileName = @"..\..\report.xlsx" };

        Assert.Equal("report.xlsx", MetricReportExcel.GetFileName(input, result));
        Assert.EndsWith(".xlsx", MetricReportExcel.GetFileName(null, result));
    }
}
