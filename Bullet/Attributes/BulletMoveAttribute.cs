/// <summary>基础、增量与随机宽度共用的四项运动属性。</summary>
public sealed record BulletMoveAttribute
{
    /// <summary>运动角或瞄准偏移，弧度，顺时针为正。</summary>
    public double Angle { get; init; } = 0;
    /// <summary>有符号速度或其增量，逻辑像素每秒。</summary>
    public double Speed { get; init; } = 0;
    /// <summary>加速度角或其增量，弧度，顺时针为正。</summary>
    public double AAngle { get; init; } = 0;
    /// <summary>有符号加速度或其增量，逻辑像素每平方秒。</summary>
    public double ASpeed { get; init; } = 0;
}
