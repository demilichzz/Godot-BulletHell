using Godot;
using System;
using System.Collections.Generic;

/// <summary>固定折线上移动光束的连续碰撞，不消耗随机或推进任何时钟。</summary>
public static partial class VMath
{
    /// <summary>检测运动目标圆与同一时刻的光束窗口，避免高速穿越及前后轨迹并集误判。</summary>
    /// <param name="points">固定世界折线，单位为逻辑像素。</param>
    /// <param name="distances">每个顶点的累计弧长，与points等长。</param>
    /// <param name="targetStart">目标本时间区间的起点，世界像素。</param>
    /// <param name="targetEnd">目标本时间区间的终点，世界像素。</param>
    /// <param name="headStart">区间起始时未截断的头部距离，像素。</param>
    /// <param name="headEnd">区间结束时未截断的头部距离，不小于headStart。</param>
    /// <param name="length">光束目标弧长，正数像素。</param>
    /// <param name="inset">显示区间两端向内裁掉的碰撞长度，非负像素。</param>
    /// <param name="radius">激光判定半宽与目标半径之和，非负像素。</param>
    /// <returns>存在同一时刻的接触时为真。</returns>
    internal static bool SweptPolylineWindowHit(IReadOnlyList<Vector2> points, IReadOnlyList<double> distances,
        Vector2 targetStart, Vector2 targetEnd, double headStart, double headEnd, double length, double inset, double radius)
    {
        // 每个段只在头尾跨顶点、路径端点或尖端裁剪边界处切分，固定步内使用栈缓存。
        double total = distances[^1], travel = headEnd - headStart;
        if (Math.Min(total, length) <= 2 * inset) return false;
        Span<double> cuts = stackalloc double[12];
        for (int index = 1; index < points.Count; index++)
        {
            // 当前段始终固定；该段内光束两端在每个分区间中都作线性运动。
            double first = distances[index - 1], last = distances[index], span = last - first;
            if (span <= 0 || last < Math.Max(0, headStart - length) + inset || first > Math.Min(total, headEnd) - inset) continue;
            // 先以目标轨迹和固定段包围盒排除远处段；包围盒只作粗筛，不决定命中。
            Vector2 a = points[index - 1], b = points[index];
            if (Math.Min(targetStart.X, targetEnd.X) - radius > Math.Max(a.X, b.X)
                || Math.Max(targetStart.X, targetEnd.X) + radius < Math.Min(a.X, b.X)
                || Math.Min(targetStart.Y, targetEnd.Y) - radius > Math.Max(a.Y, b.Y)
                || Math.Max(targetStart.Y, targetEnd.Y) + radius < Math.Min(a.Y, b.Y)) continue;
            int count = 2;
            cuts[0] = 0;
            cuts[1] = 1;
            AddWindowCut(cuts, ref count, 0, headStart, travel);
            AddWindowCut(cuts, ref count, length, headStart, travel);
            AddWindowCut(cuts, ref count, total, headStart, travel);
            AddWindowCut(cuts, ref count, total + length, headStart, travel);
            AddWindowCut(cuts, ref count, 2 * inset, headStart, travel);
            AddWindowCut(cuts, ref count, total + length - 2 * inset, headStart, travel);
            AddWindowCut(cuts, ref count, first + inset, headStart, travel);
            AddWindowCut(cuts, ref count, last + inset, headStart, travel);
            AddWindowCut(cuts, ref count, first + length - inset, headStart, travel);
            AddWindowCut(cuts, ref count, last + length - inset, headStart, travel);
            cuts[..count].Sort();
            for (int part = 1; part < count; part++)
            {
                // 用分区间内部判定存在性，避免将尚未出生或已经离开的空窗口算成端点球。
                double from = cuts[part - 1], to = cuts[part];
                if (to <= from) continue;
                double middleHead = headStart + travel * ((from + to) * 0.5);
                double middleTail = Math.Max(0, middleHead - length) + inset;
                double middleEnd = Math.Min(total, middleHead) - inset;
                if (middleTail >= middleEnd || middleEnd <= first || middleTail >= last) continue;
                double h0 = headStart + travel * from, h1 = headStart + travel * to;
                Vector2 a0 = points[index - 1].Lerp(points[index], (float)Math.Clamp((Math.Max(0, h0 - length) + inset - first) / span, 0, 1));
                Vector2 b0 = points[index - 1].Lerp(points[index], (float)Math.Clamp((Math.Min(total, h0) - inset - first) / span, 0, 1));
                Vector2 a1 = points[index - 1].Lerp(points[index], (float)Math.Clamp((Math.Max(0, h1 - length) + inset - first) / span, 0, 1));
                Vector2 b1 = points[index - 1].Lerp(points[index], (float)Math.Clamp((Math.Min(total, h1) - inset - first) / span, 0, 1));
                Vector2 p0 = targetStart.Lerp(targetEnd, (float)from), p1 = targetStart.Lerp(targetEnd, (float)to);
                if (SweptCollinearCapsuleHit(p0, p1, a0, b0, a1, b1, (points[index] - points[index - 1]).Normalized(), radius))
                    return true;
            }
        }
        return false;
    }

    /// <summary>将头部跨过指定距离的时刻加入分区间，严格保留在当前时间内部。</summary>
    /// <param name="cuts">容量足够的归一化时间缓存。</param>
    /// <param name="count">已写入数量。</param>
    /// <param name="distance">跨越距离，像素。</param>
    /// <param name="start">当前头部起始距离，像素。</param>
    /// <param name="travel">区间内前进距离，非负像素。</param>
    private static void AddWindowCut(Span<double> cuts, ref int count, double distance, double start, double travel)
    {
        if (travel <= 0) return;
        // 恒速下跨越距离对应唯一时间，不采用按帧数试探的离散碰撞。
        double time = (distance - start) / travel;
        if (time > 0 && time < 1) cuts[count++] = time;
    }

    /// <summary>检测端点沿同一固定轴线移动的胶囊体，主体按线性约束、端帽按圆扫掠求解。</summary>
    /// <param name="p0">目标起点。</param>
    /// <param name="p1">目标终点。</param>
    /// <param name="a0">胶囊起端的初始位置。</param>
    /// <param name="b0">胶囊末端的初始位置。</param>
    /// <param name="a1">胶囊起端的最终位置。</param>
    /// <param name="b1">胶囊末端的最终位置。</param>
    /// <param name="axis">固定单位轴向。</param>
    /// <param name="radius">合并判定半径，像素。</param>
    /// <returns>任意时刻相交时为真。</returns>
    private static bool SweptCollinearCapsuleHit(Vector2 p0, Vector2 p1, Vector2 a0, Vector2 b0,
        Vector2 a1, Vector2 b1, Vector2 axis, double radius)
    {
        if (SweptCircleOrigin(p0 - a0, p1 - a1, radius) || SweptCircleOrigin(p0 - b0, p1 - b1, radius)) return true;
        // 投影到固定轴；矩形主体的四条约束均是时间的一次函数。
        Vector2 r0 = p0 - a0, r1 = p1 - a1;
        double x0 = (double)r0.X * axis.X + (double)r0.Y * axis.Y;
        double x1 = (double)r1.X * axis.X + (double)r1.Y * axis.Y;
        double y0 = (double)r0.X * -axis.Y + (double)r0.Y * axis.X;
        double y1 = (double)r1.X * -axis.Y + (double)r1.Y * axis.X;
        double length0 = (b0 - a0).Dot(axis), length1 = (b1 - a1).Dot(axis);
        double lower = 0, upper = 1;
        return ClipNonnegative(x0, x1, ref lower, ref upper)
            && ClipNonnegative(length0 - x0, length1 - x1, ref lower, ref upper)
            && ClipNonnegative(radius - y0, radius - y1, ref lower, ref upper)
            && ClipNonnegative(radius + y0, radius + y1, ref lower, ref upper);
    }

    /// <summary>将时间范围裁到线性函数非负的部分。</summary>
    /// <param name="start">函数在0时的值。</param>
    /// <param name="end">函数在1时的值。</param>
    /// <param name="lower">当前时间下界。</param>
    /// <param name="upper">当前时间上界。</param>
    /// <returns>裁剪后仍有有效区间时为真。</returns>
    private static bool ClipNonnegative(double start, double end, ref double lower, ref double upper)
    {
        if (start >= 0 && end >= 0) return true;
        if (start < 0 && end < 0) return false;
        // 只在异号时除法，避免平行边界除零。
        double crossing = start / (start - end);
        if (start < 0) lower = Math.Max(lower, crossing);
        else upper = Math.Min(upper, crossing);
        return lower <= upper;
    }

    /// <summary>检查相对运动线段是否触及原点圆，使用双精度投影避免距离平方溢出。</summary>
    /// <param name="start">相对起点，像素。</param>
    /// <param name="end">相对终点，像素。</param>
    /// <param name="radius">非负合并半径，像素。</param>
    /// <returns>接触时为真。</returns>
    private static bool SweptCircleOrigin(Vector2 start, Vector2 end, double radius)
    {
        // 零位移退化为点与圆；所有平方在双精度中计算。
        double dx = (double)end.X - start.X, dy = (double)end.Y - start.Y;
        double length = dx * dx + dy * dy;
        double time = length == 0 ? 0 : Math.Clamp(-((double)start.X * dx + (double)start.Y * dy) / length, 0, 1);
        double x = start.X + dx * time, y = start.Y + dy * time;
        return x * x + y * y <= radius * radius;
    }
}
