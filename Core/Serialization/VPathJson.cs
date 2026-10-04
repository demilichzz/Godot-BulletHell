using static JsonData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>Boss、Creator、激光与编辑器共用的路径协议读取，不创建任何战斗对象。</summary>
internal static class VPathJson
{
    /// <summary>严格读取路径段并编译几何，加载时不采样目标或消耗随机。</summary>
    /// <param name="element">非空PathQueue数组。</param>
    /// <param name="pointCount">预定采样数，至少为段数加一。</param>
    /// <returns>独立的不可变几何。</returns>
    internal static VPathGeometry Read(JsonElement element, int pointCount)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
            throw new JsonException("PathQueue必须为非空数组。");
        // 逐段附加位置诊断，运行端点关系仍在实际采样时校验。
        var segments = new List<VPathSegmentAttribute>();
        foreach (var value in element.EnumerateArray())
        {
            try { segments.Add(ReadSegment(value)); }
            catch (Exception error) when (VPathGeometry.IsPathError(error)) { throw VPathGeometry.PathError(segments.Count, error); }
        }
        var geometry = new VPathGeometry(segments);
        geometry.ValidatePointCount(pointCount);
        return geometry;
    }
    /// <summary>严格读取各模式字段，冻结端点队列和控制点。</summary>
    /// <param name="element">路径段JSON对象。</param>
    /// <returns>不包含运行端点的配置。</returns>
    private static VPathSegmentAttribute ReadSegment(JsonElement element)
    {
        // 瞄准简写独占Type/X/Y字段，避免与普通PathMode或端点队列产生两套含义。
        if (element.TryGetProperty("Type", out _))
        {
            CheckFields(element, new[] { "Type", "X", "Y" });
            if (Required(element, "Type").GetString() != "AimPlayer")
                throw new JsonException("路径段Type只支持AimPlayer。");
            var offset = VMath.ValidatePathPoint((
                element.TryGetProperty("X", out _) ? Read<double>(Required(element, "X")) : 0,
                element.TryGetProperty("Y", out _) ? Read<double>(Required(element, "Y")) : 0));
            return new VPathSegmentAttribute
            {
                AimPlayerOffset = new VPathPointAttribute { X = offset.X, Y = offset.Y }
            };
        }
        // 普通模式共用起终点队列，AxisMode仅允许用于Function。
        string mode = Required(element, "PathMode").GetString() ?? "";
        CheckFields(element, mode switch
        {
            "XY" => new[] { "PathMode", "StartMoveQueue", "EndMoveQueue" },
            "Bezier" => new[] { "PathMode", "StartMoveQueue", "EndMoveQueue", "ControlPoints", "Samples" },
            "Function" => new[] { "PathMode", "StartMoveQueue", "EndMoveQueue", "AxisMode", "X", "Y", "TMin", "TMax", "Samples" },
            _ => throw new JsonException("PathMode只支持XY、Bezier或Function。")
        });
        var segment = new VPathSegmentAttribute
        {
            PathMode = mode,
            StartMoveQueue = element.TryGetProperty("StartMoveQueue", out var start) ? VMoveJson.ReadQueue(start) : Array.Empty<VNodeMoveActionAttribute>(),
            EndMoveQueue = element.TryGetProperty("EndMoveQueue", out var end) ? VMoveJson.ReadQueue(end) : Array.Empty<VNodeMoveActionAttribute>()
        };
        if (mode == "XY") return segment;
        // Samples保持整数，不接受小数表达式。
        int samples = element.TryGetProperty("Samples", out _) ? Read<int>(Required(element, "Samples")) : 1024;
        if (samples < 2 || samples > 65536) throw new JsonException("Samples必须为2至65536的整数。");
        segment = segment with { Samples = samples };
        if (mode == "Bezier")
        {
            var controls = Required(element, "ControlPoints");
            if (controls.ValueKind != JsonValueKind.Array || controls.GetArrayLength() == 0)
                throw new JsonException("ControlPoints必须为非空坐标数组。");
            return segment with { ControlPoints = Array.AsReadOnly(controls.EnumerateArray().Select(ReadPoint).ToArray()) };
        }
        // 仅模式和语法在加载时验证，函数定义域及端点匹配按实际批次检查。
        string axis = element.TryGetProperty("AxisMode", out _) ? Required(element, "AxisMode").GetString() ?? "" : "Absolute";
        double minimum = element.TryGetProperty("TMin", out _) ? Read<double>(Required(element, "TMin")) : 0;
        double maximum = element.TryGetProperty("TMax", out _) ? Read<double>(Required(element, "TMax")) : 1;
        if (axis is not ("Absolute" or "Relative")) throw new JsonException("AxisMode只支持Absolute或Relative。");
        if (minimum >= maximum) throw new JsonException("TMin必须小于TMax。");
        return segment with
        {
            AxisMode = axis,
            X = Required(element, "X").GetString(),
            Y = Required(element, "Y").GetString(),
            TMin = minimum,
            TMax = maximum
        };
    }

    /// <summary>读取完整提供X/Y的控制点；坐标相对本段参考点。</summary>
    /// <param name="element">控制点JSON对象。</param>
    /// <returns>有限双精度逻辑像素坐标。</returns>
    private static VPathPointAttribute ReadPoint(JsonElement element)
    {
        CheckFields(element, new[] { "X", "Y" });
        // 保留PI/TAU等常量表达式，不接受动态路径变量。
        var point = VMath.ValidatePathPoint((Read<double>(Required(element, "X")), Read<double>(Required(element, "Y"))));
        return new VPathPointAttribute { X = point.X, Y = point.Y };
    }

}
