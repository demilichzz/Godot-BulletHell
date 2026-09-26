using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>共用节点队列配置、批次生成、成员跟踪及运行参数批量操作。</summary>
public class VNodeQueue
{
    /// <summary>共用核心参数，子弹队列运行时使用派生核心类型。</summary>
    public VNodeCoreAttribute Core { get; protected set; } = new();
    /// <summary>出生运动基础值，纯节点默认静止。</summary>
    public BulletMoveAttribute BaseAttributes { get; protected set; } = new();
    /// <summary>按出生索引增加的运动参数。</summary>
    public BulletMoveAttribute AddAttributes { get; protected set; } = new();
    /// <summary>包括首颗在内独立抽样的运动随机总宽度。</summary>
    public BulletMoveAttribute AddAttributesRandDiff { get; protected set; } = new();
    /// <summary>逻辑参考、跟随模式和出生位移。</summary>
    public BulletPositionAttribute PositionAttributes { get; protected set; } = new();
    /// <summary>相对父对象激活时刻的生成规则。</summary>
    public IReadOnlyList<TimelineAttribute> Timeline { get; protected set; } = Array.Empty<TimelineAttribute>();
    /// <summary>相对每个实际成员出生时刻的参数动作。</summary>
    public IReadOnlyList<TimelineAttribute> MemberTimeline { get; protected set; } = Array.Empty<TimelineAttribute>();
    /// <summary>本批次的全局参考位置，逻辑像素。</summary>
    public Vector2 Source { get; protected set; }
    /// <summary>成功生成且仍存活的成员，共用唯一列表。</summary>
    public IReadOnlyList<VNode> Members { get; }
    /// <summary>实际使用的跟随模式，纯节点默认为Follow。</summary>
    public bool Follow => PositionAttributes.Mode == "Follow" || (PositionAttributes.Mode is null && DefaultFollow);
    /// <summary>当前队列类型的默认跟随模式。</summary>
    protected virtual bool DefaultFollow => true;
    // 成员唯一存储及本次发射状态。
    private readonly List<VNode> _members = new();
    private bool _emitted;
    private bool _instanceReady;
    // 加载校验完成后，实例可安全共享冻结属性。
    private bool _validated;
    // 本批次绑定的具体参考与生命周期父节点。
    private Node2D? _reference;
    private VNode? _parent;

    /// <summary>建立可复用的只读成员视图。</summary>
    public VNodeQueue() => Members = _members.AsReadOnly();

    /// <summary>从资源路径加载节点队列数据，不消耗随机。</summary>
    /// <param name="path">res://JSON资源路径。</param>
    /// <returns>已校验的节点队列模板。</returns>
    public static VNodeQueue Load(string path) => FromJson(ReadFile(path), path);

    /// <summary>从JSON加载无显示的节点队列。</summary>
    /// <param name="json">标准JSON文本。</param>
    /// <param name="sourceName">错误来源名称，默认内存。</param>
    /// <returns>未生成的配置模板。</returns>
    public static VNodeQueue FromJson(string json, string sourceName = "内存")
        => Parse(json, sourceName, element =>
        {
            var queue = ReadQueue(element, false);
            queue.ValidateSchedules(1);
            return queue;
        });

    /// <summary>复制只读配置并创建独立成员批次。</summary>
    /// <param name="source">本次全局参考位置，有限逻辑像素。</param>
    /// <returns>尚未生成的独立实例。</returns>
    public virtual VNodeQueue CreateInstance(Vector2 source)
    {
        if (!source.IsFinite()) throw new ArgumentOutOfRangeException(nameof(source));
        if (!_validated) { ValidateAttributes(); ValidateSchedules(1); _validated = true; }
        // 派生队列只替换创建类型和子弹专用配置。
        VNodeQueue instance = NewInstance();
        instance.Core = Core;
        instance.BaseAttributes = BaseAttributes;
        instance.AddAttributes = AddAttributes;
        instance.AddAttributesRandDiff = AddAttributesRandDiff;
        instance.PositionAttributes = PositionAttributes;
        instance.Timeline = Timeline;
        instance.MemberTimeline = MemberTimeline;
        instance.Source = source;
        instance._instanceReady = true;
        instance._validated = true;
        return instance;
    }

    /// <summary>创建当前队列类型的空实例。</summary>
    /// <returns>用于承接共享配置的空批次。</returns>
    protected virtual VNodeQueue NewInstance() => new();

    /// <summary>为新批次绑定本次实际参考对象。</summary>
    /// <param name="reference">实际父节点、Boss或世界原点的null。</param>
    /// <param name="parent">具体生命周期父VNode，可为空。</param>
    internal void BindReference(Node2D? reference, VNode? parent) { _reference = reference; _parent = parent; }

    /// <summary>统一逐颗求值；节点不受子弹容量限制。</summary>
    /// <param name="emitter">所属发射器。</param>
    /// <param name="manager">本场子弹管理器，子弹派生类使用。</param>
    internal virtual void Emit(BulletEmitter emitter, BulletManager manager)
    {
        BeginEmit();
        if (!CanCreate(manager)) return;
        // 共享随机先按动作数组、第一标量、第二标量顺序抽取。
        double aim = Core.AngleMode == "AimPlayer" ? VMath.GetAngleBetween2Points(Source, GlobalEvent.GetPlayer().GlobalPosition) : 0;
        var actions = PositionAttributes.RefMoveQueue;
        var shared = new (double First, double Second)[actions.Count];
        for (int index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var first = action.Type == "PMove" ? action.Angle! : action.X!;
            var second = action.Type == "PMove" ? action.Dist! : action.Y!;
            shared[index] = (first.Value + Random(first.RandDiff), second.Value + Random(second.RandDiff));
        }
        for (int index = 0; index < Core.Amount; index++)
        {
            if (!CanCreate(manager)) break;
            // 运动随机固定按Angle、Speed、有效AAngle、ASpeed求值。
            double angle = aim + Evaluate(BaseAttributes.Angle, AddAttributes.Angle, AddAttributesRandDiff.Angle, index);
            var move = new BulletMoveAttribute
            {
                Angle = angle,
                Speed = Evaluate(BaseAttributes.Speed, AddAttributes.Speed, AddAttributesRandDiff.Speed, index),
                AAngle = Core.AAngleIsSameAsAngle ? angle : Evaluate(BaseAttributes.AAngle, AddAttributes.AAngle, AddAttributesRandDiff.AAngle, index),
                ASpeed = Evaluate(BaseAttributes.ASpeed, AddAttributes.ASpeed, AddAttributesRandDiff.ASpeed, index)
            };
            Vector2 position = Source;
            for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
            {
                // 每颗从本批参考位置依次累计位移。
                var action = actions[actionIndex];
                var first = action.Type == "PMove" ? action.Angle! : action.X!;
                var second = action.Type == "PMove" ? action.Dist! : action.Y!;
                double x = Evaluate(shared[actionIndex].First, first.ValueAdd, first.RandDiffAdd, index);
                double y = Evaluate(shared[actionIndex].Second, second.ValueAdd, second.RandDiffAdd, index);
                position = action.Type == "PMove" ? VMath.PolarMove(position, x, y)
                    : new Vector2((float)(position.X + x), (float)(position.Y + y));
                if (!position.IsFinite()) throw new OverflowException($"{Core.Id}: 出生位置溢出。");
            }
            VNode? member = CreateMember(emitter, manager, position, move);
            if (member is null) break;
            Track(member, index);
            member.BindReference(_reference, _parent, Follow);
            // 成员动作先登记，子生成随后登记，顺序不依赖字典。
            foreach (var rule in MemberTimeline)
                rule.Register(member.Timeline!, index, () => member.ApplyParameters(rule.Set!));
            if (member is not Bullet) emitter.AttachChildren(member, Core.Id);
        }
    }

    /// <summary>确认批次只生成一次。</summary>
    protected void BeginEmit()
    {
        if (!_instanceReady || _emitted) throw new InvalidOperationException("须创建独立批次，且同一批次只能生成一次。");
        _emitted = true;
    }

    /// <summary>为旧代码构造入口启用一次生成。</summary>
    protected void EnableLegacyInstance() => _instanceReady = true;

    /// <summary>检查当前类型的生成容量，纯节点不受子弹上限限制。</summary>
    /// <param name="manager">子弹管理器。</param>
    /// <returns>可生成时为真。</returns>
    protected virtual bool CanCreate(BulletManager manager) => true;

    /// <summary>创建无显示节点并交给Emitter登记。</summary>
    /// <param name="emitter">所属Emitter。</param>
    /// <param name="manager">子弹管理器，纯节点不登记到其中。</param>
    /// <param name="position">全局逻辑像素出生位置。</param>
    /// <param name="move">本颗已求值的运动参数。</param>
    /// <returns>已登记的实际节点。</returns>
    protected virtual VNode? CreateMember(BulletEmitter emitter, BulletManager manager, Vector2 position, BulletMoveAttribute move)
        => emitter.AddNode(position, move, Core.LifeTimeS, Core.AAngleIsSameAsAngle);

    /// <summary>记录实际生成的成员及其不变出生索引。</summary>
    /// <param name="member">成功生成的实际对象。</param>
    /// <param name="index">零基出生索引。</param>
    protected void Track(VNode member, int index)
    {
        member.Queue = this;
        member.BirthIndex = index;
        _members.Add(member);
    }

    /// <summary>注销已释放成员，不改变其他成员的出生索引。</summary>
    /// <param name="member">已失效的对象。</param>
    internal void Unregister(VNode member) => _members.Remove(member);

    /// <summary>对当前存活成员设置相同方向。</summary>
    /// <param name="angleRadians">有限弧度，顺时针为正。</param>
    public void SetDirection(double angleRadians) => ApplyParameters(new ParameterActionAttribute { Angle = angleRadians });
    /// <summary>对当前存活成员设置相同外部速度。</summary>
    /// <param name="speed">有符号有限逻辑像素每秒。</param>
    public void SetSpeed(double speed) => ApplyParameters(new ParameterActionAttribute { Speed = speed });
    /// <summary>对存活成员逐个执行相同原子参数动作，不修改模板。</summary>
    /// <param name="parameters">运行参数设置。</param>
    public void ApplyParameters(ParameterActionAttribute parameters)
    {
        parameters.Validate();
        foreach (var member in _members) member.ApplyParameters(parameters);
    }

    /// <summary>读取一份队列JSON对象，直接构造对应属性对象。</summary>
    /// <param name="element">队列JSON根对象。</param>
    /// <param name="bullet">是否创建子弹队列。</param>
    /// <returns>已完成公共及专用校验的模板。</returns>
    internal static VNodeQueue ReadQueue(JsonElement element, bool bullet)
    {
        CheckFields(element, bullet
            ? new[] { "Core", "Display", "BaseAttributes", "AddAttributes", "AddAttributesRandDiff", "PositionAttributes", "Timeline", "MemberTimeline" }
            : new[] { "Core", "BaseAttributes", "AddAttributes", "AddAttributesRandDiff", "PositionAttributes", "Timeline", "MemberTimeline" });
        // 先识别版本，再解析已移除字段，旧JSON获得明确版本错误。
        JsonElement core = Required(element, "Core");
        if (core.TryGetProperty("Version", out var version) && version.GetInt32() != 2) throw new JsonException("Core.Version只支持2，旧版数据需要迁移。");
        VNodeQueue queue = bullet ? new BulletQueue() : new VNodeQueue();
        queue.Core = bullet ? Read<BulletCoreAttribute>(core) : Read<VNodeCoreAttribute>(core);
        JsonElement baseValues = Required(element, "BaseAttributes");
        queue.BaseAttributes = Read<BulletMoveAttribute>(baseValues);
        if (bullet && !baseValues.TryGetProperty("Speed", out _)) queue.BaseAttributes = queue.BaseAttributes with { Speed = 180 };
        queue.AddAttributes = element.TryGetProperty("AddAttributes", out var add) ? Read<BulletMoveAttribute>(add) : new();
        queue.AddAttributesRandDiff = element.TryGetProperty("AddAttributesRandDiff", out var random) ? Read<BulletMoveAttribute>(random) : new();
        queue.PositionAttributes = Read<BulletPositionAttribute>(Required(element, "PositionAttributes"));
        queue.Timeline = ReadTimes(element, "Timeline");
        queue.MemberTimeline = ReadTimes(element, "MemberTimeline");
        if (queue is BulletQueue bullets) bullets.ReadDisplay(Required(element, "Display"));
        queue.ValidateAttributes();
        queue._validated = true;
        return queue;
    }

    /// <summary>校验公共参数并冻结位移动作列表。</summary>
    protected virtual void ValidateAttributes()
    {
        if (string.IsNullOrWhiteSpace(Core.Id) || Core.Id == "Emitter") throw new JsonException("Core.Id必须为非空且非保留名Emitter。");
        if (Core.Version != 2 || Core.Amount <= 0) throw new JsonException("Core.Version须为2且Amount必须为正整数。");
        if (Core.AngleMode is not ("Fixed" or "AimPlayer")) throw new JsonException("Core.AngleMode只支持Fixed或AimPlayer。");
        VNode.ValidateMotion(Vector2.Zero, BaseAttributes, Core.LifeTimeS);
        ValidateMove(AddAttributes, false);
        ValidateMove(AddAttributesRandDiff, true);
        if (string.IsNullOrWhiteSpace(PositionAttributes.RefObject) || PositionAttributes.Mode is not (null or "Follow" or "Snapshot"))
            throw new JsonException("PositionAttributes参考或模式无效。");
        if (PositionAttributes.RefMoveQueue is null) throw new JsonException("RefMoveQueue不能为空。");
        // 拷贝列表，防止外部通过数组修改共享定义。
        var actions = PositionAttributes.RefMoveQueue.ToArray();
        foreach (var action in actions)
        {
            if (action is null) throw new JsonException("位移动作不能为null。");
            if (action.Type == "PMove" && action.X is null && action.Y is null) { ValidateScalar(action.Angle); ValidateScalar(action.Dist); }
            else if (action.Type == "XYMove" && action.Angle is null && action.Dist is null) { ValidateScalar(action.X); ValidateScalar(action.Y); }
            else throw new JsonException("位移动作类型和标量字段不匹配。");
        }
        PositionAttributes = PositionAttributes with { RefMoveQueue = Array.AsReadOnly(actions) };
        foreach (var rule in MemberTimeline) rule.Validate(Core.Amount, true);
    }

    /// <summary>根据父队列数量验证生成规则及重复节点寿命。</summary>
    /// <param name="parentAmount">父成员索引数量；Emitter为1。</param>
    internal void ValidateSchedules(int parentAmount)
    {
        foreach (var rule in Timeline) rule.Validate(parentAmount, false);
        if (this is not BulletQueue && Core.LifeTimeS is null
            && (Timeline.Count > 1 || Timeline.Any(rule => rule.Repeats(0) || rule.Repeats(parentAmount - 1))))
            throw new JsonException($"{Core.Id}: 重复生成VNode必须配置有限LifeTimeS。");
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

    /// <summary>校验运动增量或非负随机宽度。</summary>
    /// <param name="move">四项运动属性。</param>
    /// <param name="random">是否要求非负。</param>
    private static void ValidateMove(BulletMoveAttribute move, bool random)
    {
        foreach (double value in new[] { move.Angle, move.Speed, move.AAngle, move.ASpeed })
            if (!double.IsFinite(value) || (random && value < 0)) throw new JsonException("运动增量或随机宽度无效。");
    }

    /// <summary>校验一个必需的位移标量。</summary>
    /// <param name="scalar">有限基础值、增量及非负随机宽度。</param>
    private static void ValidateScalar(BulletScalarAttribute? scalar)
    {
        if (scalar is null || !double.IsFinite(scalar.Value) || !double.IsFinite(scalar.ValueAdd)
            || !double.IsFinite(scalar.RandDiff) || scalar.RandDiff < 0 || !double.IsFinite(scalar.RandDiffAdd) || scalar.RandDiffAdd < 0)
            throw new JsonException("位移标量缺失或数值无效。");
    }

    /// <summary>求解首颗也含独立随机偏移的索引值。</summary>
    /// <param name="value">基础值。</param>
    /// <param name="add">固定增量。</param>
    /// <param name="width">非负随机总宽度。</param>
    /// <param name="index">零基出生索引。</param>
    /// <returns>有限求值结果。</returns>
    private static double Evaluate(double value, double add, double width, int index)
    {
        double result = value + index * add + Random(width);
        return double.IsFinite(result) ? result : throw new OverflowException("队列属性求值溢出。");
    }

    /// <summary>统一抽取中心随机，零宽度不消耗序列。</summary>
    /// <param name="width">非负总宽度。</param>
    /// <returns>正负半宽内的偏移。</returns>
    private static double Random(double width) => VMath.getRandomDiff(width, RandomDiffMode.Center);

    /// <summary>读取Godot数据文件。</summary>
    /// <param name="path">JSON资源路径。</param>
    /// <returns>文件完整文本。</returns>
    internal static string ReadFile(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        return file is null ? throw new System.IO.IOException($"无法读取{path}：{Godot.FileAccess.GetOpenError()}") : file.GetAsText();
    }

    /// <summary>统一解析重复字段并附加来源信息。</summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="json">JSON文本。</param>
    /// <param name="source">来源名称。</param>
    /// <param name="read">仅在加载阶段使用的对象读取函数。</param>
    /// <returns>已构造的目标对象。</returns>
    internal static T Parse<T>(string json, string source, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            CheckDuplicates(document.RootElement, "$");
            return read(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
        {
            throw new JsonException($"{source}: {error.Message}", error);
        }
    }

    /// <summary>直接将一个属性组反序列化，拒绝未知字段。</summary>
    /// <typeparam name="T">属性类型。</typeparam>
    /// <param name="element">对应JSON值。</param>
    /// <returns>已构造的非空属性对象。</returns>
    internal static T Read<T>(JsonElement element)
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new BulletNumberConverter());
        options.Converters.Add(new JsonStringEnumConverter<BulletTeam>(allowIntegerValues: false));
        return element.Deserialize<T>(options) ?? throw new JsonException("属性值不能为null。");
    }

    /// <summary>取得必须存在的非空属性。</summary>
    /// <param name="element">父JSON对象。</param>
    /// <param name="name">必需字段名。</param>
    /// <returns>对应JSON值。</returns>
    internal static JsonElement Required(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : throw new JsonException(name + "缺失或为null。");

    /// <summary>校验对象的所有字段在允许集合内。</summary>
    /// <param name="element">JSON对象。</param>
    /// <param name="allowed">合法字段名。</param>
    internal static void CheckFields(JsonElement element, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("需要JSON对象。");
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) throw new JsonException(property.Name + ": 未知字段。");
    }

    /// <summary>递归检查重复字段，保留完整错误路径。</summary>
    /// <param name="element">当前值。</param>
    /// <param name="path">JSON路径。</param>
    private static void CheckDuplicates(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException(path + "." + property.Name + ": 重复字段。");
                CheckDuplicates(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var value in element.EnumerateArray()) CheckDuplicates(value, $"{path}[{index++}]");
        }
    }
}
