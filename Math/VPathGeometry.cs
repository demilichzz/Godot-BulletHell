using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>不可变的分段几何与已编译函数；不持有Creator、时间线、战斗或运行坐标。</summary>
internal sealed class VPathGeometry
{
    /// <summary>声明顺序固定的路径段，端点在每次采样时求值。</summary>
    internal IReadOnlyList<VPathSegmentAttribute> Segments { get; }
    // 表达式通过参数接收每次采样独立的端点和长度，不捕获运行上下文。
    private readonly (Func<VPathFunctionContext, double>? X, Func<VPathFunctionContext, double>? Y)[] _functions;
    // 连接点及函数端点的最大误差，单位为逻辑像素。
    private const double EndpointTolerance = 0.001;

    /// <summary>冻结已通过协议检查的路径段，并一次编译函数表达式。</summary>
    /// <param name="segments">非空路径队列，不含实际运行端点。</param>
    internal VPathGeometry(IReadOnlyList<VPathSegmentAttribute> segments)
    {
        if (segments.Count == 0) throw new JsonException("PathQueue必须为非空数组。");
        // 各属性记录不可变，列表独立包装，避免外部替换数组成员。
        Segments = Array.AsReadOnly(segments.Select(segment => segment with
        {
            StartMoveQueue = Array.AsReadOnly(segment.StartMoveQueue.ToArray()),
            EndMoveQueue = Array.AsReadOnly(segment.EndMoveQueue.ToArray()),
            ControlPoints = Array.AsReadOnly(segment.ControlPoints.ToArray())
        }).ToArray());
        // 普通段不分配表达式委托；每个Function仅解析一次。
        _functions = new (Func<VPathFunctionContext, double>?, Func<VPathFunctionContext, double>?)[Segments.Count];
        for (int index = 0; index < Segments.Count; index++)
        {
            try
            {
                var segment = Segments[index];
                if (segment.PathMode == "Function")
                    _functions[index] = (VPathExpression.CompilePath(segment.X!), VPathExpression.CompilePath(segment.Y!));
            }
            catch (Exception error) when (IsPathError(error)) { throw PathError(index, error); }
        }
    }

    /// <summary>校验总采样点数能包含每个连接点，数量不限制为生成器容量。</summary>
    /// <param name="pointCount">包含起点和终点的总点数，至少为段数加一。</param>
    internal void ValidatePointCount(int pointCount)
    {
        if (pointCount <= Segments.Count) throw new JsonException("VPath的Amount必须至少为路径段数加一。");
    }

    /// <summary>按各段弧长分配采样点；同一次调用的瞄准段只获取一次目标快照。</summary>
    /// <param name="source">本次冻结的世界参考点，逻辑像素。</param>
    /// <param name="pointCount">包含全部连接点的总采样数。</param>
    /// <param name="targetSource">仅遇到瞄准段才调用；普通路径允许省略。</param>
    /// <returns>独立只读偏移；后续参考点或目标移动不改变它。</returns>
    internal IReadOnlyList<Vector2> Sample(Vector2 source, int pointCount, Func<Vector2>? targetSource = null)
        => SampleRange(source, pointCount, targetSource, 0, Segments.Count);

    /// <summary>用已编译函数采样指定一段，供分段展示密度使用，不重建JSON或编译表达式。</summary>
    /// <param name="index">段的零基声明下标。</param>
    /// <param name="source">该段的世界参考点，逻辑像素。</param>
    /// <param name="pointCount">该段的采样点数，至少为2。</param>
    /// <param name="targetSource">可选瞄准目标提供器，普通段不调用。</param>
    /// <returns>相对该段参考点的只读偏移。</returns>
    internal IReadOnlyList<Vector2> SampleSegment(int index, Vector2 source, int pointCount, Func<Vector2>? targetSource = null)
    {
        if (index < 0 || index >= Segments.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return SampleRange(source, pointCount, targetSource, index, 1);
    }

    /// <summary>在本次调用内求值端点、弧长表与输出，不写入共享字段。</summary>
    /// <param name="source">有限世界参考点，逻辑像素。</param>
    /// <param name="pointCount">整个范围的采样点数，须大于段数。</param>
    /// <param name="targetSource">瞄准目标提供器，首次遇到瞄准段才调用。</param>
    /// <param name="firstSegment">范围内第一段的零基下标。</param>
    /// <param name="segmentCount">连续段数。</param>
    /// <returns>相对source的独立只读偏移。</returns>
    private IReadOnlyList<Vector2> SampleRange(Vector2 source, int pointCount, Func<Vector2>? targetSource, int firstSegment, int segmentCount)
    {
        if (!source.IsFinite()) throw new ArgumentOutOfRangeException(nameof(source));
        if (pointCount <= segmentCount) throw new JsonException("VPath的Amount必须至少为路径段数加一。");
        // 求值器与弧长表仅属于本次采样，不写入共享字段。
        var evaluators = new Func<double, (double X, double Y)>[segmentCount];
        var tables = new double[segmentCount][];
        Vector2 reference = source;
        // 同一条路径的全部瞄准段共享一次目标快照；普通路径不调用提供器。
        Vector2? targetSnapshot = null;
        for (int index = 0; index < segmentCount; index++)
        {
            try
            {
                var segment = Segments[firstSegment + index];
                Vector2 start = ApplyMoveQueue(reference, segment.StartMoveQueue);
                if (index > 0)
                {
                    CheckEndpoint((start.X, start.Y), reference);
                    start = reference;
                }
                Vector2 end;
                if (segment.AimPlayerOffset is { } offset)
                {
                    targetSnapshot ??= (targetSource ?? throw new InvalidOperationException("瞄准路径需要目标坐标提供器。"))();
                    // 偏移相对目标世界位置，不叠加参考位置；先以双精度检查坐标范围。
                    var target = VMath.ValidatePathPoint((targetSnapshot.Value.X + offset.X, targetSnapshot.Value.Y + offset.Y));
                    end = new Vector2((float)target.X, (float)target.Y);
                }
                else end = ApplyMoveQueue(start, segment.EndMoveQueue);
                var evaluate = CreateEvaluator(segment, reference, start, end, _functions[firstSegment + index]);
                evaluators[index] = evaluate;
                tables[index] = segment.PathMode == "XY"
                    ? new[] { 0.0, VMath.GetDistanceBetween2Points(start, end) }
                    : VMath.BuildArcLengthTable(evaluate, segment.Samples);
                if (tables[index][^1] <= 0) throw new JsonException("路径段长度必须大于0。");
                reference = end;
            }
            catch (Exception error) when (IsPathError(error)) { throw PathError(firstSegment + index, error); }
        }
        // 各段保证一个间隔，连接点仅由前一段写入一次。
        var intervals = VMath.AllocatePathIntervals(tables.Select(table => table[^1]).ToArray(), pointCount - 1);
        var positions = new Vector2[pointCount];
        int output = 0;
        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            for (int index = segmentIndex == 0 ? 0 : 1; index <= intervals[segmentIndex]; index++)
            {
                try
                {
                    var point = VMath.SampleCurveByArcLength(evaluators[segmentIndex], tables[segmentIndex], (double)index / intervals[segmentIndex]);
                    var offset = VMath.ValidatePathPoint((point.X - source.X, point.Y - source.Y));
                    positions[output++] = new Vector2((float)offset.X, (float)offset.Y);
                }
                catch (Exception error) when (IsPathError(error)) { throw PathError(firstSegment + segmentIndex, error); }
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
        // 端点和长度仅属于这次批次，表达式不捕获共享对象上的可变上下文。
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

    /// <summary>判断需要追加路径段位置的配置或计算错误。</summary>
    /// <param name="error">原始异常。</param>
    /// <returns>是否属于可诊断的路径错误。</returns>
    internal static bool IsPathError(Exception error) => error is JsonException or ArgumentException or InvalidOperationException or OverflowException;

    /// <summary>只附加路径段位置；Creator或Boss身份由上层加载和运行入口补充。</summary>
    /// <param name="index">零基路径段下标。</param>
    /// <param name="error">原始错误。</param>
    /// <returns>不吞掉原始诊断的JSON异常。</returns>
    internal static JsonException PathError(int index, Exception error)
        => new($"PathQueue[{index}]: {error.Message}", error);
}
