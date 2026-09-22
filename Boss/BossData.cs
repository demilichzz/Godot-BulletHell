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
    /// <summary>图集列数，正整数，默认1；帧按从左到右、从上到下排列。</summary>
    [Export] public int Hframes { get; set; } = 1;
    /// <summary>图集行数，正整数，默认1。</summary>
    [Export] public int Vframes { get; set; } = 1;
    /// <summary>播放速度，单位为帧/秒，默认0表示静态；多帧时必须为正数有限值。</summary>
    [Export] public double AnimationFps { get; set; }
    /// <summary>选择界面图片，未设置时使用战斗图集第一帧。</summary>
    [Export] public Texture2D? Portrait { get; set; }
    /// <summary>最大生命点数，必须大于零，默认300。</summary>
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
        if (Hframes <= 0 || Vframes <= 0 || (long)Hframes * Vframes > int.MaxValue
            || Texture.GetWidth() <= 0 || Texture.GetHeight() <= 0
            || Texture.GetWidth() % Hframes != 0 || Texture.GetHeight() % Vframes != 0
            || !double.IsFinite(AnimationFps) || AnimationFps < 0
            || ((long)Hframes * Vframes > 1 && (AnimationFps <= 0
                || !double.IsFinite((double)Hframes * Vframes / AnimationFps))))
            throw new ArgumentException($"Boss 动画配置无效：{Id}");
    }
    /// <summary>获取静态选择图片，独立肖像优先，否则裁切图集首帧。</summary>
    /// <returns>独立肖像、单帧贴图或首帧图集区域。</returns>
    public Texture2D GetSelectionTexture()
    {
        Validate();
        if (Portrait is not null) return Portrait;
        if (Hframes == 1 && Vframes == 1) return Texture!;
        return new AtlasTexture
        {
            Atlas = Texture,
            Region = new Rect2(0, 0, Texture!.GetWidth() / Hframes, Texture.GetHeight() / Vframes)
        };
    }
}
