/// <summary>整队共用的贴图名称与图集索引。</summary>
public sealed record BulletDisplayAttribute
{
    /// <summary>贴图名称，Scale、Dot、Drop或Star。</summary>
    public string TextureName { get; init; } = "Scale";
    /// <summary>从零开始的图集索引，当前范围0至9。</summary>
    public int TextureIndex { get; init; } = 0;
}
