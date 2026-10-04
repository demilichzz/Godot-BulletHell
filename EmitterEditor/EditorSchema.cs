using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>编辑器属性目录，复用运行属性类型并补充JSON专属结构。</summary>
public static class EditorSchema
{
    // 仅供界面显示的中文名称；文档键名、字段查找和表达式保持原协议。
    private static readonly Dictionary<string, string> ChineseNames = new(StringComparer.Ordinal)
    {
        ["Core"] = "核心属性",
        ["VNodes"] = "根生成器",
        ["BaseAttributes"] = "基础属性",
        ["AddAttributes"] = "逐轮增量",
        ["RandDiffAttributes"] = "随机偏差",
        ["Timeline"] = "生成时间线",
        ["MemberTimeline"] = "成员时间线",
        ["Children"] = "子生成器",
        ["Display"] = "显示属性",
        ["PathQueue"] = "路径队列",
        ["Laser"] = "激光属性",
        ["Id"] = "内部标识",
        ["Name"] = "名称",
        ["Type"] = "类型",
        ["RefObject"] = "参考对象",
        ["Team"] = "阵营",
        ["Damage"] = "伤害",
        ["StopMode"] = "停止方式",
        ["Amount"] = "生成轮数",
        ["LifeTimeMs"] = "总寿命",
        ["CreatePositionMode"] = "位置参考方式",
        ["AAngleIsSameAsAngle"] = "加速度方向跟随运动角",
        ["AngleMode"] = "出生角度来源",
        ["CopySource"] = "复制来源",
        ["Angle"] = "角度",
        ["Speed"] = "速度",
        ["AAngle"] = "加速度角度",
        ["ASpeed"] = "加速度",
        ["SpawnDelayMs"] = "出生延迟",
        ["RefMoveQueue"] = "出生位移队列",
        ["Dist"] = "距离",
        ["X"] = "横坐标",
        ["Y"] = "纵坐标",
        ["Batch"] = "批次随机",
        ["Member"] = "成员随机",
        ["StartMs"] = "首次时刻",
        ["IntervalMs"] = "重复间隔",
        ["EndMs"] = "结束时刻",
        ["AtMs"] = "指定时刻列表",
        ["StartMsAdd"] = "起始时刻索引增量",
        ["IntervalMsAdd"] = "间隔索引增量",
        ["EndMsAdd"] = "结束时刻索引增量",
        ["Set"] = "参数设置",
        ["AngleSource"] = "角度来源",
        ["OutsideTimeoutMs"] = "连续出界时限",
        ["OutsideRegion"] = "出界判定区域",
        ["Reflectable"] = "启用反射",
        ["ReflectionRegion"] = "反射区域",
        ["Radius"] = "半径",
        ["VisualScale"] = "显示倍率",
        ["BlendMode"] = "混合模式",
        ["TextureName"] = "贴图名称",
        ["TextureIndex"] = "贴图索引",
        ["PathMode"] = "路径模式",
        ["StartMoveQueue"] = "起点位移队列",
        ["EndMoveQueue"] = "终点位移队列",
        ["ControlPoints"] = "控制点",
        ["AxisMode"] = "坐标系模式",
        ["TMin"] = "参数起值",
        ["TMax"] = "参数终值",
        ["Samples"] = "采样区间数",
        ["AimPlayerOffset"] = "瞄准玩家偏移",
        ["Mode"] = "模式",
        ["Length"] = "长度",
        ["Width"] = "宽度",
        ["Height"] = "高度",
        ["HitWidth"] = "碰撞宽度",
        ["EndCap"] = "端点形状",
        ["Color"] = "主体颜色",
        ["CoreColor"] = "亮芯颜色",
        ["GlowColor"] = "外光颜色",
        ["WarningMs"] = "预警时长",
        ["ExpandMs"] = "展开时长",
        ["ActiveMs"] = "生效时长",
        ["FadeMs"] = "消退时长",
        ["TravelSpeed"] = "路径前进速度",
        ["PathPointCount"] = "路径采样点数",
        ["Edges"] = "反射边列表",
        ["AngleMin"] = "起始角度",
        ["AngleMax"] = "终止角度"
    };
    /// <summary>以中文（英文）显示JSON属性名，不改变实际字段键。</summary>
    /// <param name="name">精确JSON字段名。</param>
    /// <returns>用于标签、分组和添加菜单的双语名称。</returns>
    public static string DisplayName(string name) => $"{ChineseNames.GetValueOrDefault(name, "未识别属性")}{(QueueNames.Contains(name) ? "(队列)" : "")}（{name}）";
    // JSON数组字段统一标明队列，元素标题不沿用组名后缀。
    private static readonly HashSet<string> QueueNames = new() { "BaseAttributes", "Timeline", "MemberTimeline", "Children", "PathQueue", "RefMoveQueue", "StartMoveQueue", "EndMoveQueue", "ControlPoints", "AtMs", "Edges" };

    /// <summary>返回队列项的中文标题。</summary>
    /// <param name="name">队列JSON字段名。</param>
    /// <param name="index">零基下标。</param>
    /// <returns>无队列后缀的项目名称。</returns>
    public static string ItemName(string name, int index) => $"{(name == "BaseAttributes" ? "基础项" : name is "Timeline" or "MemberTimeline" ? "时间项" : ChineseNames.GetValueOrDefault(name, "项目"))} [{index}]";

    /// <summary>返回协议限定的字符串选项，自由名称、颜色和表达式返回空列表。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="type">标量声明类型。</param>
    /// <param name="path">用于区分各类Type的JSON路径。</param>
    /// <param name="creatorType">生成器类型。</param>
    /// <returns>JSON原始字符串取值；null表示显式空值。</returns>
    public static string[] Choices(string name, Type type, string path, string creatorType)
    {
        // 枚举声明直接复用类型，字符串白名单与加载器一致。
        if (type.IsEnum) return Enum.GetNames(type);
        return name switch
        {
            "RefObject" => new[] { "Boss", "null" },
            "StopMode" => new[] { "KeepBullets", "ClearBullets" },
            "CreatePositionMode" => new[] { "null", "Follow", "Snapshot" },
            "AngleMode" or "AngleSource" => new[] { "Fixed", "AimPlayer" },
            "BlendMode" => creatorType == "VLaser" ? new[] { "Mix", "Add", "CoreAdd" } : new[] { "Mix", "Add" },
            "TextureName" => new[] { "Scale", "Dot", "Drop", "Star" },
            "Mode" => new[] { "Fixed", "Path" },
            "EndCap" => new[] { "Round", "Point" },
            "PathMode" => ProtocolModes.Path.Values.ToArray(),
            "AxisMode" => ProtocolModes.PathAxes.ToArray(),
            "EndCondition" => ProtocolModes.EndConditions.ToArray(),
            "Edges" => new[] { "Left", "Right", "Top", "Bottom" },
            "Type" when path.Contains("/ReflectionRegion/") || path.Contains("/OutsideRegion/") => new[] { "Circle", "Rectangle" },
            "Type" when path.EndsWith("/Movement/Type", StringComparison.Ordinal) => ProtocolModes.BossMovement.Values.ToArray(),
            "Type" when path.Contains("MoveQueue/") => ProtocolModes.Displacement.Values.ToArray(),
            "Type" when path.Contains("/PathQueue/") => ProtocolModes.PathAim.Values.ToArray(),
            "Type" => new[] { "VNode", "VBullet", "VPath", "VLaser" },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>按协议目录顺序插入属性，未知字段保留在末尾；不调整数组成员顺序。</summary>
    /// <param name="obj">要修改的JSON对象。</param>
    /// <param name="fields">协议字段声明顺序。</param>
    /// <param name="field">新字段。</param>
    public static void InsertField(JsonObject obj, List<Field> fields, Field field)
    {
        obj[field.Name] = field.Default?.DeepClone();
        // 暂存原节点，清空后按协议顺序重新挂接，不改写值或表达式。
        var entries = obj.ToArray(); obj.Clear();
        foreach (var entry in entries.OrderBy(pair => { int index = fields.FindIndex(item => item.Name == pair.Key); return index < 0 ? int.MaxValue : index; }))
            obj.Add(entry.Key, entry.Value);
    }
    // 属性说明来自当前源码summary的资源快照，运行时无需读取C#源码。
    private static readonly Dictionary<string, string> Descriptions = JsonSerializer.Deserialize<Dictionary<string, string>>(
        Godot.FileAccess.GetFileAsString("res://EmitterEditor/FieldDescriptions.json")) ?? new();
    /// <summary>可添加的字段及其编辑类型和默认JSON值。</summary>
    /// <param name="Name">精确JSON字段名。</param>
    /// <param name="ValueType">字段类型；JsonObject表示Creator。</param>
    /// <param name="Default">显式添加时使用的值。</param>
    /// <param name="Tip">简要说明及单位。</param>
    public sealed record Field(string Name, Type ValueType, JsonNode? Default, string Tip);
    // 仅在编辑器主线程访问；缓存不持有文档节点或运行状态。
    private static readonly Dictionary<Type, Field[]> FieldCache = new();
    /// <summary>根据当前对象类型与模式取得可添加字段；已有非法字段由面板保留供修复。</summary>
    /// <param name="type">属性组类型。</param>
    /// <param name="value">当前JSON对象。</param>
    /// <param name="creatorType">所属Creator的实际类型。</param>
    /// <param name="context">属性组名称。</param>
    /// <returns>字段元数据；不包含内部运行字段。</returns>
    public static List<Field> Fields(Type type, JsonObject value, string creatorType, string context)
    {
        // 顶层Emitter和Creator结构不使用运行对象序列化。
        if (type == typeof(EmitterDocument)) return new()
        {
            new("Core", typeof(EmitterCoreAttribute), new JsonObject(), "发射器共用属性。"),
            new("VNodes", typeof(JsonObject), Creator("VBullet"), "唯一根生成器；子节点位于Children。")
        };
        if (type == typeof(JsonObject))
        {
            // Creator结构字段，顺序与属性面板保持一致。
            var fields = new List<Field>
            {
                new("Core", CoreType(creatorType), JsonNode.Parse("{\"Type\":\"VNode\"}"), "类型、数量、寿命与运动约定。"),
                new("BaseAttributes", typeof(VNodeSpawnAttribute[]), JsonNode.Parse("[{}]"), "每轮基础成员；支持多个基础项。"),
                new("AddAttributes", typeof(VNodeSpawnAttribute), new JsonObject(), "逐轮增量：第g轮增加g倍，不继承上一颗状态。"),
                new("RandDiffAttributes", typeof(VNodeRandDiffAttribute), new JsonObject(), "批次与成员的Center随机总宽度；静态画布不抽随机。"),
                new("Timeline", typeof(TimelineAttribute[]), JsonNode.Parse("[{\"StartMs\":0}]"), "相对父对象激活时刻，整数毫秒。"),
                new("MemberTimeline", typeof(TimelineAttribute[]), new JsonArray(), "相对成员出生时刻的参数动作。"),
                new("Children", typeof(JsonObject[]), new JsonArray(), "按声明顺序运行的子生成器。")
            };
            if (creatorType is "VBullet" or "VLaser") fields.Add(new("Display", typeof(VBulletDisplayAttribute), new JsonObject(), "仅显示参数，不改变运动。"));
            if (creatorType is "VPath" or "VLaser") fields.Add(new("PathQueue", typeof(VPathSegmentAttribute[]), new JsonArray(PathSegment()), "依次相连的路径段。"));
            if (creatorType == "VLaser") fields.Add(new("Laser", typeof(VLaserAttribute), new JsonObject(), "激光长度、阶段时间与颜色。"));
            return fields;
        }
        // 实际属性的声明类型决定字符串与数字表达式的区别。
        // 错误形状仍展示原始字段，避免业务无效文件阻断JSON修复入口。
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum || Nullable.GetUnderlyingType(type) is not null || ElementType(type) is not null) return new();
        // 类型的反射和默认配置只计算一次，返回独立默认值供模式筛选及添加字段。
        if (!FieldCache.TryGetValue(type, out var cached))
        {
            var instance = Activator.CreateInstance(type);
            cached = type.GetProperties().Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>() is null && property.CanWrite)
                .OrderBy(property => InheritanceDepth(property.DeclaringType!)).ThenBy(property => property.MetadataToken)
                .Select(property => new Field(property.Name, property.PropertyType, Default(property, instance),
                    Descriptions.GetValueOrDefault(property.DeclaringType!.Name + "." + property.Name, property.Name))).ToArray();
            FieldCache.Add(type, cached);
        }
        var result = cached.Select(field => field with { Default = field.Default?.DeepClone() }).ToList();
        if (typeof(VNodeCoreAttribute).IsAssignableFrom(type))
            result.Add(new("CopySource", typeof(string), JsonValue.Create(""), "引用同级更早的非复制Creator名称；Name用作复制后缀，保留原始指令。"));
        if (context is "Batch" or "Member") result.RemoveAll(field => field.Name == "SpawnDelayMs");
        if (context == "Timeline") result.RemoveAll(field => field.Name == "Set");
        // 可选正数时间首次添加时给出可运行值，避免默认0必然失败。
        for (int index = 0; index < result.Count; index++)
            if (result[index].Name is "LifeTimeMs" or "IntervalMs" or "EndMs")
                result[index] = result[index] with { Default = JsonValue.Create(result[index].Name == "LifeTimeMs" ? 4000 : result[index].Name == "EndMs" ? 5000 : 1000) };
        if (type == typeof(VBulletDisplayAttribute) && creatorType == "VLaser") result.RemoveAll(field => field.Name != "BlendMode");
        if (type == typeof(VPathSegmentAttribute))
        {
            result.RemoveAll(field => field.Name == "AimPlayerOffset");
            result.Add(new("Type", typeof(string), JsonValue.Create("AimPlayer"), "AimPlayer简写：冻结生成时玩家位置，仅搭配X、Y偏移；移除普通PathMode字段后使用。"));
            if (value.ContainsKey("Type"))
            {
                result.RemoveAll(field => field.Name is "X" or "Y");
                result.Add(new("X", typeof(double), JsonValue.Create(0), "相对玩家世界横坐标的偏移，逻辑像素。"));
                result.Add(new("Y", typeof(double), JsonValue.Create(0), "相对玩家世界纵坐标的偏移，逻辑像素。"));
            }
        }
        // 模式只限制可添加字段；已有不合法字段仍按原文显示，供用户修复。
        IReadOnlyList<string>? allowed = null;
        if (type == typeof(VPathSegmentAttribute))
            allowed = value.ContainsKey("Type") ? ProtocolModes.PathAim.Fields("AimPlayer")
                : ProtocolModes.Path.Fields(value["PathMode"]?.ToString() ?? "XY") ?? new[] { "PathMode" };
        if (type == typeof(VNodeMoveActionAttribute))
            allowed = ProtocolModes.Displacement.Fields(value["Type"]?.ToString() ?? "XYMove") ?? new[] { "Type" };
        if (allowed is not null) result.RemoveAll(field => !allowed.Contains(field.Name));
        return result;
    }
    /// <summary>使基础类型字段先于派生类型字段，保持源码声明顺序。</summary>
    /// <param name="type">属性所属声明类型。</param>
    /// <returns>继承层级。</returns>
    private static int InheritanceDepth(Type type) => type.BaseType is null ? 0 : 1 + InheritanceDepth(type.BaseType);
    /// <summary>取得Creator专用Core属性类型。</summary>
    /// <param name="type">VNode、VBullet、VPath或VLaser。</param>
    /// <returns>对应运行属性类型。</returns>
    public static Type CoreType(string type) => type == "VBullet" ? typeof(VBulletCoreAttribute) : type == "VLaser" ? typeof(VLaserCoreAttribute) : typeof(VNodeCoreAttribute);
    /// <summary>取得字段可添加的默认值，枚举保存字符串。</summary>
    /// <param name="property">运行属性。</param>
    /// <param name="instance">默认属性对象。</param>
    /// <returns>独立JSON值。</returns>
    private static JsonNode? Default(PropertyInfo property, object? instance)
    {
        // 新增嵌套属性先使用空组；可空标量使用可编辑的初始值。
        object? value = property.GetValue(instance);
        if (value is Enum) return JsonValue.Create(value.ToString());
        // 去除可空包装后的实际属性类型。
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (ElementType(type) is not null) return new JsonArray();
        if (!type.IsPrimitive && type != typeof(string) && !type.IsEnum) return new JsonObject();
        if (value is not null) return JsonSerializer.SerializeToNode(value, property.PropertyType);
        if (type == typeof(string)) return JsonValue.Create("");
        if (type == typeof(bool)) return JsonValue.Create(false);
        if (type.IsPrimitive) return JsonValue.Create(type == typeof(double) ? 0.0 : 0);
        if (ElementType(type) is not null) return new JsonArray();
        return new JsonObject();
    }
    /// <summary>读取数组或只读列表的元素类型。</summary>
    /// <param name="type">字段类型。</param>
    /// <returns>元素类型，非列表为空。</returns>
    public static Type? ElementType(Type type) => type.IsArray ? type.GetElementType() : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : null;
    /// <summary>创建可直接编辑的生成器模板。</summary>
    /// <param name="type">生成器类型。</param>
    /// <returns>没有名称冲突的独立JSON模板。</returns>
    public static JsonObject Creator(string type)
    {
        // 默认生成一次，后续在Timeline内设置周期。
        var node = JsonNode.Parse("{\"Core\":{},\"BaseAttributes\":[{}],\"Timeline\":[{\"StartMs\":0}]}")!.AsObject();
        node["Core"]!["Type"] = type;
        if (type == "VBullet") { node["Core"]!["LifeTimeMs"] = 4000; node["Display"] = new JsonObject(); }
        if (type == "VLaser") node["Laser"] = new JsonObject();
        if (type == "VPath")
        {
            node["Core"]!["Amount"] = 8;
            node["PathQueue"] = new JsonArray(PathSegment());
        }
        return node;
    }
    /// <summary>创建列表新项，避免从上一项隐式继承名称。</summary>
    /// <param name="type">元素类型。</param>
    /// <param name="context">所属属性组。</param>
    /// <param name="variant">位移动作或Creator的模板类型。</param>
    /// <returns>新项JSON。</returns>
    public static JsonNode Item(Type type, string context, string variant)
    {
        if (type == typeof(JsonObject)) return Creator(variant);
        if (type == typeof(VNodeMoveActionAttribute)) return JsonNode.Parse(variant switch
        {
            "PMove" => "{\"Type\":\"PMove\",\"Angle\":0,\"Dist\":0}",
            "TarMove" => "{\"Type\":\"TarMove\",\"X\":640,\"Y\":600,\"Dist\":100}",
            _ => "{\"Type\":\"XYMove\",\"X\":0,\"Y\":0}"
        })!;
        if (type == typeof(TimelineAttribute)) return JsonNode.Parse(context == "MemberTimeline" ? "{\"StartMs\":0,\"Set\":{\"Speed\":180}}" : "{\"StartMs\":0}")!;
        if (type == typeof(VPathSegmentAttribute)) return PathSegment();
        if (type == typeof(VPathPointAttribute)) return JsonNode.Parse("{\"X\":0,\"Y\":0}")!;
        if (type == typeof(string)) return JsonValue.Create("Left")!;
        if (type.IsPrimitive) return JsonValue.Create(0)!;
        return new JsonObject();
    }
    /// <summary>两种工作区共用的独立路径段模板，只有显式添加或切换时写入文档。</summary>
    /// <param name="aim">为真生成玩家瞄准简写，否则生成200像素向右直线。</param>
    /// <returns>独立JSON对象，不共享默认值节点。</returns>
    public static JsonObject PathSegment(bool aim = false) => JsonNode.Parse(aim
        ? """{"Type":"AimPlayer","X":0,"Y":0}"""
        : """{"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":200,"Y":0}]}""")!.AsObject();

    /// <summary>显式切换普通路径模式，保留端点、未知字段及可继续使用的原文表达式。</summary>
    /// <param name="value">当前文档事务内的路径段。</param>
    /// <param name="mode">XY、Bezier或Function。</param>
    public static void ChangePathMode(JsonObject value, string mode)
    {
        // 只清理其他模式专属字段，未知输入不会被顺手删除。
        var allowed = ProtocolModes.Path.Fields(mode) ?? throw new ArgumentException("未知路径模式。", nameof(mode));
        foreach (string key in ProtocolModes.Path.Values.SelectMany(name => ProtocolModes.Path.Fields(name)!).Distinct())
            if (!allowed.Contains(key)) value.Remove(key);
        value["PathMode"] = mode;
        if (mode == "Bezier" && !value.ContainsKey("ControlPoints")) value["ControlPoints"] = JsonNode.Parse("""[{"X":100,"Y":80}]""");
        if (mode == "Function")
        {
            if (!value.ContainsKey("AxisMode")) value["AxisMode"] = "Relative";
            if (!value.ContainsKey("X")) value["X"] = "L*t";
            if (!value.ContainsKey("Y")) value["Y"] = "80*sin(PI*t)";
        }
    }
    /// <summary>把表单文本转换成JSON，数字表达式保留字符串。</summary>
    /// <param name="text">用户文本；null表示显式空值。</param>
    /// <param name="type">运行属性类型。</param>
    /// <returns>JSON标量。</returns>
    public static JsonNode? Scalar(string text, Type type)
    {
        if (text == "null") return null;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string) || type.IsEnum) return JsonValue.Create(text);
        if (type == typeof(bool)) return JsonValue.Create(bool.Parse(text));
        if (type == typeof(double) || type == typeof(float))
        {
            // 只把合法JSON数值写成数值，其余文本由游戏表达式解析器校验。
            try { var value = JsonNode.Parse(text); if (value?.GetValueKind() == JsonValueKind.Number) return value; }
            catch (JsonException) { }
            return JsonValue.Create(text);
        }
        return long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long number)
            ? JsonValue.Create(number) : JsonValue.Create(text);
    }
}
