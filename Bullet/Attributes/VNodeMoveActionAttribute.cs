/// <summary>一次极坐标或直角坐标出生位移。</summary>
public sealed record VNodeMoveActionAttribute
{
    /// <summary>位移类型，PMove或XYMove。</summary>
    public string Type { get; init; } = "";
    /// <summary>PMove角度参数，弧度，0向右且顺时针为正。</summary>
    public double? Angle { get; init; } = null;
    /// <summary>PMove距离参数，逻辑像素，可为负。</summary>
    public double? Dist { get; init; } = null;
    /// <summary>XYMove横向参数，逻辑像素，向右为正。</summary>
    public double? X { get; init; } = null;
    /// <summary>XYMove纵向参数，逻辑像素，向下为正。</summary>
    public double? Y { get; init; } = null;
}
