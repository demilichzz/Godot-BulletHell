using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>提供不消耗随机的双精度路径求值和弧长采样工具。</summary>
public static partial class VMath
{
    /// <summary>使用de Casteljau求值任意阶贝塞尔，避免高阶组合数溢出。</summary>
    /// <param name="points">包含起终点的有序有限坐标，至少两个，单位为逻辑像素。</param>
    /// <param name="t">归一化参数，范围[0,1]。</param>
    /// <returns>双精度曲线坐标。</returns>
    internal static (double X, double Y) EvaluateBezier(IReadOnlyList<(double X, double Y)> points, double t)
    {
        if (points.Count < 2 || !double.IsFinite(t) || t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t));
        // 原地收缩工作数组，保留传入的只读控制点。
        var work = points.ToArray();
        for (int remaining = work.Length - 1; remaining > 0; remaining--)
            for (int index = 0; index < remaining; index++)
                work[index] = (work[index].X * (1 - t) + work[index + 1].X * t,
                    work[index].Y * (1 - t) + work[index + 1].Y * t);
        return ValidatePathPoint(work[0]);
    }

    /// <summary>按固定参数网格建立累计弧长表，顺序不依赖帧率或随机。</summary>
    /// <param name="evaluate">参数[0,1]到逻辑像素坐标的纯函数。</param>
    /// <param name="samples">参数区间数，范围2至65536。</param>
    /// <returns>长度为samples+1的累计弧长；第一项为0。</returns>
    internal static double[] BuildArcLengthTable(Func<double, (double X, double Y)> evaluate, int samples)
    {
        if (samples < 2 || samples > 65536) throw new ArgumentOutOfRangeException(nameof(samples));
        // 始终从起点按参数递增求值，零长度小区间可保留。
        var lengths = new double[samples + 1];
        var previous = ValidatePathPoint(evaluate(0));
        for (int index = 1; index <= samples; index++)
        {
            var point = ValidatePathPoint(evaluate((double)index / samples));
            lengths[index] = FiniteResult(lengths[index - 1] + GetDistanceBetween2Points(previous.X, previous.Y, point.X, point.Y));
            previous = point;
        }
        return lengths;
    }

    /// <summary>从累计弧长反查参数，随后重新求值实际曲线，不返回折线插值点。</summary>
    /// <param name="evaluate">参数[0,1]到逻辑像素坐标的纯函数。</param>
    /// <param name="lengths">按固定参数网格计算的非零总弧长表。</param>
    /// <param name="fraction">目标弧长占整段比例，范围[0,1]。</param>
    /// <returns>双精度曲线坐标。</returns>
    internal static (double X, double Y) SampleCurveByArcLength(Func<double, (double X, double Y)> evaluate,
        IReadOnlyList<double> lengths, double fraction)
    {
        if (!double.IsFinite(fraction) || fraction < 0 || fraction > 1 || lengths.Count < 2 || lengths[^1] <= 0)
            throw new ArgumentOutOfRangeException(nameof(fraction));
        if (fraction == 0 || fraction == 1) return ValidatePathPoint(evaluate(fraction));
        // 查找首个严格大于目标弧长的上界，跳过累计长度平台，避免除零。
        double target = lengths[^1] * fraction;
        int lower = 0, upper = lengths.Count - 1;
        while (upper - lower > 1)
        {
            int middle = lower + (upper - lower) / 2;
            if (lengths[middle] <= target) lower = middle;
            else upper = middle;
        }
        double span = lengths[upper] - lengths[lower];
        double t = (lower + (span == 0 ? 0 : (target - lengths[lower]) / span)) / (lengths.Count - 1);
        return ValidatePathPoint(evaluate(t));
    }

    /// <summary>先保证每段一个间隔，再按长度和最大余数法分配剩余间隔。</summary>
    /// <param name="lengths">每段的正有限弧长，逻辑像素。</param>
    /// <param name="totalIntervals">总间隔数，须不少于路径段数。</param>
    /// <returns>每段的正整数间隔数，合计等于totalIntervals。</returns>
    internal static int[] AllocatePathIntervals(IReadOnlyList<double> lengths, int totalIntervals)
    {
        if (lengths.Count == 0 || totalIntervals < lengths.Count || lengths.Any(value => !double.IsFinite(value) || value <= 0))
            throw new ArgumentOutOfRangeException(nameof(lengths));
        // 缩放后求比例，避免总长因多段极大数值而溢出。
        double scale = lengths.Max();
        double total = lengths.Sum(value => value / scale);
        int remaining = totalIntervals - lengths.Count;
        var result = new int[lengths.Count];
        var remainders = new double[lengths.Count];
        int assigned = 0;
        for (int index = 0; index < lengths.Count; index++)
        {
            double share = remaining * ((lengths[index] / scale) / total);
            int whole = (int)Math.Floor(share);
            result[index] = 1 + whole;
            remainders[index] = share - whole;
            assigned = checked(assigned + whole);
        }
        // 显式以声明索引打破余数相同的排序，保持重现性。
        foreach (int index in Enumerable.Range(0, lengths.Count).OrderByDescending(index => remainders[index])
            .ThenBy(index => index).Take(remaining - assigned)) result[index]++;
        return result;
    }

    /// <summary>在转换为Godot坐标前检查有限性与单精度表示范围。</summary>
    /// <param name="point">双精度逻辑像素坐标。</param>
    /// <returns>已检查的原始双精度坐标，不提前舍入。</returns>
    internal static (double X, double Y) ValidatePathPoint((double X, double Y) point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > float.MaxValue || Math.Abs(point.Y) > float.MaxValue)
            throw new OverflowException("路径坐标超出Vector2有限范围。");
        return point;
    }
}
