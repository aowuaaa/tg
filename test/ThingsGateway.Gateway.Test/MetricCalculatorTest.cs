using System;
using System.Collections.Generic;
using System.Linq;

using ThingsGateway.Gateway.Application;

using Xunit;

namespace ThingsGateway.Gateway.Test;

public class MetricCalculatorTest
{
    private static readonly DateTime _start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Local);

    private static ReportPoint Point(int minute, decimal value, bool online = true)
    {
        return new ReportPoint(_start.AddMinutes(minute), value, online);
    }

    [Fact]
    public void ParseStateValue_Should_Support_Bool_And_Number()
    {
        Assert.Equal(1m, MetricCalculator.ParseStateValue(null));
        Assert.Equal(1m, MetricCalculator.ParseStateValue(" "));
        Assert.Equal(1m, MetricCalculator.ParseStateValue("true"));
        Assert.Equal(0m, MetricCalculator.ParseStateValue("FALSE"));
        Assert.Equal(2m, MetricCalculator.ParseStateValue("2"));
        Assert.Equal(1m, MetricCalculator.ParseStateValue("abc"));
    }

    [Fact]
    public void MatchState_Should_Ignore_Floating_Noise()
    {
        Assert.True(MetricCalculator.MatchState(1.0000001m, 1m));
        Assert.False(MetricCalculator.MatchState(1.001m, 1m));
    }

    [Fact]
    public void ResolveMaxGap_Should_Use_Median_Interval()
    {
        var points = new List<ReportPoint> { Point(0, 1), Point(1, 1), Point(2, 1), Point(3, 1) };
        Assert.Equal(TimeSpan.FromSeconds(180), MetricCalculator.ResolveMaxGap(points));

        Assert.Equal(TimeSpan.FromMinutes(5), MetricCalculator.ResolveMaxGap(new List<ReportPoint> { Point(0, 1) }));
    }

    [Fact]
    public void BuildSegments_Should_Use_Baseline_Before_Start()
    {
        var points = new List<ReportPoint> { Point(0, 1), Point(20, 1), Point(30, 0), Point(45, 1) };
        var segments = MetricCalculator.BuildSegments(points, _start, _start.AddHours(1), TimeSpan.FromHours(1));

        Assert.Equal(4, segments.Count);
        Assert.Equal(_start, segments[0].Start);
        Assert.Equal(_start.AddMinutes(20), segments[0].End);
        Assert.Equal(_start.AddMinutes(45), segments[3].Start);
        Assert.Equal(_start.AddHours(1), segments[3].End);
    }

    [Fact]
    public void BuildSegments_Should_Clamp_Unknown_Gap()
    {
        var points = new List<ReportPoint> { Point(0, 1), Point(10, 1) };
        var segments = MetricCalculator.BuildSegments(points, _start, _start.AddHours(1), TimeSpan.FromMinutes(15));

        Assert.Equal(2, segments.Count);
        Assert.Equal(_start.AddMinutes(10), segments[1].Start);
        Assert.Equal(_start.AddMinutes(25), segments[1].End);

        Assert.Equal(25d / 60d, MetricCalculator.OnlineHours(segments), 6);
        Assert.Equal(35d / 60d, MetricCalculator.OfflineHours(segments, _start, _start.AddHours(1)), 6);
        Assert.Equal(1500d / 36d, MetricCalculator.OnlineRate(segments, _start, _start.AddHours(1)), 6);
    }

    [Fact]
    public void StateHours_Should_Only_Count_Online_Time()
    {
        var points = new List<ReportPoint> { Point(0, 1), Point(20, 1), Point(30, 0), Point(45, 1) };
        var segments = MetricCalculator.BuildSegments(points, _start, _start.AddHours(1), TimeSpan.FromHours(1));

        Assert.Equal(0.75d, MetricCalculator.StateHours(segments, 1m), 6);
        Assert.Equal(0.25d, MetricCalculator.StateHours(segments, 0m), 6);

        var offlinePoints = new List<ReportPoint> { Point(0, 1), Point(20, 1, false), Point(30, 1), Point(45, 1) };
        var offlineSegments = MetricCalculator.BuildSegments(offlinePoints, _start, _start.AddHours(1), TimeSpan.FromHours(1));
        Assert.Equal(50d / 60d, MetricCalculator.StateHours(offlineSegments, 1m), 6);
    }

    [Fact]
    public void CountStateEntries_Should_Count_Rising_Edges()
    {
        var points = new List<ReportPoint> { Point(0, 1), Point(20, 1), Point(30, 0), Point(45, 1), Point(50, 1), Point(55, 0) };
        var segments = MetricCalculator.BuildSegments(points, _start, _start.AddHours(1), TimeSpan.FromHours(1));

        Assert.Equal(2, MetricCalculator.CountStateEntries(segments, 1m));
        Assert.Equal(2, MetricCalculator.CountStateEntries(segments, 0m));
    }

    [Fact]
    public void StateTimestamps_Should_Follow_Segments()
    {
        var points = new List<ReportPoint> { Point(0, 1), Point(20, 1), Point(30, 0), Point(45, 1) };
        var segments = MetricCalculator.BuildSegments(points, _start, _start.AddHours(1), TimeSpan.FromHours(1));

        Assert.Equal(_start, MetricCalculator.FirstStateTimestamp(segments, 1m));
        Assert.Equal(_start.AddMinutes(45), MetricCalculator.LastStateTimestamp(segments, 1m));
        Assert.Equal(_start.AddMinutes(30), MetricCalculator.FirstStateTimestamp(segments, 0m));
        Assert.Null(MetricCalculator.FirstStateTimestamp(segments, 9m));
    }

    [Fact]
    public void CounterDelta_Should_Handle_Reset()
    {
        var points = new List<ReportPoint> { Point(-5, 100), Point(10, 105), Point(20, 3), Point(30, 8) };
        Assert.Equal(13m, MetricCalculator.CounterDelta(points, _start, _start.AddHours(1)));

        Assert.Null(MetricCalculator.CounterDelta(new List<ReportPoint>(), _start, _start.AddHours(1)));
        Assert.Null(MetricCalculator.CounterDelta(new List<ReportPoint> { Point(-5, 100) }, _start, _start.AddHours(1)));
    }

    [Fact]
    public void Aggregate_Should_Ignore_Points_Out_Of_Range()
    {
        var points = new List<ReportPoint> { Point(-5, 100), Point(10, 2), Point(20, 6), Point(30, 4), Point(90, 100) };
        var (avg, max, min, last) = MetricCalculator.Aggregate(points, _start, _start.AddHours(1));

        Assert.Equal(4m, avg);
        Assert.Equal(6m, max);
        Assert.Equal(2m, min);
        Assert.Equal(4m, last);

        var (emptyAvg, emptyMax, emptyMin, emptyLast) = MetricCalculator.Aggregate(points, _start.AddDays(1), _start.AddDays(2));
        Assert.Null(emptyAvg);
        Assert.Null(emptyMax);
        Assert.Null(emptyMin);
        Assert.Null(emptyLast);
    }

    [Fact]
    public void Downsample_Should_Keep_Boundaries_And_Limit()
    {
        var points = Enumerable.Range(0, 1000).Select(a => Point(a, a)).ToList();
        var result = MetricCalculator.Downsample(points, 50);

        Assert.True(result.Count <= 50);
        Assert.Equal(points[0].Time, result[0].Time);
        Assert.Equal(points[^1].Time, result[^1].Time);

        Assert.Equal(points.Count, MetricCalculator.Downsample(points, 2000).Count);
    }

    [Fact]
    public void NaturalStringComparer_Should_Sort_By_Number()
    {
        var list = new List<string> { "设备10", "设备2", "设备1", "Device1", "设备A" };
        list.Sort(NaturalStringComparer.Instance);

        var index1 = list.IndexOf("设备1");
        var index2 = list.IndexOf("设备2");
        var index10 = list.IndexOf("设备10");
        Assert.True(index1 < index2);
        Assert.True(index2 < index10);
    }
}
