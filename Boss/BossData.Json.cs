using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>读取目录内嵌Boss属性和有序阶段，不嵌入Emitter数据。</summary>
public partial class BossData
{
    /// <summary>数据化阶段队列；空队列仅用于显式代码阶段配置。</summary>
    public IReadOnlyList<BossPhaseDefinition> Phases { get; private set; } = Array.Empty<BossPhaseDefinition>();
    /// <summary>阶段数由队列长度决定，避免重复保存不一致的数量。</summary>
    public int PhaseCount => Phases.Count;
    /// <summary>本份数据创建独立Emitter的入口；编辑预览可绑定冻结的内存文本。</summary>
    internal Func<string, VBulletEmitter> EmitterLoader { get; private set; } = VBulletEmitter.Load;
    /// <summary>解析完整Boss JSON，严格拒绝未知、重复及旧字段。</summary>
    /// <param name="json">含Core和非空Phases队列的JSON。</param>
    /// <param name="sourceName">用于错误定位的来源名称。</param>
    /// <returns>独立静态数据；加载不推进战斗或消耗随机。</returns>
    /// <param name="loadEmitter">可选预览资源入口；每次返回全新的运行树。</param>
    public static BossData FromJson(string json, string sourceName = "内存Boss", Func<string, VBulletEmitter>? loadEmitter = null)
        => JsonData.Parse(json, sourceName, element => Read(element, loadEmitter));
    /// <summary>读取目录内的Boss对象，供目录和内存预览共用。</summary>
    /// <param name="element">包含Core与非空Phases的对象。</param>
    /// <returns>独立的Boss静态配置。</returns>
    /// <param name="loadEmitter">可选Emitter创建入口。</param>
    internal static BossData Read(JsonElement element, Func<string, VBulletEmitter>? loadEmitter = null)
    {
        JsonData.CheckFields(element, new[] { "Core", "Phases" });
        // Core只保存显示、碰撞、出生和总血量。
        var core = JsonData.Read<BossCoreDefinition>(JsonData.Required(element, "Core"));
        var queue = JsonData.Required(element, "Phases");
        if (queue.ValueKind != JsonValueKind.Array || queue.GetArrayLength() == 0)
            throw new JsonException("Phases必须为非空有序阶段队列。");
        var phases = new List<BossPhaseDefinition>();
        foreach (var phase in queue.EnumerateArray())
        {
            try { phases.Add(BossPhaseDefinition.Read(phase, loadEmitter)); }
            catch (Exception error) when (error is JsonException or ArgumentException or System.IO.IOException)
            { throw new JsonException($"Phases[{phases.Count}]: {error.Message}", error); }
        }
        if (phases.Sum(phase => (long)phase.Hp) != core.MaxHp)
            throw new JsonException("Core.MaxHp必须等于全部独立阶段Hp之和。");
        // 贴图资源仍由Godot缓存管理，配置不持有任何战斗对象。
        var data = new BossData
        {
            Id = core.Id, DisplayName = core.DisplayName,
            Texture = LoadTexture(core.TexturePath), Portrait = core.PortraitPath is null ? null : LoadTexture(core.PortraitPath),
            Hframes = core.Hframes, Vframes = core.Vframes, AnimationFps = core.AnimationFps,
            MaxHp = core.MaxHp, CollisionRadius = core.CollisionRadius, VisualScale = core.VisualScale,
            SpawnPosition = BossMovement.ReadPoint(JsonData.Required(JsonData.Required(element, "Core"), "SpawnPosition")),
            Phases = phases.AsReadOnly(), PhaseProfile = "", EmitterLoader = loadEmitter ?? VBulletEmitter.Load
        };
        data.Validate();
        return data;
    }
    /// <summary>读取有效Godot贴图资源。</summary>
    /// <param name="path">res://贴图路径。</param>
    /// <returns>已加载的贴图。</returns>
    private static Texture2D LoadTexture(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://", StringComparison.Ordinal)
            || !ResourceLoader.Exists(path, "Texture2D"))
            throw new JsonException($"无效Boss贴图路径：{path}");
        return GD.Load<Texture2D>(path) ?? throw new JsonException($"无法加载Boss贴图：{path}");
    }
}

/// <summary>Boss JSON的Core属性；总血量等于各阶段独立血池总和。</summary>
public sealed record BossCoreDefinition
{
    /// <summary>选择界面与Replay使用的稳定标识。</summary>
    public required string Id { get; init; }
    /// <summary>Boss显示名称。</summary>
    public required string DisplayName { get; init; }
    /// <summary>战斗图集res://资源路径。</summary>
    public required string TexturePath { get; init; }
    /// <summary>可选独立肖像路径，null时使用图集首帧。</summary>
    public string? PortraitPath { get; init; }
    /// <summary>图集列数，默认1，必须为正整数。</summary>
    public int Hframes { get; init; } = 1;
    /// <summary>图集行数，默认1，必须为正整数。</summary>
    public int Vframes { get; init; } = 1;
    /// <summary>动画帧/秒，多帧时必须为正有限数。</summary>
    public double AnimationFps { get; init; }
    /// <summary>独立阶段血池之和，正整数生命点数。</summary>
    public required int MaxHp { get; init; }
    /// <summary>碰撞半径，正数有限逻辑像素，默认32。</summary>
    public float CollisionRadius { get; init; } = 32;
    /// <summary>显示倍率，正数有限值，默认3。</summary>
    public float VisualScale { get; init; } = 3;
    /// <summary>出生位置，战场局部像素，右下为正。</summary>
    public required VPathPointAttribute SpawnPosition { get; init; }
}
