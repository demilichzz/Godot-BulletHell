/// <summary>继承共用节点核心参数，增加子弹碰撞与显示倍率。</summary>
public sealed record BulletCoreAttribute : VNodeCoreAttribute
{
    /// <summary>设置子弹默认寿命为四秒。</summary>
    public BulletCoreAttribute() => LifeTimeS = 4;
    /// <summary>固定碰撞半径，正数逻辑像素。</summary>
    public double Radius { get; init; } = 6;
    /// <summary>固定显示倍率，正数且不影响碰撞。</summary>
    public double VisualScale { get; init; } = 3;
}
