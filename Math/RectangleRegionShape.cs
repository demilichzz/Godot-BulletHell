using Godot;
using System;

/// <summary>不可变轴对齐矩形区域，世界坐标向右、向下递增，包含四条边。</summary>
public sealed class RectangleRegionShape : IRegionShape
{
    /// <summary>世界矩形，尺寸为正，单位为逻辑像素。</summary>
    public Rect2 Bounds { get; }

    /// <summary>创建轴对齐闭矩形。</summary>
    /// <param name="bounds">有限世界矩形，宽高须为正，逻辑像素。</param>
    public RectangleRegionShape(Rect2 bounds)
    {
        VMath.ValidateQueryRectangle(bounds);
        if (bounds.Size.X <= 0 || bounds.Size.Y <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        Bounds = bounds;
    }

    /// <inheritdoc/>
    public bool Contains(Vector2 center, double radius = 0)
    {
        VMath.ValidateRegionObject(center, radius);
        return center.X >= Bounds.Position.X + radius && center.X <= Bounds.End.X - radius
            && center.Y >= Bounds.Position.Y + radius && center.Y <= Bounds.End.Y - radius;
    }

    /// <inheritdoc/>
    public bool IntersectsCircle(Vector2 center, double radius)
        => VMath.CircleIntersectsRect(center, radius, Bounds);

    /// <inheritdoc/>
    public Vector2 Clamp(Vector2 center, double radius = 0)
    {
        VMath.ValidateRegionObject(center, radius);
        if (radius > Bounds.Size.X * 0.5 || radius > Bounds.Size.Y * 0.5)
            throw new ArgumentOutOfRangeException(nameof(radius), "判定圆无法放入区域。");
        return new Vector2((float)Math.Clamp(center.X, Bounds.Position.X + radius, Bounds.End.X - radius),
            (float)Math.Clamp(center.Y, Bounds.Position.Y + radius, Bounds.End.Y - radius));
    }

    /// <inheritdoc/>
    public bool TryGetExit(Vector2 start, Vector2 end, out RegionExit exit)
    {
        VMath.ValidateRegionObject(start, 0);
        VMath.ValidateRegionObject(end, 0);
        exit = default;
        // 闭边界与交点单精度舍入相容，但不会把明显位于区域外的弹幕拉回。
        if (start.X < Bounds.Position.X - 0.001 || start.X > Bounds.End.X + 0.001
            || start.Y < Bounds.Position.Y - 0.001 || start.Y > Bounds.End.Y + 0.001 || Contains(end)) return false;
        double dx = (double)end.X - start.X, dy = (double)end.Y - start.Y;
        double tx = dx > 0 ? (Bounds.End.X - (double)start.X) / dx
            : dx < 0 ? (Bounds.Position.X - (double)start.X) / dx : double.PositiveInfinity;
        double ty = dy > 0 ? (Bounds.End.Y - (double)start.Y) / dy
            : dy < 0 ? (Bounds.Position.Y - (double)start.Y) / dy : double.PositiveInfinity;
        double fraction = Math.Min(tx, ty);
        if (!double.IsFinite(fraction) || fraction < -0.000001 || fraction > 1) return false;
        // 同时接触两边时分别保留标记，不能用斜法线只反射一次来替代两轴翻转。
        RectangleEdges edges = RectangleEdges.None;
        Vector2 normal = Vector2.Zero;
        if (Math.Abs(tx - fraction) <= 1e-10)
        {
            edges |= dx > 0 ? RectangleEdges.Right : RectangleEdges.Left;
            normal.X = dx > 0 ? 1 : -1;
        }
        if (Math.Abs(ty - fraction) <= 1e-10)
        {
            edges |= dy > 0 ? RectangleEdges.Bottom : RectangleEdges.Top;
            normal.Y = dy > 0 ? 1 : -1;
        }
        fraction = Math.Clamp(fraction, 0, 1);
        exit = new RegionExit
        {
            Point = new Vector2((float)(start.X + dx * fraction), (float)(start.Y + dy * fraction)),
            Normal = normal.Normalized(),
            Fraction = fraction,
            Edges = edges
        };
        return true;
    }
}
