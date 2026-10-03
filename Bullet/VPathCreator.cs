using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>按批次计算首尾相连的路径段，复用公共节点、时间线和子生成器流程。</summary>
public sealed class VPathCreator : VNodeCreator
{
    /// <summary>加载时冻结的路径定义；起终点在每次批次触发时通过位移队列求值。</summary>
    public IReadOnlyList<VPathSegmentAttribute> PathQueue { get; private set; } = Array.Empty<VPathSegmentAttribute>();
    // 表达式仅在加载时解析，委托通过参数接收每批独立的端点和长度。
    private (Func<VPathFunctionContext, double>? X, Func<VPathFunctionContext, double>? Y)[] _functions = Array.Empty<(Func<VPathFunctionContext, double>?, Func<VPathFunctionContext, double>?)>();
    // 连接点及函数端点的最大误差，单位为逻辑像素。
    private const double EndpointTolerance = 0.001;

    /// <summary>建立路径类型的空生成器，使用前须通过JSON加载有效定义。</summary>
    public VPathCreator() => Core = new VNodeCoreAttribute { Type = "VPath" };

    /// <summary>建立仅供激光采样的路径定义，不生成节点或登记时间线。</summary>
    /// <param name="queue">沿用VPath格式的非空PathQueue。</param>
    /// <param name="pointCount">包含全部连接点的采样点数，至少为段数加一。</param>
    /// <returns>经过现有路径校验和表达式编译的独立定义。</returns>
    internal static VPathCreator CreateGeometry(JsonElement queue, int pointCount)
    {
        // 复用原生成器的几何计算，数量只决定采样密度。
        var path = new VPathCreator { Core = new VNodeCoreAttribute { Type = "VPath", Amount = pointCount } };
        path.ReadPathQueue(queue);
        path.ValidateAttributes();
        return path;
    }

    /// <summary>采样完整路径，返回相对指定世界参考点的固定偏移，不创建成员。</summary>
    /// <param name="source">本次路径冻结时的世界参考点，逻辑像素。</param>
    /// <returns>包含起点、连接点及终点的只读偏移。</returns>
    internal IReadOnlyList<Vector2> SampleGeometry(Vector2 source) => PrepareBatchPositions(source)!;

    /// <summary>读取新路径格式，不保留固定Start/End或Polar模式的兼容分支。</summary>
    /// <param name="element">非空PathQueue数组。</param>
    internal void ReadPathQueue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
            throw new JsonException("PathQueue必须为非空数组。");
        // 逐段附加位置诊断，运行时再校验实际几何关系。
        var segments = new List<VPathSegmentAttribute>();
        foreach (var value in element.EnumerateArray())
        {
            try { segments.Add(ReadSegment(value)); }
            catch (Exception error) when (IsPathError(error)) { throw PathError(segments.Count, error); }
        }
        PathQueue = segments.AsReadOnly();
    }

    /// <summary>校验基础配置并编译函数，加载阶段不依赖父位置或消耗随机。</summary>
    protected override void ValidateAttributes()
    {
        base.ValidateAttributes();
        if (BaseAttributes.Count != 1) throw new JsonException("VPath的BaseAttributes必须恰好包含一个元素。");
        if (PathQueue.Count == 0 || Core.Amount <= PathQueue.Count)
            throw new JsonException("VPath的Amount必须至少为路径段数加一。");
        // 普通段不分配表达式委托；每个Function仅解析一次。
        _functions = new (Func<VPathFunctionContext, double>?, Func<VPathFunctionContext, double>?)[PathQueue.Count];
        for (int index = 0; index < PathQueue.Count; index++)
        {
            try
            {
                var segment = PathQueue[index];
                if (segment.PathMode == "Function")
                    _functions[index] = (VPathExpression.CompilePath(segment.X!), VPathExpression.CompilePath(segment.Y!));
            }
            catch (Exception error) when (IsPathError(error)) { throw PathError(index, error); }
        }
    }

    /// <summary>在任何成员出生前解析全部端点和曲线，返回本批独立的位置缓存。</summary>
    /// <param name="source">触发时的父参考世界位置，逻辑像素。</param>
    /// <returns>相对source的只读偏移；Follow后续仅平移，Snapshot保持触发位置。</returns>
    protected override IReadOnlyList<Vector2>? PrepareBatchPositions(Vector2 source)
    {
        // 求值器与弧长表只在准备当前批次期间存在，不写入Creator共享字段。
        var evaluators = new Func<double, (double X, double Y)>[PathQueue.Count];
        var tables = new double[PathQueue.Count][];
        Vector2 reference = source;
        for (int index = 0; index < PathQueue.Count; index++)
        {
            try
            {
                var segment = PathQueue[index];
                Vector2 start = ApplyMoveQueue(reference, segment.StartMoveQueue);
                if (index > 0)
                {
                    CheckEndpoint((start.X, start.Y), reference);
                    start = reference;
                }
                Vector2 end = ApplyMoveQueue(start, segment.EndMoveQueue);
                var evaluate = CreateEvaluator(segment, reference, start, end, _functions[index]);
                evaluators[index] = evaluate;
                tables[index] = segment.PathMode == "XY"
                    ? new[] { 0.0, VMath.GetDistanceBetween2Points(start, end) }
                    : VMath.BuildArcLengthTable(evaluate, segment.Samples);
                if (tables[index][^1] <= 0) throw new JsonException("路径段长度必须大于0。");
                reference = end;
            }
            catch (Exception error) when (IsPathError(error)) { throw PathError(index, error); }
        }
        // 各段保证一个间隔，连接点仅由前一段写入一次。
        var intervals = VMath.AllocatePathIntervals(tables.Select(table => table[^1]).ToArray(), Core.Amount - 1);
        var positions = new Vector2[Core.Amount];
        int output = 0;
        for (int segmentIndex = 0; segmentIndex < PathQueue.Count; segmentIndex++)
        {
            for (int index = segmentIndex == 0 ? 0 : 1; index <= intervals[segmentIndex]; index++)
            {
                try
                {
                    var point = VMath.SampleCurveByArcLength(evaluators[segmentIndex], tables[segmentIndex], (double)index / intervals[segmentIndex]);
                    var offset = VMath.ValidatePathPoint((point.X - source.X, point.Y - source.Y));
                    positions[output++] = new Vector2((float)offset.X, (float)offset.Y);
                }
                catch (Exception error) when (IsPathError(error)) { throw PathError(segmentIndex, error); }
            }
        }
        return Array.AsReadOnly(positions);
    }

    /// <summary>从世界位置开始执行确定性的位移队列；TarMove始终读取世界目标。</summary>
    /// <param name="source">初始世界坐标，逻辑像素。</param>
    /// <param name="actions">已校验且无随机的位移动作。</param>
    /// <returns>最终世界位置，坐标溢出时抛错。</returns>
    private static Vector2 ApplyMoveQueue(Vector2 source, IReadOnlyList<VNodeMoveActionAttribute> actions)
    {
        // 每个动作从上个动作的结果继续，不将TarMove目标转换为局部坐标。
        Vector2 position = source;
        foreach (var action in actions)
        {
            position = action.Type switch
            {
                "PMove" => VMath.PolarMove(position, action.Angle ?? 0, action.Dist ?? 0),
                "TarMove" => VMath.TargetMove(position, new Vector2((float)(action.X ?? 0), (float)(action.Y ?? 0)), action.Dist ?? 0),
                _ => new Vector2((float)(position.X + (action.X ?? 0)), (float)(position.Y + (action.Y ?? 0)))
            };
            if (!position.IsFinite()) throw new OverflowException("路径位移结果超出Vector2范围。");
        }
        return position;
    }

    /// <summary>严格读取各模式字段，冻结端点队列和控制点。</summary>
    /// <param name="element">路径段JSON对象。</param>
    /// <returns>不包含运行端点的配置。</returns>
    private static VPathSegmentAttribute ReadSegment(JsonElement element)
    {
        // 所有模式共用起终点队列，AxisMode仅允许用于Function。
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
            StartMoveQueue = element.TryGetProperty("StartMoveQueue", out var start) ? ReadMoveQueue(start) : Array.Empty<VNodeMoveActionAttribute>(),
            EndMoveQueue = element.TryGetProperty("EndMoveQueue", out var end) ? ReadMoveQueue(end) : Array.Empty<VNodeMoveActionAttribute>()
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

    /// <summary>建立参数[0,1]到世界坐标的批次求值器，端点在容差内吸附。</summary>
    /// <param name="segment">段配置。</param>
    /// <param name="reference">本段参考世界点，Bezier控制点相对此点。</param>
    /// <param name="start">本段实际起点，世界逻辑像素。</param>
    /// <param name="end">本段实际终点，世界逻辑像素。</param>
    /// <param name="functions">已编译的X/Y表达式，非Function段为空。</param>
    /// <returns>不读取后续父位置的纯函数。</returns>
    private static Func<double, (double X, double Y)> CreateEvaluator(VPathSegmentAttribute segment, Vector2 reference,
        Vector2 start, Vector2 end, (Func<VPathFunctionContext, double>? X, Func<VPathFunctionContext, double>? Y) functions)
    {
        if (segment.PathMode == "XY")
            return t => (start.X * (1 - t) + end.X * t, start.Y * (1 - t) + end.Y * t);
        if (segment.PathMode == "Bezier")
        {
            // 控制点相对本段参考点，轴向始终与屏幕一致。
            var points = new[] { ((double)start.X, (double)start.Y) }
                .Concat(segment.ControlPoints.Select(point => VMath.ValidatePathPoint((reference.X + point.X, reference.Y + point.Y))))
                .Append(((double)end.X, (double)end.Y)).ToArray();
            return t => VMath.EvaluateBezier(points, t);
        }
        // 端点和长度仅属于这次批次，表达式不捕获Creator上的可变上下文。
        double length = VMath.GetDistanceBetween2Points(start, end);
        bool relative = segment.AxisMode == "Relative";
        if (relative && length == 0) throw new JsonException("Relative坐标系要求起终点不重合。");
        var context = new VPathFunctionContext { L = length, SX = start.X, SY = start.Y, EX = end.X, EY = end.Y };
        double axisX = relative ? ((double)end.X - start.X) / length : 0;
        double axisY = relative ? ((double)end.Y - start.Y) / length : 0;
        /// <summary>把实际参数的函数结果转换为世界坐标。</summary>
        /// <param name="t">TMin至TMax内的实际参数。</param>
        /// <returns>世界逻辑像素坐标。</returns>
        (double X, double Y) Evaluate(double t)
        {
            // Y轴是X轴顺时针90度方向，端点变量在两种模式下均为世界坐标。
            var values = context with { T = t };
            double x = functions.X!(values), y = functions.Y!(values);
            return VMath.ValidatePathPoint(relative ? (start.X + axisX * x - axisY * y, start.Y + axisY * x + axisX * y) : (x, y));
        }
        CheckEndpoint(Evaluate(segment.TMin), start);
        CheckEndpoint(Evaluate(segment.TMax), end);
        return fraction =>
        {
            if (fraction == 0) return (start.X, start.Y);
            if (fraction == 1) return (end.X, end.Y);
            // 加权插值避免参数跨度计算溢出。
            double t = segment.TMin * (1 - fraction) + segment.TMax * fraction;
            return Evaluate(t);
        };
    }

    /// <summary>检查连接点或方程端点符合已计算的世界坐标。</summary>
    /// <param name="actual">实际双精度世界坐标。</param>
    /// <param name="expected">端点队列确定的世界坐标。</param>
    private static void CheckEndpoint((double X, double Y) actual, Vector2 expected)
    {
        if (VMath.GetDistanceBetween2Points(actual.X, actual.Y, expected.X, expected.Y) > EndpointTolerance)
            throw new JsonException("路径连接点或函数端点偏差超过0.001像素。");
    }

    /// <summary>判断需要追加Creator和段位置诊断的配置或计算错误。</summary>
    /// <param name="error">原始错误。</param>
    /// <returns>是否属于可诊断的路径错误。</returns>
    private static bool IsPathError(Exception error) => error is JsonException or ArgumentException or InvalidOperationException or OverflowException;

    /// <summary>包装路径诊断，不吞掉错误或保留部分生成成员。</summary>
    /// <param name="index">零基路径段索引。</param>
    /// <param name="error">原始错误。</param>
    /// <returns>包含Creator身份和段位置的JSON错误。</returns>
    private JsonException PathError(int index, Exception error)
        => new($"{Core.Id}({Core.Name}): PathQueue[{index}]: {error.Message}", error);
}
