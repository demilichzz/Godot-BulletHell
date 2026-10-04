using static JsonData;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>Creator配置的严格读取、属性校验和树身份初始化，与批次运行逻辑分开维护。</summary>
public partial class VNodeCreator
{
    /// <summary>从资源路径加载节点队列数据，不消耗随机。</summary>
    /// <param name="path">res://JSON资源路径。</param>
    /// <returns>已校验的节点队列模板。</returns>
    public static VNodeCreator Load(string path) => FromJson(ReadFile(path), path);

    /// <summary>按Core.Type从JSON加载独立Creator树。</summary>
    /// <param name="json">标准JSON文本。</param>
    /// <param name="sourceName">错误来源名称，默认内存。</param>
    /// <returns>未生成的配置模板。</returns>
    public static VNodeCreator FromJson(string json, string sourceName = "内存")
        => Parse(json, sourceName, element =>
        {
            var queue = ReadCreator(element);
            queue.InitializeTree();
            return queue;
        });

    /// <summary>读取一份队列JSON对象，直接构造对应属性对象。</summary>
    /// <param name="element">队列JSON根对象。</param>
    /// <param name="path">展开后的Creator错误路径。</param>
    /// <returns>已完成公共及专用校验的模板。</returns>
    private static VNodeCreator ReadExpandedCreator(JsonElement element, string path)
    {
        // 类型必须由数据显式指定，ID只由树初始化过程生成。
        try
        {
            JsonElement core = Required(element, "Core");
            string type = Required(core, "Type").GetString() ?? "";
            if (type is not ("VNode" or "VBullet" or "VPath" or "VLaser")) throw new JsonException("Core.Type只支持VNode、VBullet、VPath或VLaser。");
            if (core.TryGetProperty("Id", out _)) throw new JsonException("Core.Id不属于JSON字段。");
            bool bullet = type == "VBullet";
            CheckFields(element, type == "VLaser"
                ? new[] { "Core", "Laser", "Display", "PathQueue", "BaseAttributes", "AddAttributes", "RandDiffAttributes", "Timeline", "MemberTimeline", "Children" }
                : type == "VPath"
                ? new[] { "Core", "PathQueue", "BaseAttributes", "AddAttributes", "RandDiffAttributes", "Timeline", "MemberTimeline", "Children" }
                : bullet
                ? new[] { "Core", "Display", "BaseAttributes", "AddAttributes", "RandDiffAttributes", "Timeline", "MemberTimeline", "Children" }
                : new[] { "Core", "BaseAttributes", "AddAttributes", "RandDiffAttributes", "Timeline", "MemberTimeline", "Children" });
            VNodeCreator queue = type == "VLaser" ? new VLaserCreator() : type == "VPath" ? new VPathCreator() : bullet ? new VBulletCreator() : new VNodeCreator();
            queue.Core = type == "VLaser" ? Read<VLaserCoreAttribute>(core) : bullet ? Read<VBulletCoreAttribute>(core) : Read<VNodeCoreAttribute>(core);
            // 仅路径允许省略基础项，其他类型保留原有必填约束。
            if (type != "VPath" || element.TryGetProperty("BaseAttributes", out _))
            {
                JsonElement baseValues = Required(element, "BaseAttributes");
                if (baseValues.ValueKind != JsonValueKind.Array || baseValues.GetArrayLength() == 0)
                    throw new JsonException("BaseAttributes必须为非空数组。");
                queue.BaseAttributes = Array.AsReadOnly(baseValues.EnumerateArray().Select(value =>
                {
                    var spawn = ReadSpawn(value, false);
                    return bullet && !value.TryGetProperty("Speed", out _) ? spawn with { Speed = 180 } : spawn;
                }).ToArray());
            }
            queue.AddAttributes = element.TryGetProperty("AddAttributes", out var add) ? ReadSpawn(add, false) : new();
            if (element.TryGetProperty("RandDiffAttributes", out var random))
            {
                CheckFields(random, new[] { "Batch", "Member" });
                queue.RandDiffAttributes = new VNodeRandDiffAttribute
                {
                    Batch = random.TryGetProperty("Batch", out var batch) ? ReadSpawn(batch, true) : new(),
                    Member = random.TryGetProperty("Member", out var member) ? ReadSpawn(member, true) : new()
                };
            }
            queue.Timeline = ReadTimes(element, "Timeline");
            queue.MemberTimeline = ReadTimes(element, "MemberTimeline");
            if (queue is VBulletCreator bullets)
            {
                bullets.ReadDisplay(Required(element, "Display"));
                bullets.ReadRegions(core);
            }
            if (queue is VPathCreator pathCreator) pathCreator.ReadPathQueue(Required(element, "PathQueue"));
            if (queue is VLaserCreator laser) laser.ReadLaser(element);
            queue.ValidateAttributes();
            if (element.TryGetProperty("Children", out var children))
            {
                if (children.ValueKind != JsonValueKind.Array) throw new JsonException("Children必须为数组。");
                queue.Children = Array.AsReadOnly(children.EnumerateArray().Select((child, index) => ReadExpandedCreator(child, $"{path}.Children[{index}]")).ToArray());
            }
            return queue;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
        {
            throw new JsonException($"{path}: {error.Message}", error);
        }
    }

    /// <summary>建立稳定路径ID并验证名称和父索引时间边界。</summary>
    /// <returns>按Id和Name查找的独立索引。</returns>
    internal (Dictionary<string, VNodeCreator> Ids, Dictionary<string, VNodeCreator> Names) InitializeTree()
    {
        // 两类标识分别检查唯一性，允许跨类别重名。
        var ids = new Dictionary<string, VNodeCreator>(StringComparer.Ordinal);
        var names = new Dictionary<string, VNodeCreator>(StringComparer.Ordinal);
        // 按声明顺序分配路径；父Amount决定子规则的索引边界。
        void Visit(VNodeCreator creator, string id, int parentAmount)
        {
            creator.Core.Id = id;
            ids.Add(id, creator);
            if (!string.IsNullOrWhiteSpace(creator.Core.Name) && !names.TryAdd(creator.Core.Name, creator))
                throw new JsonException("重复Creator名称：" + creator.Core.Name);
            creator.ValidateSchedules(parentAmount);
            if (creator.Children.Count > 999) throw new JsonException(id + ": Children不能超过999项。");
            for (int index = 0; index < creator.Children.Count; index++)
                Visit(creator.Children[index], id + (index + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture), creator.TotalAmount);
        }
        Visit(this, "VN001", 1);
        return (ids, names);
    }

    /// <summary>校验公共参数并冻结位移动作列表。</summary>
    protected virtual void ValidateAttributes()
    {
        if (Core.Amount <= 0) throw new JsonException("Core.Amount必须为正整数。");
        if (Core.AngleMode is not ("Fixed" or "AimPlayer")) throw new JsonException("Core.AngleMode只支持Fixed或AimPlayer。");
        if (Core.CreatePositionMode is not (null or "Follow" or "Snapshot"))
            throw new JsonException("Core.CreatePositionMode必须为Follow或Snapshot。");
        _ = TotalAmount;
        foreach (var basis in BaseAttributes)
        {
            VNode.ValidateMotion(Vector2.Zero, basis, Core.LifeTimeMs);
            // 最大轮次延迟必须能用内部整数时间表示。
            _ = checked((basis.SpawnDelayMs + (Core.Amount - 1L) * AddAttributes.SpawnDelayMs) * VTimerProcessor.UnitsPerMillisecond);
            foreach (var attributes in new[] { AddAttributes, RandDiffAttributes.Batch, RandDiffAttributes.Member })
                if (attributes.RefMoveQueue.Count != 0
                    && (attributes.RefMoveQueue.Count != basis.RefMoveQueue.Count
                    || !attributes.RefMoveQueue.Select(action => action.Type).SequenceEqual(basis.RefMoveQueue.Select(action => action.Type))))
                    throw new JsonException("增量和随机位移动作必须匹配所有基础项的数量及Type。");
        }
        foreach (var rule in MemberTimeline) rule.Validate(TotalAmount, true);
    }

    /// <summary>根据父队列数量验证生成规则及重复节点寿命。</summary>
    /// <param name="parentAmount">父成员索引数量；Emitter为1。</param>
    internal void ValidateSchedules(int parentAmount)
    {
        foreach (var rule in Timeline) rule.Validate(parentAmount, false);
        if (this is not VBulletCreator && Core.LifeTimeMs is null
            && (Timeline.Count > 1 || Timeline.Any(rule => rule.Repeats(0) || rule.Repeats(parentAmount - 1))))
            throw new JsonException($"{Core.Id}: 重复生成VNode必须配置有限LifeTimeMs。");
    }

    /// <summary>读取时间规则并冻结时刻数组。</summary>
    /// <param name="element">包含时间规则的队列。</param>
    /// <param name="name">Timeline或MemberTimeline。</param>
    /// <returns>只读规则数组，省略时为空。</returns>
    private static IReadOnlyList<TimelineAttribute> ReadTimes(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var times)) return Array.Empty<TimelineAttribute>();
        var values = Read<TimelineAttribute[]>(times);
        if (values.Any(value => value is null)) throw new JsonException(name + "不能包含null。");
        return Array.AsReadOnly(values.Select(value => value.Freeze()).ToArray());
    }

    /// <summary>解析、校验并冻结一个出生、增量或随机属性组。</summary>
    /// <param name="element">当前格式JSON对象。</param>
    /// <param name="random">随机宽度必须非负，且不能包含时间。</param>
    /// <returns>只读属性组。</returns>
    private static VNodeSpawnAttribute ReadSpawn(JsonElement element, bool random)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("出生属性必须为对象。");
        if (random && element.TryGetProperty("SpawnDelayMs", out _)) throw new JsonException("随机组不支持SpawnDelayMs。");
        var spawn = Read<VNodeSpawnAttribute>(element);
        if (spawn.SpawnDelayMs < 0 || spawn.RefMoveQueue is null) throw new JsonException("延迟须非负，位移动作不可为null。");
        foreach (double value in new[] { spawn.Angle, spawn.Speed, spawn.AAngle, spawn.ASpeed })
            VMoveJson.ValidateNumber(value, random);
        return spawn with
        {
            RefMoveQueue = element.TryGetProperty("RefMoveQueue", out var actions)
                ? VMoveJson.ReadQueue(actions, random) : Array.Empty<VNodeMoveActionAttribute>()
        };
    }

}
