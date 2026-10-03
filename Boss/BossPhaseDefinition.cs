using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>独立阶段的静态定义，血量为独立血池，时间统一使用整数毫秒。</summary>
public sealed record BossPhaseDefinition
{
    /// <summary>阶段显示名称。</summary>
    public required string Name { get; init; }
    /// <summary>阶段最大生命，正整数点数；超额伤害不跨阶段。</summary>
    public required int Hp { get; init; }
    /// <summary>阶段时限，正整数毫秒；仅血量阶段允许null表示无时限。</summary>
    public required int? DurationMs { get; init; }
    /// <summary>结束条件：Health、Time、HealthOrTime；后者任一条件满足即可。</summary>
    public required string EndCondition { get; init; }
    /// <summary>按声明顺序绑定的独立Emitter资源路径，允许空队列。</summary>
    public required IReadOnlyList<string> Emitters { get; init; }
    /// <summary>阶段移动定义；位置为战场逻辑像素。</summary>
    public required JsonElement Movement { get; init; }

    /// <summary>读取并检查阶段；验证发射器不启动战斗或消耗随机。</summary>
    /// <param name="element">完整阶段JSON对象。</param>
    /// <returns>带独立移动JSON和只读路径队列的定义。</returns>
    internal static BossPhaseDefinition Read(JsonElement element)
    {
        // 序列化器拒绝未知字段，公共解析入口拒绝重复字段。
        var value = VNodeCreator.Read<BossPhaseDefinition>(element);
        if (string.IsNullOrWhiteSpace(value.Name) || value.Hp <= 0
            || value.EndCondition is not ("Health" or "Time" or "HealthOrTime")
            || value.DurationMs is <= 0 || (value.EndCondition != "Health" && value.DurationMs is null)
            || value.Emitters is null)
            throw new JsonException("阶段名称、血量、时限或切换条件无效。");
        // 加载检查全部引用；每次激活仍创建全新的Emitter树。
        foreach (string path in value.Emitters)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://", StringComparison.Ordinal)
                || !path.EndsWith(".json", StringComparison.Ordinal))
                throw new JsonException("Emitter必须引用res://下的独立JSON文件。");
            VBulletEmitter.Load(path);
        }
        BossMovement.Read(value.Movement);
        return value with
        {
            Movement = value.Movement.Clone(),
            Emitters = Array.AsReadOnly(value.Emitters.ToArray())
        };
    }
}

/// <summary>加载阶段移动配置；固定时间步执行，所有随机仅由VMath抽样。</summary>
public sealed record BossMovement
{
    /// <summary>Center、RandomCircle、RandomRect、Sequence或Path。</summary>
    public required string Type { get; init; }
    /// <summary>移动速度，正数逻辑像素/秒，默认200。</summary>
    public float Speed { get; init; } = 200;
    /// <summary>中心或入场目标；省略时随机移动先留在出生点。</summary>
    public VPathPointAttribute? Target { get; init; }
    /// <summary>圆形随机范围的中心，逻辑像素。</summary>
    public VPathPointAttribute? Center { get; init; }
    /// <summary>圆形随机的半径下限，非负逻辑像素，默认0。</summary>
    public double MinRadius { get; init; }
    /// <summary>圆形随机的半径上限，非负逻辑像素。</summary>
    public double MaxRadius { get; init; }
    /// <summary>矩形随机范围的左上角，逻辑像素。</summary>
    public VPathPointAttribute? Min { get; init; }
    /// <summary>矩形随机范围的右下角，逻辑像素。</summary>
    public VPathPointAttribute? Max { get; init; }
    /// <summary>首次选点时刻，非负整数毫秒，默认0。</summary>
    public int StartMs { get; init; }
    /// <summary>随机或固定目标的切换周期，正整数毫秒。</summary>
    public int IntervalMs { get; init; }
    /// <summary>循环固定目标队列，使用战场局部逻辑像素。</summary>
    public IReadOnlyList<VPathPointAttribute>? Targets { get; init; }
    /// <summary>复用VPath的路径段定义，初次入场冻结空间和瞄准目标。</summary>
    public JsonElement PathQueue { get; init; }
    /// <summary>VPath几何采样数，默认1025，范围2至65536且多于段数。</summary>
    public int PointCount { get; init; } = 1025;
    /// <summary>是否循环闭合路径；默认否，到终点停止。</summary>
    public bool Loop { get; init; }

    /// <summary>读取并按模式拒绝无意义字段。</summary>
    /// <param name="element">阶段Movement对象。</param>
    /// <returns>经过模式、范围和路径语法校验的配置。</returns>
    internal static BossMovement Read(JsonElement element)
    {
        // 先检查类型，再限定该模式可以使用的字段。
        string type = VNodeCreator.Required(element, "Type").GetString() ?? "";
        string[] extra = type switch
        {
            "Center" => new[] { "Target" },
            "RandomCircle" => new[] { "Target", "Center", "MinRadius", "MaxRadius", "StartMs", "IntervalMs" },
            "RandomRect" => new[] { "Target", "Min", "Max", "StartMs", "IntervalMs" },
            "Sequence" => new[] { "Target", "Targets", "StartMs", "IntervalMs" },
            "Path" => new[] { "PathQueue", "PointCount", "Loop" },
            _ => throw new JsonException("未知Boss移动类型。")
        };
        VNodeCreator.CheckFields(element, new[] { "Type", "Speed" }.Concat(extra).ToArray());
        // 配置位置必须同时声明X/Y，避免拼漏坐标静默落到原点。
        foreach (string key in new[] { "Target", "Center", "Min", "Max" })
            if (element.TryGetProperty(key, out var point)) ReadPoint(point);
        if (element.TryGetProperty("Targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
            foreach (var point in targets.EnumerateArray()) ReadPoint(point);
        var value = VNodeCreator.Read<BossMovement>(element);
        if (!float.IsFinite(value.Speed) || value.Speed <= 0 || value.StartMs < 0)
            throw new JsonException("移动速度必须为正有限数，StartMs不能为负。");
        if (value.Target is not null) Point(value.Target);
        if (type is "RandomCircle" or "RandomRect" or "Sequence" && value.IntervalMs <= 0)
            throw new JsonException("选点IntervalMs必须为正整数。");
        if (type == "RandomCircle")
        {
            Point(value.Center);
            if (!double.IsFinite(value.MinRadius) || !double.IsFinite(value.MaxRadius)
                || value.MinRadius < 0 || value.MaxRadius < value.MinRadius || value.MaxRadius > float.MaxValue)
                throw new JsonException("随机半径范围无效。");
        }
        if (type == "RandomRect")
        {
            // 包括零宽度或零高度范围，零宽度不消耗随机。
            Vector2 min = Point(value.Min), max = Point(value.Max);
            if (min.X > max.X || min.Y > max.Y) throw new JsonException("随机矩形边界倒置。");
        }
        if (type == "Sequence")
        {
            if (value.Targets is null || value.Targets.Count == 0) throw new JsonException("Targets不能为空。");
            foreach (var target in value.Targets) Point(target);
        }
        if (type == "Path")
        {
            if (value.PointCount < 2 || value.PointCount > 65536) throw new JsonException("PointCount须为2至65536。");
            VPathCreator.CreateGeometry(value.PathQueue, value.PointCount);
        }
        return value;
    }

    /// <summary>严格读取包含X与Y的配置点。</summary>
    /// <param name="element">仅包含X/Y的JSON位置对象。</param>
    /// <returns>有限逻辑像素位置。</returns>
    internal static Vector2 ReadPoint(JsonElement element)
    {
        VNodeCreator.CheckFields(element, new[] { "X", "Y" });
        VNodeCreator.Required(element, "X");
        VNodeCreator.Required(element, "Y");
        return Point(VNodeCreator.Read<VPathPointAttribute>(element));
    }
    /// <summary>检查并转换双精度配置点。</summary>
    /// <param name="point">右下为正的必填像素坐标。</param>
    /// <returns>有限Vector2坐标。</returns>
    internal static Vector2 Point(VPathPointAttribute? point)
    {
        if (point is null) throw new JsonException("缺少移动坐标。");
        VMath.ValidatePathPoint((point.X, point.Y));
        return new Vector2((float)point.X, (float)point.Y);
    }
}
