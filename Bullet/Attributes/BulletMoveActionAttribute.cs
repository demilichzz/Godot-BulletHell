/// <summary>一次极坐标或直角坐标出生位移。</summary>
public sealed record BulletMoveActionAttribute
{
    /// <summary>位移类型，PMove或XYMove。</summary>
    public string Type { get; init; } = "";
    /// <summary>PMove角度参数，弧度，0向右且顺时针为正。</summary>
    public BulletScalarAttribute? Angle { get; init; } = null;
    /// <summary>PMove距离参数，逻辑像素，可为负。</summary>
    public BulletScalarAttribute? Dist { get; init; } = null;
    /// <summary>XYMove横向参数，逻辑像素，向右为正。</summary>
    public BulletScalarAttribute? X { get; init; } = null;
    /// <summary>XYMove纵向参数，逻辑像素，向下为正。</summary>
    public BulletScalarAttribute? Y { get; init; } = null;
}
