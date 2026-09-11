using Godot;
using System;

/// <summary>保存单个 Boss 的静态配置，可在编辑器中创建独立资源。</summary>
[GlobalClass]
public partial class BossData : Resource
{
    /// <summary>目录内唯一标识，默认对应现有环形 Boss。</summary>
    [Export] public string Id { get; set; } = "Boss_01";
    /// <summary>选择界面和战斗中显示的名称。</summary>
    [Export] public string DisplayName { get; set; } = "环形守卫";
    /// <summary>战斗贴图，使用带真实透明通道的 PNG。</summary>
    [Export] public Texture2D? Texture { get; set; }
    /// <summary>选择界面图片，未设置时使用战斗贴图。</summary>
    [Export] public Texture2D? Portrait { get; set; }
    /// <summary>最大生命点数，必须大于零，默认100。</summary>
    [Export] public int MaxHp { get; set; } = BattleConfig.BossHp;
    /// <summary>判定半径，单位为正数有限逻辑像素，默认32。</summary>
    [Export] public float CollisionRadius { get; set; } = BattleConfig.BossRadius;
    /// <summary>正数有限贴图倍率，默认3，不改变判定半径。</summary>
    [Export] public float VisualScale { get; set; } = BattleConfig.BossScale;
    /// <summary>出生位置，单位为逻辑像素，右和下为正，默认(640,250)。</summary>
    [Export] public Vector2 SpawnPosition { get; set; } = BattleConfig.BossSpawn;
    /// <summary>阶段组合注册键，默认Boss_01。</summary>
    [Export] public string PhaseProfile { get; set; } = "Boss_01";
    /// <summary>检查配置是否能用于生成 Boss。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName) || Texture is null
            || MaxHp <= 0 || !float.IsFinite(CollisionRadius) || CollisionRadius <= 0
            || !float.IsFinite(VisualScale) || VisualScale <= 0 || !SpawnPosition.IsFinite())
            throw new ArgumentException($"Boss 配置无效：{Id}");
    }
}
