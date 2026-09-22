using Godot;
using System;

/// <summary>选择一整组弹幕基础参数，成员统一以Set结尾。</summary>
public enum BulletType
{
    // 当前敌方鳞弹参数。
    ScaleSet,
    // 当前玩家单发参数。
    PlayerSet
}

/// <summary>集中保存不可变的弹幕参数预设，不包含位置、角度和运动行为。</summary>
public sealed class BulletDefaultSet
{
    /// <summary>贴图资源路径；圆点模式可为空。</summary>
    public string? TexturePath { get; private init; }
    /// <summary>图集列数，正整数。</summary>
    public int Hframes { get; private init; }
    /// <summary>图集行数，正整数。</summary>
    public int Vframes { get; private init; }
    /// <summary>从0开始按行排列的贴图索引。</summary>
    public int ColorIndex { get; private init; }
    /// <summary>非负弹速，逻辑像素/秒。</summary>
    public float Speed { get; private init; }
    /// <summary>正数寿命，单位为秒。</summary>
    public float LifetimeSeconds { get; private init; }
    /// <summary>正数碰撞半径，单位为逻辑像素。</summary>
    public float Radius { get; private init; }
    /// <summary>正数发射间隔，单位为秒，仅供攻击调度方计时。</summary>
    public double IntervalSeconds { get; private init; }
    /// <summary>子弹所属阵营。</summary>
    public BulletTeam Team { get; private init; }
    /// <summary>正整数伤害点数。</summary>
    public int Damage { get; private init; }
    /// <summary>正数贴图倍率，不改变碰撞半径；圆点按半径绘制。</summary>
    public float VisualScale { get; private init; }
    /// <summary>true使用贴图，false使用圆点。</summary>
    public bool UseSprite { get; private init; }
    /// <summary>圆点模式的显示颜色，含透明度。</summary>
    public Color CircleColor { get; private init; }
    /// <summary>仅允许类内创建预设，外部不能改变公共默认值。</summary>
    private BulletDefaultSet() { }
    // 现有敌弹的完整参数，贴图10列1行，首发由阶段等待1秒。
    private static readonly BulletDefaultSet Scale = new()
    {
        TexturePath = "res://Assets/Sprite_02.png", Hframes = 10, Vframes = 1, ColorIndex = 0,
        Speed = 180, LifetimeSeconds = 4, Radius = 6, IntervalSeconds = 1,
        Team = BulletTeam.Enemy, Damage = 1, VisualScale = 3, UseSprite = true, CircleColor = Colors.Cyan
    };
    // 现有玩家弹的完整参数，使用青色圆点，首发由玩家攻击等待0.2秒。
    private static readonly BulletDefaultSet Player = new()
    {
        TexturePath = null, Hframes = 1, Vframes = 1, ColorIndex = 0,
        Speed = 600, LifetimeSeconds = 2, Radius = 3, IntervalSeconds = 0.2,
        Team = BulletTeam.Player, Damage = 1, VisualScale = 3, UseSprite = false, CircleColor = Colors.Cyan
    };
    /// <summary>按枚举取得不可变预设，未知枚举立即报错。</summary>
    /// <param name="type">要使用的弹幕参数集。</param>
    /// <returns>可安全共享的只读参数集。</returns>
    public static BulletDefaultSet Get(BulletType type) => type switch
    {
        BulletType.ScaleSet => Scale,
        BulletType.PlayerSet => Player,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
