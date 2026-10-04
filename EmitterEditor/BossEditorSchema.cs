using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Boss编辑模式的字段、移动模板和显示名称，复用现有VPath属性目录。</summary>
public static class BossEditorSchema
{
    // Boss专有中文名称；基础路径字段沿用Emitter编辑器的名称。
    private static readonly Dictionary<string, string> Names = new()
    {
        ["Id"] = "Boss标识", ["DisplayName"] = "显示名称", ["TexturePath"] = "战斗图集路径",
        ["PortraitPath"] = "肖像路径（可空）", ["Hframes"] = "图集列数", ["Vframes"] = "图集行数",
        ["AnimationFps"] = "动画帧/秒", ["MaxHp"] = "Boss总血量", ["Hp"] = "阶段独立血量",
        ["CollisionRadius"] = "碰撞半径（像素）", ["SpawnPosition"] = "出生坐标",
        ["DurationMs"] = "持续时间（毫秒）", ["EndCondition"] = "切换条件", ["Emitters"] = "Emitter引用",
        ["Movement"] = "Boss移动", ["Center"] = "随机圆心", ["Target"] = "入场目标",
        ["Min"] = "左上边界", ["Max"] = "右下边界", ["MinRadius"] = "最小半径（像素）",
        ["MaxRadius"] = "最大半径（像素）", ["Targets"] = "固定目标队列",
        ["PointCount"] = "路径采样数", ["Loop"] = "循环闭合路径"
    };
    /// <summary>取得中文字段名称并保留JSON键。</summary>
    /// <param name="key">JSON键。</param>
    /// <returns>中文名称与原键。</returns>
    public static string Label(string key) => key == "Emitters项" ? "文件路径"
        : key.EndsWith("项", StringComparison.Ordinal) ? Label(key[..^1]) + "项"
        : Names.TryGetValue(key, out string? name) ? $"{name}（{key}）" : EditorSchema.DisplayName(key);
    /// <summary>创建独立阶段模板，默认60秒内血量或时间任一满足结束。</summary>
    /// <returns>可运行的新阶段。</returns>
    public static JsonObject Phase() => JsonNode.Parse("""
        {"Name":"新阶段","Hp":100,"DurationMs":60000,"EndCondition":"HealthOrTime","Emitters":[],
         "Movement":{"Type":"Center","Speed":200,"Target":{"X":640,"Y":240}}}
        """)!.AsObject();
    /// <summary>创建模式对应的完整移动模板。</summary>
    /// <param name="type">已支持的移动类型。</param>
    /// <returns>只包含本模式字段的JSON对象。</returns>
    public static JsonObject Movement(string type) => JsonNode.Parse(type switch
    {
        "RandomCircle" => """{"Type":"RandomCircle","Speed":100,"Center":{"X":640,"Y":250},"MinRadius":200,"MaxRadius":200,"StartMs":5000,"IntervalMs":5000}""",
        "RandomRect" => """{"Type":"RandomRect","Speed":100,"Min":{"X":440,"Y":160},"Max":{"X":840,"Y":350},"StartMs":3000,"IntervalMs":3000}""",
        "Sequence" => """{"Type":"Sequence","Speed":200,"Target":{"X":640,"Y":240},"StartMs":3000,"IntervalMs":3000,"Targets":[{"X":500,"Y":230},{"X":780,"Y":230}]}""",
        "Path" => """{"Type":"Path","Speed":100,"PointCount":1025,"Loop":false,"PathQueue":[{"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":200,"Y":0}]}]}""",
        _ => """{"Type":"Center","Speed":200,"Target":{"X":640,"Y":240}}"""
    })!.AsObject();
    /// <summary>按运行类型和模式筛选可编辑字段。</summary>
    /// <param name="type">Boss或复用的VPath属性类型。</param>
    /// <param name="value">当前属性对象。</param>
    /// <param name="context">字段名称。</param>
    /// <returns>包含缺省值的字段列表。</returns>
    public static List<EditorSchema.Field> Fields(Type type, JsonObject value, string context)
    {
        // 复用反射目录，JsonElement字段在UI中还原为实际对象或数组。
        var fields = EditorSchema.Fields(type, value, "", context);
        for (int index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            if (field.Name == "Movement") fields[index] = field with { ValueType = typeof(BossMovement), Default = Movement("Center") };
            if (field.Name == "PathQueue") fields[index] = field with { ValueType = typeof(VPathSegmentAttribute[]), Default = Movement("Path")["PathQueue"]!.DeepClone() };
            if (field.ValueType == typeof(VPathPointAttribute)) fields[index] = field with { Default = JsonNode.Parse("""{"X":640,"Y":240}""") };
        }
        if (type == typeof(BossMovement))
        {
            // 随机和序列允许省略入场目标；路径不显示无关参数。
            var allowed = Movement(value["Type"]?.ToString() ?? "Center").Select(pair => pair.Key).ToHashSet();
            if (value["Type"]?.ToString() is "RandomCircle" or "RandomRect") allowed.Add("Target");
            fields.RemoveAll(field => !allowed.Contains(field.Name));
        }
        if (type == typeof(VPathSegmentAttribute))
        {
            if (value["Type"]?.ToString() == "AimPlayer")
                return fields.Where(field => field.Name is "Type" or "X" or "Y").ToList();
            // 模式切换自动提供对应曲线参数，保留端点位移队列。
            string mode = value["PathMode"]?.ToString() ?? "XY";
            fields.RemoveAll(field => field.Name == "Type" || field.Name == "AimPlayerOffset"
                || (mode != "Bezier" && field.Name == "ControlPoints")
                || (mode != "Function" && field.Name is "AxisMode" or "X" or "Y" or "TMin" or "TMax")
                || (mode == "XY" && field.Name == "Samples"));
        }
        return fields;
    }
    /// <summary>调整阶段后同步总血量，作为同一撤销事务的一部分。</summary>
    /// <param name="root">完整Boss文档。</param>
    public static void SumHealth(JsonObject root)
    {
        // 允许暂存非法输入供修复；正式校验阻止其保存，不静默恢复旧血量。
        long sum = 0;
        try
        {
            foreach (var phase in root["Phases"]!.AsArray())
            {
                int hp = phase!["Hp"]!.Deserialize<int>();
                if (hp <= 0) return;
                sum += hp;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or NullReferenceException) { return; }
        if (sum <= int.MaxValue) root["Core"]!["MaxHp"] = (int)sum;
    }
}
