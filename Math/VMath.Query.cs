using Godot;
using System;

/// <summary>独立随机流工厂和无副作用的二维范围查询工具。</summary>
public static partial class VMath
{
    /// <summary>创建独立随机流，不抽取或重置默认战斗流。</summary>
    /// <param name="seed">固定32位初始种子。</param>
    /// <returns>由调用方持有的全新随机流。</returns>
    public static VRandomStream CreateRandomStream(int seed) => new(seed);

    /// <summary>求有限线段上距离目标最近的点，零长度线段返回起点。</summary>
    /// <param name="point">有限目标世界位置，逻辑像素。</param>
    /// <param name="start">有限线段起点，逻辑像素。</param>
    /// <param name="end">有限线段终点，逻辑像素。</param>
    /// <returns>线段上的最近世界位置。</returns>
    public static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        if (!point.IsFinite() || !start.IsFinite() || !end.IsFinite()) throw new ArgumentOutOfRangeException(nameof(point));
        // 双精度投影避免有限单精度坐标的平方溢出。
        double dx = (double)end.X - start.X, dy = (double)end.Y - start.Y;
        double length = dx * dx + dy * dy;
        double ratio = length == 0 ? 0 : Math.Clamp((((double)point.X - start.X) * dx
            + ((double)point.Y - start.Y) * dy) / length, 0, 1);
        return new Vector2((float)(start.X + dx * ratio), (float)(start.Y + dy * ratio));
    }

    /// <summary>判断圆与闭矩形是否相交，接触边界也包含。</summary>
    /// <param name="center">有限圆心，世界逻辑像素。</param>
    /// <param name="radius">非负有限半径，逻辑像素。</param>
    /// <param name="rectangle">有限世界矩形，尺寸非负，允许退化。</param>
    /// <returns>圆覆盖矩形任意点时为真。</returns>
    public static bool CircleIntersectsRect(Vector2 center, double radius, Rect2 rectangle)
    {
        ValidateQueryRectangle(rectangle);
        if (!center.IsFinite() || !double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        // 圆心到矩形最近点的距离平方。
        double dx = (double)center.X - Math.Clamp(center.X, rectangle.Position.X, rectangle.End.X);
        double dy = (double)center.Y - Math.Clamp(center.Y, rectangle.Position.Y, rectangle.End.Y);
        return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>判断带圆端帽的线段与闭矩形是否相交，精确排除扩张矩形的四角误判。</summary>
    /// <param name="start">有限线段起点，世界逻辑像素。</param>
    /// <param name="end">有限线段终点，世界逻辑像素。</param>
    /// <param name="radius">非负有限线段半宽，逻辑像素。</param>
    /// <param name="rectangle">有限世界矩形，尺寸非负。</param>
    /// <returns>胶囊覆盖矩形任意点时为真。</returns>
    public static bool CapsuleIntersectsRect(Vector2 start, Vector2 end, double radius, Rect2 rectangle)
    {
        if (!start.IsFinite() || !end.IsFinite()) throw new ArgumentOutOfRangeException(nameof(start));
        if (CircleIntersectsRect(start, radius, rectangle) || CircleIntersectsRect(end, radius, rectangle)) return true;
        // 将中心线裁到矩形内部，检查无端点落入的穿越情形。
        double lower = 0, upper = 1;
        if (ClipQueryAxis(start.X, end.X, rectangle.Position.X, rectangle.End.X, ref lower, ref upper)
            && ClipQueryAxis(start.Y, end.Y, rectangle.Position.Y, rectangle.End.Y, ref lower, ref upper)) return true;
        // 不相交时，最短距离位于端点到矩形或角点到线段。
        for (int index = 0; index < 4; index++)
        {
            // 当前角点及其线段投影，不建立临时集合。
            var corner = new Vector2((index & 1) == 0 ? rectangle.Position.X : rectangle.End.X,
                (index & 2) == 0 ? rectangle.Position.Y : rectangle.End.Y);
            var nearest = ClosestPointOnSegment(corner, start, end);
            double dx = (double)corner.X - nearest.X, dy = (double)corner.Y - nearest.Y;
            if (dx * dx + dy * dy <= radius * radius) return true;
        }
        return false;
    }

    /// <summary>校验世界查询矩形，拒绝非有限坐标与负尺寸。</summary>
    /// <param name="rectangle">逻辑像素矩形。</param>
    internal static void ValidateQueryRectangle(Rect2 rectangle)
    {
        if (!rectangle.Position.IsFinite() || !rectangle.Size.IsFinite() || !rectangle.End.IsFinite()
            || rectangle.Size.X < 0 || rectangle.Size.Y < 0) throw new ArgumentOutOfRangeException(nameof(rectangle));
    }

    /// <summary>把归一化线段时间裁到一个闭坐标区间。</summary>
    /// <param name="start">轴向起点像素。</param>
    /// <param name="end">轴向终点像素。</param>
    /// <param name="min">区间下界像素。</param>
    /// <param name="max">区间上界像素。</param>
    /// <param name="lower">归一化时间下界。</param>
    /// <param name="upper">归一化时间上界。</param>
    /// <returns>裁剪后非空时为真。</returns>
    private static bool ClipQueryAxis(double start, double end, double min, double max, ref double lower, ref double upper)
    {
        if (start == end) return start >= min && start <= max;
        // 反向线段交换进入和离开时刻。
        double from = (min - start) / (end - start), to = (max - start) / (end - start);
        if (from > to) (from, to) = (to, from);
        lower = Math.Max(lower, from);
        upper = Math.Min(upper, to);
        return lower <= upper;
    }
}