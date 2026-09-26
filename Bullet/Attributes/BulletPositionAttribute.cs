/// <summary>节点和子弹共用的引用、跟随模式及出生位移配置。</summary>
public sealed record BulletPositionAttribute
{
    /// <summary>Emitter或同一发射器内的VNodeQueue标识。</summary>
    public string RefObject { get; init; } = "Emitter";
    /// <summary>Follow或Snapshot；省略时节点跟随、子弹快照。</summary>
    public string? Mode { get; init; }
    /// <summary>按数组顺序累计的出生位移，默认空。</summary>
    public System.Collections.Generic.IReadOnlyList<BulletMoveActionAttribute> RefMoveQueue { get; init; } = System.Array.Empty<BulletMoveActionAttribute>();
}
