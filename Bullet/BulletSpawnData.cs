using Godot;
using System;

/// <summary>保存可逐项修改或批量应用预设的子弹初始化参数，位置使用全局逻辑像素。</summary>
public sealed record BulletSpawnData
{
    /// <summary>全局起点，逻辑像素，右和下为正，默认原点。</summary>
    public Vector2 Position { get; set; }
    /// <summary>有限发射角度，单位为弧度，0向右、π/2向下，默认0。</summary>
    public float AngleRadians { get; set; }
    /// <summary>贴图资源路径；贴图模式必须有效，圆点模式可为空。</summary>
    public string? TexturePath { get; set; }
    /// <summary>图集列数，正整数，默认由预设提供。</summary>
    public int Hframes { get; set; }
    /// <summary>图集行数，正整数，默认由预设提供。</summary>
    public int Vframes { get; set; }
    /// <summary>按行排列的零基贴图索引，必须小于行列乘积。</summary>
    public int ColorIndex { get; set; }
    /// <summary>非负有限速度，逻辑像素/秒，默认由预设提供。</summary>
    public float Speed { get; set; }
    /// <summary>正数有限寿命，单位为秒，默认由预设提供。</summary>
    public float LifetimeSeconds { get; set; }
    /// <summary>正数有限碰撞半径，逻辑像素，默认由预设提供。</summary>
    public float Radius { get; set; }
    /// <summary>正数有限发射间隔，单位为秒，仅供阶段或玩家攻击组件读取。</summary>
    public double IntervalSeconds { get; set; }
    /// <summary>所属阵营，默认由预设提供。</summary>
    public BulletTeam Team { get; set; }
    /// <summary>正整数伤害点数，默认由预设提供。</summary>
    public int Damage { get; set; }
    /// <summary>正数有限贴图倍率，不改变碰撞范围；圆点仍按半径绘制。</summary>
    public float VisualScale { get; set; }
    /// <summary>true显示贴图，false显示指定颜色圆点。</summary>
    public bool UseSprite { get; set; }
    /// <summary>圆点颜色，RGBA各分量范围0～1，默认由预设提供。</summary>
    public Color CircleColor { get; set; }
    /// <summary>运动行为，独立于预设，默认直线；有状态行为须每颗独立创建。</summary>
    public BulletBehavior Behavior { get; set; } = new StraightBehavior();
    /// <summary>创建独立参数对象并应用指定预设。</summary>
    /// <param name="type">基础参数集，默认ScaleSet。</param>
    public BulletSpawnData(BulletType type = BulletType.ScaleSet) => setPattern(type);
    /// <summary>覆盖全部预设字段，保留位置、角度和运动行为；后续可逐项覆盖。</summary>
    /// <param name="type">要批量复制的参数集，未知枚举抛错且不修改原数据。</param>
    public void setPattern(BulletType type)
    {
        // 先取得合法预设，再复制值，避免无效枚举导致部分修改。
        var preset = BulletDefaultSet.Get(type);
        TexturePath = preset.TexturePath;
        Hframes = preset.Hframes;
        Vframes = preset.Vframes;
        ColorIndex = preset.ColorIndex;
        Speed = preset.Speed;
        LifetimeSeconds = preset.LifetimeSeconds;
        Radius = preset.Radius;
        IntervalSeconds = preset.IntervalSeconds;
        Team = preset.Team;
        Damage = preset.Damage;
        VisualScale = preset.VisualScale;
        UseSprite = preset.UseSprite;
        CircleColor = preset.CircleColor;
    }
    /// <summary>在创建节点前校验参数、图集索引和贴图资源。</summary>
    public void Validate()
    {
        if (!Position.IsFinite() || !float.IsFinite(AngleRadians) || !float.IsFinite(Speed) || Speed < 0
            || !float.IsFinite(LifetimeSeconds) || LifetimeSeconds <= 0 || Damage <= 0
            || !float.IsFinite(Radius) || Radius <= 0 || Hframes <= 0 || Vframes <= 0
            || (long)Hframes * Vframes > int.MaxValue || ColorIndex < 0 || ColorIndex >= (long)Hframes * Vframes
            || !double.IsFinite(IntervalSeconds) || IntervalSeconds <= 0
            || !float.IsFinite(VisualScale) || VisualScale <= 0 || !Enum.IsDefined(Team) || Behavior is null
            || !ValidColorComponent(CircleColor.R) || !ValidColorComponent(CircleColor.G)
            || !ValidColorComponent(CircleColor.B) || !ValidColorComponent(CircleColor.A))
            throw new ArgumentOutOfRangeException(nameof(BulletSpawnData), "子弹参数无效。");
        if (UseSprite && (string.IsNullOrWhiteSpace(TexturePath) || !ResourceLoader.Exists(TexturePath, "Texture2D")
            || ResourceLoader.Load(TexturePath) is not Texture2D))
            throw new ArgumentException("贴图模式需要有效Texture2D资源。", nameof(TexturePath));
    }
    /// <summary>检查颜色分量是否有限且位于标准范围。</summary>
    /// <param name="value">待检查的RGBA分量，范围应为0～1。</param>
    /// <returns>是否为有效颜色分量。</returns>
    private static bool ValidColorComponent(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
}
