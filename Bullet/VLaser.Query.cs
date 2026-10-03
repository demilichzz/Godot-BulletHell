using Godot;
using System;

/// <summary>供矩形查询与AI使用的当前激光几何，不推进时钟或渲染。</summary>
public partial class VLaser
{
    /// <summary>查询预警或当前生效路径是否覆盖矩形，消退及结束返回否。</summary>
    /// <param name="rectangle">世界逻辑像素矩形，尺寸非负。</param>
    /// <returns>正式碰撞宽度与矩形相交时为真。</returns>
    public bool IntersectsRect(Rect2 rectangle)
    {
        VMath.ValidateQueryRectangle(rectangle);
        if (!TryGetQueryWindow(out double tail, out double head)) return false;
        // 按原路径及出生时冻结的末段延长线检查当前窗口。
        for (int index = 1; index < _collisionPath.Length; index++)
            if (TryGetQuerySegment(index, tail, head, out var start, out var end)
                && VMath.CapsuleIntersectsRect(start, end, Settings.HitWidth * 0.5, rectangle)) return true;
        return false;
    }

    /// <summary>查找危险或预警中心线上的最近点，相等距离保持原路径顺序。</summary>
    /// <param name="point">有限世界位置，逻辑像素。</param>
    /// <param name="nearest">返回最近中心线位置，逻辑像素。</param>
    /// <param name="tangent">返回对应线段单位切向量，右下为正。</param>
    /// <returns>存在有效危险线段时为真。</returns>
    public bool TryGetNearestHazard(Vector2 point, out Vector2 nearest, out Vector2 tangent)
    {
        if (!point.IsFinite()) throw new ArgumentOutOfRangeException(nameof(point));
        nearest = tangent = Vector2.Zero;
        if (!TryGetQueryWindow(out double tail, out double head)) return false;
        // 最短距离平方，遍历时不开方。
        double best = double.PositiveInfinity;
        for (int index = 1; index < _collisionPath.Length; index++)
        {
            if (!TryGetQuerySegment(index, tail, head, out var start, out var end)) continue;
            // 当前段上的投影位置和距离平方。
            var candidate = VMath.ClosestPointOnSegment(point, start, end);
            double dx = (double)point.X - candidate.X, dy = (double)point.Y - candidate.Y;
            double distance = dx * dx + dy * dy;
            if (distance >= best) continue;
            best = distance;
            nearest = candidate;
            tangent = (end - start).Normalized();
        }
        return double.IsFinite(best);
    }

    /// <summary>按正式碰撞规则计算弧长窗口，预警使用正式碰撞中心线。</summary>
    /// <param name="tail">返回裁剪后的起始弧长，像素。</param>
    /// <param name="head">返回裁剪后的结束弧长，像素。</param>
    /// <returns>存在非空危险或预警区域时为真。</returns>
    private bool TryGetQueryWindow(out double tail, out double head)
    {
        tail = head = 0;
        if (!IsAlive || Stage is VLaserStage.Fade or VLaserStage.End) return false;
        // 尖端裁剪与实际碰撞保持一致，不使用外发光几何。
        double inset = Settings.EndCap == "Point" ? Settings.TipLength + Settings.HitWidth * 0.5 : 0;
        tail = TailDistance + inset;
        head = Math.Min(_collisionDistances[^1], HeadDistance) - inset;
        return head > tail;
    }

    /// <summary>把固定路径的一段裁到弧长窗口，不生成临时路径数组。</summary>
    /// <param name="index">线段终点索引，大于零。</param>
    /// <param name="tail">窗口起始弧长，像素。</param>
    /// <param name="head">窗口结束弧长，像素。</param>
    /// <param name="start">裁剪后世界起点，像素。</param>
    /// <param name="end">裁剪后世界终点，像素。</param>
    /// <returns>与窗口有正长度交集时为真。</returns>
    private bool TryGetQuerySegment(int index, double tail, double head, out Vector2 start, out Vector2 end)
    {
        start = end = Vector2.Zero;
        // 当前线段累计弧长与查询窗口的交集。
        double first = _collisionDistances[index - 1], last = _collisionDistances[index];
        double from = Math.Max(first, tail), to = Math.Min(last, head);
        if (to <= from) return false;
        start = _collisionPath[index - 1].Lerp(_collisionPath[index], (float)((from - first) / (last - first)));
        end = _collisionPath[index - 1].Lerp(_collisionPath[index], (float)((to - first) / (last - first)));
        return true;
    }
}