using System;

/// <summary>可选的弹幕方向排列工具，不负责创建子弹或控制发射时序。</summary>
public abstract class BulletPattern
{
    /// <summary>逐颗输出排列方向。</summary>
    /// <param name="emit">接收角度的回调，单位为度，0向右、90向下。</param>
    /// <param name="angle">第一颗角度，单位为度，0向右、90向下。</param>
    public abstract void Emit(Action<float> emit, float angle);
}
/// <summary>沿指定初始方向输出单颗子弹角度。</summary>
public sealed class SinglePattern : BulletPattern
{
    /// <summary>输出唯一方向。</summary>
    /// <param name="emit">接收角度的回调，单位为度。</param>
    /// <param name="angle">有限角度，单位为度，0向右、90向下。</param>
    public override void Emit(Action<float> emit, float angle)
    {
        ArgumentNullException.ThrowIfNull(emit);
        if (!float.IsFinite(angle)) throw new ArgumentOutOfRangeException(nameof(angle));
        emit(angle);
    }
}
/// <summary>从单一起点按固定角度间隔排列方向，支持圆环、扇形和单发。</summary>
public sealed class AngularPattern : BulletPattern
{
    // 子弹总数、非负角度间隔（度）和顺时针标志。
    private readonly int _count;
    private readonly float _offsetDegrees;
    private readonly bool _clockwise;
    /// <summary>定义排列数量、间隔和方向。</summary>
    /// <param name="count">正整数子弹总数，默认24。</param>
    /// <param name="offsetDegrees">相邻方向的非负有限角度差，单位为度，默认15。</param>
    /// <param name="clockwise">true为顺时针增加角度，false为逆时针，默认true。</param>
    public AngularPattern(int count = 24, float offsetDegrees = 15, bool clockwise = true)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (!float.IsFinite(offsetDegrees) || offsetDegrees < 0) throw new ArgumentOutOfRangeException(nameof(offsetDegrees));
        _count = count;
        _offsetDegrees = offsetDegrees;
        _clockwise = clockwise;
    }
    /// <summary>按索引逐颗输出方向，不重复附加圆环终点。</summary>
    /// <param name="emit">接收角度的回调，单位为度。</param>
    /// <param name="angle">第一颗的有限角度，单位为度，0向右、90向下。</param>
    public override void Emit(Action<float> emit, float angle)
    {
        ArgumentNullException.ThrowIfNull(emit);
        if (!float.IsFinite(angle)) throw new ArgumentOutOfRangeException(nameof(angle));
        // 索引从零开始，方向符号决定顺逆时针。
        for (int index = 0; index < _count; index++)
            emit(angle + (_clockwise ? 1 : -1) * index * _offsetDegrees);
    }
}
