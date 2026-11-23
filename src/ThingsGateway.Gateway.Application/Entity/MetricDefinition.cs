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
/// 指标定义表
/// </summary>
[OrmTable("metric_definition", TableDescription = "指标定义表")]
[Tenant(SqlOrmConst.DB_Custom)]
public class MetricDefinition : PrimaryKeyEntity
{
    /// <summary>
    /// 指标名称（报表行标题）
    /// </summary>
    [OrmColumn(ColumnDescription = "指标名称", Length = 100)]
    public virtual string Name { get; set; } = string.Empty;

    /// <summary>
    /// 指标分组
    /// </summary>
    [OrmColumn(ColumnDescription = "指标分组", Length = 100, IsNullable = true)]
    public virtual string? GroupName { get; set; }

    /// <summary>
    /// 计算类型
    /// </summary>
    [OrmColumn(ColumnDescription = "计算类型")]
    public virtual MetricTypeEnum MetricType { get; set; }

    /// <summary>
    /// 变量名称，支持*通配；离线时长/在线率可不填
    /// </summary>
    [OrmColumn(ColumnDescription = "变量名称", Length = 200, IsNullable = true)]
    public virtual string? VariableName { get; set; }

    /// <summary>
    /// 限定设备名称，逗号分隔；为空表示全部设备
    /// </summary>
    [OrmColumn(ColumnDescription = "限定设备", Length = 2000, IsNullable = true)]
    public virtual string? DeviceNames { get; set; }

    /// <summary>
    /// 状态真值，默认1，支持 true/false
    /// </summary>
    [OrmColumn(ColumnDescription = "状态真值", Length = 50, IsNullable = true)]
    public virtual string? StateValue { get; set; }

    /// <summary>
    /// 单位
    /// </summary>
    [OrmColumn(ColumnDescription = "单位", Length = 20, IsNullable = true)]
    public virtual string? Unit { get; set; }

    /// <summary>
    /// 小数位
    /// </summary>
    [OrmColumn(ColumnDescription = "小数位")]
    public virtual int Decimals { get; set; } = 2;

    /// <summary>
    /// 是否显示
    /// </summary>
    [OrmColumn(ColumnDescription = "是否显示")]
    public virtual bool Visible { get; set; } = true;

    /// <summary>
    /// 排序
    /// </summary>
    [OrmColumn(ColumnDescription = "排序")]
    public virtual int SortCode { get; set; }

    public override string ToString()
    {
        return Name;
    }
}
