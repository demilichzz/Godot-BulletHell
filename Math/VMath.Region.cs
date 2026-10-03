using Godot;
using System;

/// <summary>区域查询共用的有限性校验。</summary>
public static partial class VMath
{
    /// <summary>验证点或圆形对象的查询参数。</summary>
    /// <param name="center">有限世界中心，逻辑像素。</param>
    /// <param name="radius">有限非负半径，逻辑像素。</param>
    internal static void ValidateRegionObject(Vector2 center, double radius)
    {
        if (!center.IsFinite() || !double.IsFinite(radius) || radius < 0 || radius > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(radius), "范围查询需要有限中心及非负半径。");
    }

    /// <summary>按边界法线镜面反射向量，保留实际速率，不读取随机。</summary>
    /// <param name="velocity">有限实际速度，逻辑像素/秒。</param>
    /// <param name="normal">有限非零边界法线，不要求预先归一化。</param>
    /// <returns>反射后的有限速度向量。</returns>
    public static Vector2 ReflectVector(Vector2 velocity, Vector2 normal)
    {
        if (!velocity.IsFinite() || !normal.IsFinite() || normal == Vector2.Zero)
            throw new ArgumentOutOfRangeException(nameof(normal));
        // 双精度投影避免法线或速度在平方运算中溢出。
        double square = (double)normal.X * normal.X + (double)normal.Y * normal.Y;
        double projection = 2 * ((double)velocity.X * normal.X + (double)velocity.Y * normal.Y) / square;
        var result = new Vector2((float)(velocity.X - projection * normal.X), (float)(velocity.Y - projection * normal.Y));
        if (!result.IsFinite()) throw new OverflowException("反射速度超出Vector2范围。");
        return result;
    }
}
