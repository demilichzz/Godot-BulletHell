using static JsonData;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>统一持有节点配置、子生成器树、多批成员及运行参数批量操作。</summary>
public partial class VNodeCreator
{
    /// <summary>共用核心参数，子弹队列运行时使用派生核心类型。</summary>
    public VNodeCoreAttribute Core { get; protected set; } = new();
    /// <summary>非空基础成员列表；每轮按声明顺序求值。</summary>
    public IReadOnlyList<VNodeSpawnAttribute> BaseAttributes { get; protected set; } = Array.AsReadOnly(new[] { new VNodeSpawnAttribute() });
    /// <summary>所有基础项共用的逐轮固定增量。</summary>
    public VNodeSpawnAttribute AddAttributes { get; protected set; } = new();
    /// <summary>整批共享及逐颗独立的Center随机总宽度。</summary>
    public VNodeRandDiffAttribute RandDiffAttributes { get; protected set; } = new();
    /// <summary>每次生成规则触发的计划总成员数。</summary>
    public int TotalAmount => checked(Core.Amount * BaseAttributes.Count);
    /// <summary>相对父对象激活时刻的生成规则。</summary>
    public IReadOnlyList<TimelineAttribute> Timeline { get; protected set; } = Array.Empty<TimelineAttribute>();
    /// <summary>相对每个实际成员出生时刻的参数动作。</summary>
    public IReadOnlyList<TimelineAttribute> MemberTimeline { get; protected set; } = Array.Empty<TimelineAttribute>();
    /// <summary>声明顺序固定的子生成器。</summary>
    public IReadOnlyList<VNodeCreator> Children { get; private set; } = Array.Empty<VNodeCreator>();
    /// <summary>仅保存成功生成且仍存活的成员，每个内层列表代表一次生成。</summary>
    public IReadOnlyList<IReadOnlyList<VNode>> Batches { get; }
    /// <summary>当前生成器全部批次的只读平铺视图，不含Children。</summary>
    public IReadOnlyList<VNode> Members { get; }
    /// <summary>Follow只继承参考对象平移；子弹默认Snapshot。</summary>
    public bool Follow => Core.CreatePositionMode == "Follow" || (Core.CreatePositionMode is null && DefaultFollow);
    /// <summary>未配置模式时是否跟随。</summary>
    protected virtual bool DefaultFollow => true;
    // 批次为成员唯一存储；只读包装防止外部修改。
    private readonly List<IReadOnlyList<VNode>> _batches = new();

    /// <summary>创建空运行集合，配置由加载器填写。</summary>
    public VNodeCreator()
    {
        Batches = _batches.AsReadOnly();
        Members = new MemberView<VNode>(() => EnumerateMembers(false));
    }

    /// <summary>从唯一批次来源枚举成员，可包含后代生成器。</summary>
    /// <param name="recursive">是否按前序包含Children。</param>
    /// <returns>当前存活成员；枚举期间不得修改集合。</returns>
    internal IEnumerable<VNode> EnumerateMembers(bool recursive)
    {
        // 批次和成员均按出生顺序枚举。
        foreach (var batch in _batches)
            foreach (var member in batch) yield return member;
        if (recursive)
            foreach (var child in Children)
                foreach (var member in child.EnumerateMembers(true)) yield return member;
    }

    /// <summary>稳定访问当前成员，支持动作注销当前或后续成员。</summary>
    /// <param name="visit">对每个仍存活成员执行一次的操作。</param>
    internal void VisitMembers(Action<VNode> visit)
    {
        // 删除后不递增索引，保留其他成员的相对顺序。
        for (int batchIndex = 0; batchIndex < _batches.Count;)
        {
            var batch = _batches[batchIndex];
            for (int index = 0; index < batch.Count;)
            {
                var member = batch[index];
                if (member.IsAlive) visit(member);
                if (index < batch.Count && ReferenceEquals(batch[index], member)) index++;
            }
            if (batchIndex < _batches.Count && ReferenceEquals(_batches[batchIndex], batch)) batchIndex++;
        }
        foreach (var child in Children) child.VisitMembers(visit);
    }

    /// <summary>共享成员来源的类型过滤视图，不复制存活列表。</summary>
    /// <typeparam name="T">需要呈现的节点类型。</typeparam>
    internal sealed class MemberView<T> : IReadOnlyList<T> where T : VNode
    {
        // 每次枚举读取同一运行来源，可用于尚未启动的Emitter。
        private readonly Func<IEnumerable<VNode>> _source;
        /// <summary>绑定运行来源。</summary>
        /// <param name="source">当前成员枚举入口。</param>
        internal MemberView(Func<IEnumerable<VNode>> source) => _source = source;
        /// <summary>当前匹配成员数量。</summary>
        public int Count => _source().OfType<T>().Count();
        /// <summary>当前零基索引处的成员，下标不表示稳定身份。</summary>
        public T this[int index] => _source().OfType<T>().ElementAt(index);
        /// <summary>枚举当前匹配对象。</summary>
        /// <returns>只读枚举器。</returns>
        public IEnumerator<T> GetEnumerator() => _source().OfType<T>().GetEnumerator();
        /// <summary>提供非泛型枚举。</summary>
        /// <returns>只读枚举器。</returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

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

    /// <summary>统一逐颗求值；节点不受子弹容量限制。</summary>
    /// <param name="emitter">所属发射器。</param>
    /// <param name="manager">本场子弹管理器，子弹派生类使用。</param>
    /// <param name="reference">当前父实例、Boss或世界原点的null。</param>
    /// <param name="parent">实际生命周期父VNode，根生成时为空。</param>
    /// <param name="source">本次生成的有限世界逻辑像素起点。</param>
    internal void Emit(VBulletEmitter emitter, VBulletManager manager, Node2D? reference, VNode? parent, Vector2 source)
    {
        if (!source.IsFinite()) throw new ArgumentOutOfRangeException(nameof(source));
        // 每批持有独立快照、稳定顺序和共享随机，未出生对象不加入场景。
        var batch = new SpawnBatch(checked(_nextBatchOrder++), PrepareBatchPositions(source));
        for (int index = 0; index < TotalAmount; index++)
        {
            // 索引由配置固定，容量或已死亡成员不改变它。
            int birthIndex = index;
            int group = index / BaseAttributes.Count;
            long delay = checked(BaseAttributes[index % BaseAttributes.Count].SpawnDelayMs + group * AddAttributes.SpawnDelayMs);
            if (delay == 0) SpawnMember(batch, birthIndex, emitter, manager, reference, parent, source);
            else
            {
                var timeline = parent?.Timeline ?? emitter.Timeline
                    ?? throw new InvalidOperationException("延迟生成需要活动父对象时间线。");
                Action? action = null;
                action = () =>
                {
                    try
                    {
                        if (emitter.Timeline is not null && (parent is null || (parent.IsAlive && parent.CanGenerate)))
                            SpawnMember(batch, birthIndex, emitter, manager, reference, parent, source);
                    }
                    finally { if (parent is not null) parent.UntrackGeneration(action!); }
                };
                timeline.After(delay, action);
                parent?.TrackGeneration(action);
            }
        }
    }

    /// <summary>检查当前类型的生成容量，纯节点不受子弹上限限制。</summary>
    /// <param name="manager">子弹管理器。</param>
    /// <returns>可生成时为真。</returns>
    protected virtual bool CanCreate(VBulletManager manager) => true;

    /// <summary>在安排出生前准备批次专属位置，普通节点和子弹不分配缓存。</summary>
    /// <param name="source">批次触发时父参考点的世界逻辑像素坐标。</param>
    /// <returns>相对source的固定出生偏移列表，普通生成器返回null。</returns>
    protected virtual IReadOnlyList<Vector2>? PrepareBatchPositions(Vector2 source) => null;
    /// <summary>创建无显示节点并交给Emitter登记。</summary>
    /// <param name="emitter">所属Emitter。</param>
    /// <param name="manager">子弹管理器，纯节点不登记到其中。</param>
    /// <param name="position">全局逻辑像素出生位置。</param>
    /// <param name="move">本颗已求值的运动参数。</param>
    /// <returns>已登记的实际节点。</returns>
    protected virtual VNode? CreateMember(VBulletEmitter emitter, VBulletManager manager, Vector2 position, VNodeMoveAttribute move)
        => emitter.AddNode(position, move, Core.LifeTimeMs, Core.AAngleIsSameAsAngle);

    /// <summary>注销成员并回收空批次，不改变固定出生索引。</summary>
    /// <param name="member">本生成器中已失效的成员。</param>
    internal void Unregister(VNode member)
    {
        member.Batch?.Remove(member);
        if (member.Batch?.Count == 0 && member.BatchView is not null)
        {
            _batches.Remove(member.BatchView);
            _batchOrders.Remove(member.BatchView);
        }
        member.Batch = null;
        member.BatchView = null;
    }

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
        foreach (var member in EnumerateMembers(false)) member.ApplyParameters(parameters);
    }

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

    /// <summary>统一抽取中心随机，零宽度不消耗序列。</summary>
    /// <param name="width">非负总宽度。</param>
    /// <returns>正负半宽内的偏移。</returns>
    private static double Random(double width) => VMath.getRandomDiff(width, RandomDiffMode.Center);

}
