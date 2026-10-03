/// <summary>激光专用核心参数；主体宽度仅允许配置在激光的Core内。</summary>
public sealed record VLaserCoreAttribute : VNodeCoreAttribute
{
    /// <summary>主体全宽，正数逻辑像素，默认8；不含外发光。</summary>
    public double Width { get; init; } = 8;
}
