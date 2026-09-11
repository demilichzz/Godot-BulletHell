using Godot;
using System;

/// <summary>保存单颗子弹的完整初始化参数，位置使用全局逻辑像素。</summary>
public sealed record BulletSpawnData
{
    /// <summary>全局起点，逻辑像素，右和下为正，默认原点。</summary>
    public Vector2 Position { get; init; }
    /// <summary>发射角度，单位为度，0向右、90向下，默认0。</summary>
    public float AngleDegrees { get; init; }
    /// <summary>非负有限速度，逻辑像素/秒，默认敌弹速度。</summary>
    public float Speed { get; init; } = BattleConfig.EnemySpeed;
    /// <summary>正数有限寿命，单位为秒，默认敌弹寿命。</summary>
    public float LifetimeSeconds { get; init; } = BattleConfig.EnemyLifetime;
    /// <summary>所属阵营，默认敌方。</summary>
    public BulletTeam Team { get; init; } = BulletTeam.Enemy;
    /// <summary>正整数伤害点数，默认1。</summary>
    public int Damage { get; init; } = BattleConfig.Damage;
    /// <summary>正数有限碰撞半径，逻辑像素，默认敌弹半径。</summary>
    public float Radius { get; init; } = BattleConfig.EnemyBulletRadius;
    /// <summary>图集从左至右索引0～9，默认敌弹颜色。</summary>
    public int ColorIndex { get; init; } = BattleConfig.EnemyColor;
    /// <summary>正数有限显示倍率，默认敌弹倍率，不改变碰撞范围。</summary>
    public float VisualScale { get; init; } = BattleConfig.EnemyScale;
    /// <summary>是否使用图集外观，默认开启；关闭时绘制玩家圆点。</summary>
    public bool UseSprite { get; init; } = true;
    /// <summary>生成后的运动行为，默认直线；有状态行为须每颗独立创建。</summary>
    public BulletBehavior Behavior { get; init; } = new StraightBehavior();
    /// <summary>创建采用当前阵营默认参数的配置。</summary>
    /// <param name="team">子弹所属阵营。</param>
    /// <param name="angle">角度，单位为度，0向右、90向下，默认0。</param>
    /// <returns>可通过 with 调整的参数快照。</returns>
    public static BulletSpawnData ForTeam(BulletTeam team, float angle = 0) => new()
    {
        Team = team, AngleDegrees = angle,
        Speed = team == BulletTeam.Enemy ? BattleConfig.EnemySpeed : BattleConfig.ShotSpeed,
        LifetimeSeconds = team == BulletTeam.Enemy ? BattleConfig.EnemyLifetime : BattleConfig.ShotLifetime,
        Radius = team == BulletTeam.Enemy ? BattleConfig.EnemyBulletRadius : BattleConfig.PlayerBulletRadius,
        UseSprite = team == BulletTeam.Enemy
    };
    /// <summary>在创建节点前检查数值范围，避免产生无效节点。</summary>
    public void Validate()
    {
        if (!Position.IsFinite() || !float.IsFinite(AngleDegrees) || !float.IsFinite(Speed) || Speed < 0
            || !float.IsFinite(LifetimeSeconds) || LifetimeSeconds <= 0 || Damage <= 0
            || !float.IsFinite(Radius) || Radius <= 0 || ColorIndex < 0 || ColorIndex > 9
            || !float.IsFinite(VisualScale) || VisualScale <= 0 || !Enum.IsDefined(Team) || Behavior is null)
            throw new ArgumentOutOfRangeException(nameof(BulletSpawnData), "子弹参数无效。");
    }
}
