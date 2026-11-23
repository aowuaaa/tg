//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using System.Globalization;

namespace ThingsGateway.Gateway.Application;

/// <summary>
/// 报表原始数据点
/// </summary>
public readonly record struct ReportPoint(DateTime Time, decimal Value, bool IsOnline);

/// <summary>
/// 报表分段（一段时间内值保持不变）
/// </summary>
public readonly record struct ReportSegment(DateTime Start, DateTime End, decimal Value, bool IsOnline)
{
    /// <summary>
    /// 分段时长
    /// </summary>
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// 指标计算器（纯函数，方便单元测试与后续扩展）
/// </summary>
public static class MetricCalculator
{
    /// <summary>
    /// 状态值比较精度
    /// </summary>
    public const decimal StateEpsilon = 0.000001m;

    /// <summary>
    /// 解析状态真值，默认1，支持 true/false
    /// </summary>
    public static decimal ParseStateValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1m;
        var value = text.Trim();
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1m;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0m;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 1m;
    }

    /// <summary>
    /// 数值是否等于状态真值
    /// </summary>
    public static bool MatchState(decimal value, decimal stateValue)
    {
        return Math.Abs(value - stateValue) < StateEpsilon;
    }

    /// <summary>
    /// 根据采样间隔推算允许的最大空档（间隔中位数*3，限制在30秒~6小时）
    /// </summary>
    public static TimeSpan ResolveMaxGap(IReadOnlyList<ReportPoint> points, TimeSpan? fallback = null)
    {
        var result = fallback ?? TimeSpan.FromMinutes(5);
        if (points == null || points.Count < 3) return result;

        var gaps = new List<double>(points.Count - 1);
        for (var i = 1; i < points.Count; i++)
        {
            gaps.Add((points[i].Time - points[i - 1].Time).TotalSeconds);
        }
        gaps.Sort();
        var median = gaps[gaps.Count / 2];
        if (median <= 0) return result;

        return TimeSpan.FromSeconds(Math.Clamp(median * 3, 30, 6 * 3600));
    }

    /// <summary>
    /// 把原始点转换为分段；区间起点前最后一个点作为基线，保证"区间开始时已在运行"能正确统计。
    /// 超过最大空档的时间不参与统计（视为未知/离线）。
    /// </summary>
    public static List<ReportSegment> BuildSegments(IReadOnlyList<ReportPoint> points, DateTime start, DateTime end, TimeSpan maxGap)
    {
        var segments = new List<ReportSegment>();
        if (points == null || points.Count == 0 || end <= start) return segments;
        if (maxGap <= TimeSpan.Zero) maxGap = TimeSpan.FromMinutes(5);

        var index = -1;
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i].Time <= start) index = i;
            else break;
        }
        if (index < 0) index = 0;

        for (var i = index; i < points.Count; i++)
        {
            var current = points[i];
            if (current.Time >= end) break;

            var segmentStart = current.Time < start ? start : current.Time;
            var limit = current.Time + maxGap;
            var segmentEnd = end;
            if (i + 1 < points.Count && points[i + 1].Time < segmentEnd) segmentEnd = points[i + 1].Time;
            if (limit < segmentEnd) segmentEnd = limit;

            if (segmentEnd > segmentStart)
                segments.Add(new ReportSegment(segmentStart, segmentEnd, current.Value, current.IsOnline));
        }

        return segments;
    }

    /// <summary>
    /// 在线秒数
    /// </summary>
    public static double OnlineSeconds(IReadOnlyList<ReportSegment> segments)
    {
        var total = 0d;
        foreach (var segment in segments)
        {
            if (segment.IsOnline) total += segment.Duration.TotalSeconds;
        }
        return total;
    }

    /// <summary>
    /// 在线时长（小时）
    /// </summary>
    public static double OnlineHours(IReadOnlyList<ReportSegment> segments)
    {
        return OnlineSeconds(segments) / 3600d;
    }

    /// <summary>
    /// 离线时长（小时），超过最大空档的未知时间按离线计算
    /// </summary>
    public static double OfflineHours(IReadOnlyList<ReportSegment> segments, DateTime start, DateTime end)
    {
        var total = (end - start).TotalSeconds;
        if (total <= 0) return 0;
        var offline = total - OnlineSeconds(segments);
        return offline <= 0 ? 0 : offline / 3600d;
    }

    /// <summary>
    /// 在线率（百分比）
    /// </summary>
    public static double OnlineRate(IReadOnlyList<ReportSegment> segments, DateTime start, DateTime end)
    {
        var total = (end - start).TotalSeconds;
        if (total <= 0) return 0;
        var rate = OnlineSeconds(segments) / total * 100d;
        return Math.Clamp(rate, 0, 100);
    }

    /// <summary>
    /// 处于指定状态的时长（小时，仅统计在线时段）
    /// </summary>
    public static double StateHours(IReadOnlyList<ReportSegment> segments, decimal stateValue)
    {
        var total = 0d;
        foreach (var segment in segments)
        {
            if (segment.IsOnline && MatchState(segment.Value, stateValue)) total += segment.Duration.TotalSeconds;
        }
        return total / 3600d;
    }

    /// <summary>
    /// 进入指定状态的次数
    /// </summary>
    public static int CountStateEntries(IReadOnlyList<ReportSegment> segments, decimal stateValue)
    {
        var count = 0;
        var previous = false;
        foreach (var segment in segments)
        {
            var current = MatchState(segment.Value, stateValue);
            if (current && !previous) count++;
            previous = current;
        }
        return count;
    }

    /// <summary>
    /// 首次进入指定状态的时刻（区间开始时已处于该状态则返回区间开始时间）
    /// </summary>
    public static DateTime? FirstStateTimestamp(IReadOnlyList<ReportSegment> segments, decimal stateValue)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            var current = MatchState(segments[i].Value, stateValue);
            if (current && (i == 0 || !MatchState(segments[i - 1].Value, stateValue))) return segments[i].Start;
        }
        return null;
    }

    /// <summary>
    /// 最后一次进入指定状态的时刻
    /// </summary>
    public static DateTime? LastStateTimestamp(IReadOnlyList<ReportSegment> segments, decimal stateValue)
    {
        DateTime? result = null;
        for (var i = 0; i < segments.Count; i++)
        {
            var current = MatchState(segments[i].Value, stateValue);
            if (current && (i == 0 || !MatchState(segments[i - 1].Value, stateValue))) result = segments[i].Start;
        }
        return result;
    }

    /// <summary>
    /// 累计差值，自动识别计数器复位
    /// </summary>
    public static decimal? CounterDelta(IReadOnlyList<ReportPoint> points, DateTime start, DateTime end)
    {
        if (points == null || points.Count == 0) return null;

        decimal? previous = null;
        var total = 0m;
        var any = false;
        foreach (var point in points)
        {
            if (point.Time > end) break;
            if (point.Time < start)
            {
                previous = point.Value;
                continue;
            }
            if (previous.HasValue)
            {
                total += point.Value >= previous.Value ? point.Value - previous.Value : point.Value;
                any = true;
            }
            previous = point.Value;
        }
        return any ? total : null;
    }

    /// <summary>
    /// 区间内统计值
    /// </summary>
    public static (decimal? Avg, decimal? Max, decimal? Min, decimal? Last) Aggregate(IReadOnlyList<ReportPoint> points, DateTime start, DateTime end)
    {
        if (points == null || points.Count == 0) return (null, null, null, null);

        decimal sum = 0;
        decimal? max = null;
        decimal? min = null;
        decimal? last = null;
        var count = 0;
        foreach (var point in points)
        {
            if (point.Time < start || point.Time > end) continue;
            sum += point.Value;
            count++;
            if (max == null || point.Value > max) max = point.Value;
            if (min == null || point.Value < min) min = point.Value;
            last = point.Value;
        }
        return count == 0 ? (null, null, null, null) : (sum / count, max, min, last);
    }

    /// <summary>
    /// 等分降采样，每个桶保留最后一个点（保留状态跳变）
    /// </summary>
    public static List<ReportPoint> Downsample(IReadOnlyList<ReportPoint> points, int maxPoints)
    {
        if (points == null) return new List<ReportPoint>();
        if (maxPoints <= 2 || points.Count <= maxPoints) return points.ToList();

        var first = points[0].Time;
        var last = points[^1].Time;
        var totalSeconds = (last - first).TotalSeconds;
        if (totalSeconds <= 0) return points.ToList();

        var bucketSeconds = totalSeconds / (maxPoints - 1);
        var result = new List<ReportPoint>(maxPoints) { points[0] };
        var bucketIndex = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var point = points[i];
            var index = (int)((point.Time - first).TotalSeconds / bucketSeconds);
            if (index != bucketIndex)
            {
                result.Add(point);
                bucketIndex = index;
            }
            else if (result.Count > 1)
            {
                result[^1] = point;
            }
        }
        return result;
    }
}
