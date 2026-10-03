using Godot;
using System;

/// <summary>不可变的反射关联区域；限制可反射的矩形边或圆周弧，不改变形状的范围判断。</summary>
public sealed class VReflectionRegion
{
    /// <summary>以世界坐标定义的关联形状。</summary>
    public IRegionShape Shape { get; }
    /// <summary>矩形允许反射的边；圆形不使用此值。</summary>
    public RectangleEdges Edges { get; }
    /// <summary>圆弧起始弧度，0向右，顺时针为正。</summary>
    public double AngleMin { get; }
    /// <summary>圆弧结束弧度；可小于起始值，表示跨过0。</summary>
    public double AngleMax { get; }

    /// <summary>关联矩形并选择反射边。</summary>
    /// <param name="shape">不可变世界矩形。</param>
    /// <param name="edges">允许反射的边组合，默认四边，可为None。</param>
    public VReflectionRegion(RectangleRegionShape shape, RectangleEdges edges = RectangleEdges.All)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if ((edges & ~RectangleEdges.All) != 0) throw new ArgumentOutOfRangeException(nameof(edges));
        Shape = shape;
        Edges = edges;
    }

    /// <summary>关联圆形并选择顺时针反射弧，默认整圆。</summary>
    /// <param name="shape">不可变世界圆形。</param>
    /// <param name="angleMin">有限起始弧度，默认0。</param>
    /// <param name="angleMax">有限终止弧度，默认TAU；原始跨度达到整圈表示全圆，相同值仅表示该方向。</param>
    public VReflectionRegion(CircleRegionShape shape, double angleMin = 0, double angleMax = Math.Tau)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (!double.IsFinite(angleMin) || !double.IsFinite(angleMax)) throw new ArgumentOutOfRangeException(nameof(angleMin));
        Shape = shape;
        AngleMin = angleMin;
        AngleMax = angleMax;
    }

    /// <summary>根据实际出界交点判断选区，返回镜面反射后的速度，保持速率。</summary>
    /// <param name="exit">关联形状的出界交点。</param>
    /// <param name="velocity">本步加速后的有限实际速度，像素/秒。</param>
    /// <param name="reflected">允许反射时的新速度；不允许时保持输入。</param>
    /// <returns>该边或圆弧启用反射时为真。</returns>
    internal bool TryReflect(RegionExit exit, Vector2 velocity, out Vector2 reflected)
    {
        reflected = velocity;
        if (Shape is RectangleRegionShape)
        {
            // 角点只翻转被选择的边所对应轴，未选边仍可穿出。
            var selected = exit.Edges & Edges;
            if (selected == RectangleEdges.None) return false;
            if ((selected & (RectangleEdges.Left | RectangleEdges.Right)) != 0) reflected.X = -reflected.X;
            if ((selected & (RectangleEdges.Top | RectangleEdges.Bottom)) != 0) reflected.Y = -reflected.Y;
            return true;
        }
        // 用相对顺时针角判断跨零区间，两个端点均包含。
        double angle = VMath.GetAngleBetween2Points(Vector2.Zero, exit.Normal);
        double start = VMath.StandardizationAngle(AngleMin);
        double span = VMath.StandardizationAngle(VMath.StandardizationAngle(AngleMax) - start);
        double offset = VMath.StandardizationAngle(angle - start);
        if (Math.Abs(AngleMax - AngleMin) < Math.Tau && offset > span + 1e-7 && Math.Tau - offset > 1e-7) return false;
        reflected = VMath.ReflectVector(velocity, exit.Normal);
        return true;
    }
}
