/// <summary>弹幕共用的贴图和混合模式；激光的Display仅使用BlendMode。</summary>
public sealed record VBulletDisplayAttribute
{
    /// <summary>Mix为普通透明混合，Add为整体加算；激光另支持CoreAdd，仅亮芯置顶加算、主体和外光普通混合。默认Mix。</summary>
    public string BlendMode { get; init; } = "Mix";
    /// <summary>贴图名称，Scale、Dot、Drop或Star。</summary>
    public string TextureName { get; init; } = "Scale";
    /// <summary>从零开始的图集索引，当前范围0至9。</summary>
    public int TextureIndex { get; init; } = 0;
}
