using System;
using System.Text.Json;

/// <summary>一次原子设置运行参数；空字段保持旧值，不修改生成模板。</summary>
public sealed record ParameterActionAttribute
{
    /// <summary>固定方向或动态方向偏移，有限弧度，顺时针为正。</summary>
    public double? Angle { get; init; }
    /// <summary>角度来源，Fixed或执行时从对象当前位置AimPlayer。</summary>
    public string AngleSource { get; init; } = "Fixed";
    /// <summary>外部有符号速度，逻辑像素每秒。</summary>
    public double? Speed { get; init; }
    /// <summary>外部加速度方向，有限弧度。</summary>
    public double? AAngle { get; init; }
    /// <summary>外部有符号加速度，逻辑像素每平方秒。</summary>
    public double? ASpeed { get; init; }
    /// <summary>是否沿外部Angle施加加速度。</summary>
    public bool? AAngleIsSameAsAngle { get; init; }
    /// <summary>横坐标，Follow用父参考系，Snapshot用世界坐标，逻辑像素。</summary>
    public double? X { get; init; }
    /// <summary>纵坐标，坐标系与X相同，逻辑像素。</summary>
    public double? Y { get; init; }
    /// <summary>出生后的总寿命上限，有限正数秒，不是剩余时间。</summary>
    public double? LifeTimeS { get; init; }

    /// <summary>在应用前校验所有字段，不产生运行状态修改。</summary>
    internal void Validate()
    {
        if (AngleSource is not ("Fixed" or "AimPlayer")) throw new JsonException("AngleSource只支持Fixed或AimPlayer。");
        // 所有待设置值先检查，避免半更新。
        foreach (double? value in new[] { Angle, Speed, AAngle, ASpeed, X, Y, LifeTimeS })
            if (value.HasValue && !double.IsFinite(value.Value)) throw new JsonException("参数动作需要有限数值。");
        foreach (double? value in new[] { Speed, ASpeed, X, Y })
            if (value.HasValue && Math.Abs(value.Value) > float.MaxValue) throw new JsonException("运行参数超出坐标或速度范围。");
        if (LifeTimeS <= 0) throw new JsonException("LifeTimeS必须为正数。");
    }
}
