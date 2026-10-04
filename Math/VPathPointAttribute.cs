/// <summary>相对本段参考点的双精度路径坐标，单位为逻辑像素。</summary>
public sealed record VPathPointAttribute
{
    /// <summary>横坐标，向右为正，JSON必须显式填写。</summary>
    public double X { get; init; }
    /// <summary>纵坐标，向下为正，JSON必须显式填写。</summary>
    public double Y { get; init; }
}
