using Godot;
using System;

/// <summary>选择一整组弹幕基础参数，成员统一以Set结尾。</summary>
public enum BulletType
{
    // 当前敌方鳞弹参数。
    ScaleSet,
    // 当前玩家单发参数。
    PlayerSet,
    // 圆形贴图弹幕，除贴图外沿用鳞弹参数。
    DotSet,
    // 水滴贴图弹幕，除贴图外沿用鳞弹参数。
    DropSet,
    // 星形贴图弹幕，除贴图外沿用鳞弹参数。
    StarSet
}

/// <summary>集中保存不可变的弹幕完整初始化参数与默认预设。</summary>
public sealed record BulletDefaultSet
{
    /// <summary>全局起点，逻辑像素，右和下为正，默认原点。</summary>
    public Vector2 Position { get; init; }
    /// <summary>有限发射角度，单位为弧度，0向右、π/2向下，默认0。</summary>
    public double AngleRadians { get; init; }
    /// <summary>贴图资源路径；圆点模式可为空。</summary>
    public string? TexturePath { get; init; }
    /// <summary>图集列数，正整数。</summary>
    public int Hframes { get; init; }
    /// <summary>图集行数，正整数。</summary>
    public int Vframes { get; init; }
    /// <summary>从0开始按行排列的贴图索引。</summary>
    public int ColorIndex { get; init; }
    /// <summary>有符号弹速，逻辑像素/秒；负值沿角度反向移动。</summary>
    public double Speed { get; init; }
    /// <summary>加速度方向，弧度，0向右且顺时针为正。</summary>
    public double AAngle { get; init; }
    /// <summary>有符号加速度，逻辑像素每平方秒，默认零。</summary>
    public double ASpeed { get; init; }
    /// <summary>为真时忽略AAngle，沿外部运动方向施加加速度。</summary>
    public bool AAngleIsSameAsAngle { get; init; } = true;
    /// <summary>正数寿命，单位为秒。</summary>
    public double LifetimeSeconds { get; init; }
    /// <summary>正数碰撞半径，单位为逻辑像素。</summary>
    public double Radius { get; init; }
    /// <summary>子弹所属阵营。</summary>
    public BulletTeam Team { get; init; }
    /// <summary>正整数伤害点数。</summary>
    public int Damage { get; init; }
    /// <summary>正数贴图倍率，不改变碰撞半径；圆点按半径绘制。</summary>
    public double VisualScale { get; init; }
    /// <summary>true使用贴图，false使用圆点。</summary>
    public bool UseSprite { get; init; }
    /// <summary>圆点模式的显示颜色，含透明度。</summary>
    public Color CircleColor { get; init; }
    /// <summary>运动行为，with仅浅复制引用；默认直线，有状态行为须每颗独立创建。</summary>
    public BulletBehavior Behavior { get; init; } = new StraightBehavior();
    /// <summary>仅供静态默认预设初始化。</summary>
    private BulletDefaultSet() { }
    // 现有敌弹的完整参数，贴图10列1行。
    private static readonly BulletDefaultSet Scale = new()
    {
        TexturePath = "res://Assets/Sprites/Sprite_scale.png", Hframes = 10, Vframes = 1, ColorIndex = 0,
        Speed = 180, LifetimeSeconds = 4, Radius = 6,
        Team = BulletTeam.Enemy, Damage = 1, VisualScale = 3, UseSprite = true, CircleColor = Colors.Cyan
    };
    // 三种贴图各为10列1行；复用鳞弹全部非贴图参数及无状态直线行为。
    private static readonly BulletDefaultSet Dot = Scale with { TexturePath = "res://Assets/Sprites/Sprite_dot.png" };
    private static readonly BulletDefaultSet Drop = Scale with { TexturePath = "res://Assets/Sprites/Sprite_drop.png" };
    private static readonly BulletDefaultSet Star = Scale with { TexturePath = "res://Assets/Sprites/Sprite_star.png" };
    // 现有玩家弹的完整参数，使用青色圆点，首发由玩家攻击等待0.2秒。
    private static readonly BulletDefaultSet Player = new()
    {
        TexturePath = null, Hframes = 1, Vframes = 1, ColorIndex = 0,
        Speed = 600, LifetimeSeconds = 2, Radius = 3,
        Team = BulletTeam.Player, Damage = 1, VisualScale = 3, UseSprite = false, CircleColor = Colors.Cyan
    };
    /// <summary>按枚举取得不可变预设，未知枚举立即报错。</summary>
    /// <param name="type">要使用的弹幕参数集。</param>
    /// <returns>可安全共享的只读参数集。</returns>
    public static BulletDefaultSet Get(BulletType type) => type switch
    {
        BulletType.ScaleSet => Scale,
        BulletType.PlayerSet => Player,
        BulletType.DotSet => Dot,
        BulletType.DropSet => Drop,
        BulletType.StarSet => Star,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
