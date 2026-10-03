using Godot;
using System;

/// <summary>不可变圆形区域，采用世界坐标，包含圆周边界。</summary>
public sealed class CircleRegionShape : IRegionShape
{
    /// <summary>有限世界圆心，逻辑像素。</summary>
    public Vector2 Center { get; }
    /// <summary>有限正半径，逻辑像素。</summary>
    public double Radius { get; }

    /// <summary>创建闭合圆形区域。</summary>
    /// <param name="center">有限世界圆心，逻辑像素。</param>
    /// <param name="radius">有限正半径，逻辑像素；边界须在Vector2范围内。</param>
    public CircleRegionShape(Vector2 center, double radius)
    {
        VMath.ValidateRegionObject(center, radius);
        if (radius <= 0 || Math.Abs(center.X) + radius > float.MaxValue || Math.Abs(center.Y) + radius > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(radius));
        Center = center;
        Radius = radius;
    }

    /// <inheritdoc/>
    public bool Contains(Vector2 center, double radius = 0)
    {
        VMath.ValidateRegionObject(center, radius);
        // 缩小区域后判断圆心，半径过大的对象不可能完整包含。
        double available = Radius - radius;
        double dx = (double)center.X - Center.X, dy = (double)center.Y - Center.Y;
        return available >= 0 && dx * dx + dy * dy <= available * available;
    }

    /// <inheritdoc/>
    public bool IntersectsCircle(Vector2 center, double radius)
    {
        VMath.ValidateRegionObject(center, radius);
        // 半径和比较区分“接触区域”与“完整包含”。
        double reach = Radius + radius;
        double dx = (double)center.X - Center.X, dy = (double)center.Y - Center.Y;
        return dx * dx + dy * dy <= reach * reach;
    }

    /// <inheritdoc/>
    public Vector2 Clamp(Vector2 center, double radius = 0)
    {
        VMath.ValidateRegionObject(center, radius);
        if (radius > Radius) throw new ArgumentOutOfRangeException(nameof(radius), "判定圆无法放入区域。");
        // 常规范围保持原角色LimitLength的单精度运算，极端坐标使用双精度投影。
        var offset = center - Center;
        if (offset.IsFinite() && float.IsFinite(offset.LengthSquared()))
            return Center + offset.LimitLength((float)(Radius - radius));
        double dx = (double)center.X - Center.X, dy = (double)center.Y - Center.Y;
        double scale = (Radius - radius) / Math.Sqrt(dx * dx + dy * dy);
        return new Vector2((float)(Center.X + dx * scale), (float)(Center.Y + dy * scale));
    }

    /// <inheritdoc/>
    public bool TryGetExit(Vector2 start, Vector2 end, out RegionExit exit)
    {
        VMath.ValidateRegionObject(start, 0);
        VMath.ValidateRegionObject(end, 0);
        exit = default;
        // 接触点转回Vector2时允许极小舍入，避免第二次反射误判为起点已在外。
        double sx = (double)start.X - Center.X, sy = (double)start.Y - Center.Y;
        if (sx * sx + sy * sy > (Radius + 0.001) * (Radius + 0.001) || Contains(end)) return false;
        double dx = (double)end.X - start.X, dy = (double)end.Y - start.Y;
        double a = dx * dx + dy * dy, b = sx * dx + sy * dy;
        if (a == 0) return false;
        double c = sx * sx + sy * sy - Radius * Radius;
        double discriminant = b * b - a * c;
        if (discriminant < 0) return false;
        // 取离开圆的较大根；向外且起点接近圆周时用等价式避免相减抵消。
        double root = Math.Sqrt(discriminant);
        double fraction = b > 0 ? -c / (b + root) : (-b + root) / a;
        if (fraction < -0.000001 || fraction > 1) return false;
        fraction = Math.Clamp(fraction, 0, 1);
        var point = new Vector2((float)(start.X + dx * fraction), (float)(start.Y + dy * fraction));
        var normal = (point - Center).Normalized();
        if (dx * normal.X + dy * normal.Y <= 0) return false;
        exit = new RegionExit { Point = point, Normal = normal, Fraction = fraction };
        return true;
    }
}
