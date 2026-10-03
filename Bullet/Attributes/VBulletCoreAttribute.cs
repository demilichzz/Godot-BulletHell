/// <summary>继承共用节点核心参数，增加子弹碰撞与显示倍率。</summary>
public sealed record VBulletCoreAttribute : VNodeCoreAttribute
{
    /// <summary>设置子弹默认寿命为四秒。</summary>
    public VBulletCoreAttribute() => LifeTimeMs = 4000;
    /// <summary>中心连续出界后的强制消失阈值，非负整数毫秒，默认2000；回区清零。</summary>
    public long OutsideTimeoutMs { get; init; } = 2000;
    /// <summary>可选世界出界判定区域；省略使用游戏区域，与反射区域独立。</summary>
    public VRegionShapeAttribute? OutsideRegion { get; init; }
    /// <summary>是否启用选定边界反射，默认false；true时必须配置ReflectionRegion。</summary>
    public bool Reflectable { get; init; }
    /// <summary>以世界坐标定义的反射关联区域，默认null；不改变OutsideRegion的出界判定。</summary>
    public VReflectionRegionAttribute? ReflectionRegion { get; init; }
    /// <summary>固定碰撞半径，正数逻辑像素。</summary>
    public double Radius { get; init; } = 6;
    /// <summary>固定显示倍率，正数且不影响碰撞。</summary>
    public double VisualScale { get; init; } = 3;
}
