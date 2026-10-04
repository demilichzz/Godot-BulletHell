using Godot;
using System;

/// <summary>保存从JSON读取的单个Boss静态配置；贴图仍使用Godot资源，配置本身不参与资源序列化。</summary>
public partial class BossData
{
    /// <summary>目录内唯一标识，默认对应现有环形 Boss。</summary>
    public string Id { get; set; } = "Boss_01";
    /// <summary>选择界面和战斗中显示的名称。</summary>
    public string DisplayName { get; set; } = "环形守卫";
    /// <summary>战斗贴图，使用带真实透明通道的 PNG。</summary>
    public Texture2D? Texture { get; set; }
    /// <summary>图集列数，正整数，默认1；帧按从左到右、从上到下排列。</summary>
    public int Hframes { get; set; } = 1;
    /// <summary>图集行数，正整数，默认1。</summary>
    public int Vframes { get; set; } = 1;
    /// <summary>播放速度，单位为帧/秒，默认0表示静态；多帧时必须为正数有限值。</summary>
    public double AnimationFps { get; set; }
    /// <summary>选择界面图片，未设置时使用战斗图集第一帧。</summary>
    public Texture2D? Portrait { get; set; }
    /// <summary>最大生命点数，必须大于零，默认300。</summary>
    public int MaxHp { get; set; } = BattleConfig.BossHp;
    /// <summary>判定半径，单位为正数有限逻辑像素，默认32。</summary>
    public float CollisionRadius { get; set; } = BattleConfig.BossRadius;
    /// <summary>正数有限贴图倍率，默认3，不改变判定半径。</summary>
    public float VisualScale { get; set; } = BattleConfig.BossScale;
    /// <summary>出生位置，单位为逻辑像素，右和下为正，默认(640,250)。</summary>
    public Vector2 SpawnPosition { get; set; } = BattleConfig.BossSpawn;
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
