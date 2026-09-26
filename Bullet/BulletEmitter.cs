using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>弹幕所属阵营，用于筛选伤害目标。</summary>
public enum BulletTeam
{
    // Boss 发出的敌弹。
    Enemy,
    // 玩家发出的攻击弹。
    Player
}

/// <summary>管理数据生成规则、VNode引用树、共通子弹参数及停止策略。</summary>
public class BulletEmitter : IVTimelineOwner
{
    /// <summary>发射器的共通参数；代码入口默认敌方、伤害1并保留已发子弹。</summary>
    public EmitterCoreAttribute Core { get; private set; } = new();
    /// <summary>按声明顺序保存的节点队列定义。</summary>
    public IReadOnlyList<VNodeQueue> VNodes { get; private set; } = Array.Empty<VNodeQueue>();
    /// <summary>按声明顺序保存的子弹队列定义。</summary>
    public IReadOnlyList<BulletQueue> BulletQueues { get; private set; } = Array.Empty<BulletQueue>();
    /// <summary>本发射器激活时创建的逻辑时间线。</summary>
    public VTimeline? Timeline { get; private set; }
    /// <summary>所有仍存活的已发子弹。</summary>
    public IReadOnlyList<Bullet> Bullets { get; }
    /// <summary>当前仍存活的纯参考节点，不包括Bullet。</summary>
    public IReadOnlyList<VNode> Nodes { get; }
    /// <summary>所属战斗的子弹管理器，用于归属校验，不持有处理器。</summary>
    internal BulletManager? Manager { get; private set; }
    // 活动成员唯一存储，节点按稳定出生顺序排列。
    private readonly List<Bullet> _bullets = new();
    private readonly List<VNode> _nodes = new();
    // 子定义按父标识查找，列表保持声明顺序。
    private readonly Dictionary<string, List<VNodeQueue>> _children = new(StringComparer.Ordinal);
    // 所属Boss、纯节点物理容器及数据定义标记。
    private BossController? _owner;
    private Node2D? _nodeContainer;
    private bool _hasData;

    /// <summary>建立不可从外部增删的成员视图。</summary>
    public BulletEmitter()
    {
        Bullets = _bullets.AsReadOnly();
        Nodes = _nodes.AsReadOnly();
    }

    /// <summary>从资源文件直接加载发射器及其队列定义。</summary>
    /// <param name="path">版本2的JSON资源路径。</param>
    /// <returns>尚未启动的发射器。</returns>
    public static BulletEmitter Load(string path) => FromJson(VNodeQueue.ReadFile(path), path);

    /// <summary>反序列化发射器，解析引用树并校验全部时间索引边界。</summary>
    /// <param name="json">标准JSON文本。</param>
    /// <param name="sourceName">错误来源名称。</param>
    /// <returns>已校验且未消耗随机的发射器。</returns>
    public static BulletEmitter FromJson(string json, string sourceName = "内存")
        => VNodeQueue.Parse(json, sourceName, element =>
        {
            VNodeQueue.CheckFields(element, new[] { "Core", "VNodes", "BulletQueues" });
            JsonElement coreJson = VNodeQueue.Required(element, "Core");
            if (coreJson.TryGetProperty("Version", out var version) && version.GetInt32() != 2)
                throw new JsonException("Emitter.Core.Version只支持2。");
            if (coreJson.TryGetProperty("Team", out var team)
                && (team.ValueKind != JsonValueKind.String || team.GetString() is not ("Enemy" or "Player")))
                throw new JsonException("Emitter.Core.Team只支持Enemy或Player。");
            var emitter = new BulletEmitter { Core = VNodeQueue.Read<EmitterCoreAttribute>(coreJson), _hasData = true };
            // 两个数组分别直接映射为队列，统一登记时节点定义在前。
            var nodes = new List<VNodeQueue>();
            var bullets = new List<BulletQueue>();
            if (element.TryGetProperty("VNodes", out var nodeJson))
                foreach (var value in nodeJson.EnumerateArray()) nodes.Add(VNodeQueue.ReadQueue(value, false));
            if (element.TryGetProperty("BulletQueues", out var bulletJson))
                foreach (var value in bulletJson.EnumerateArray()) bullets.Add((BulletQueue)VNodeQueue.ReadQueue(value, true));
            emitter.VNodes = nodes.AsReadOnly();
            emitter.BulletQueues = bullets.AsReadOnly();
            emitter.ValidateData();
            return emitter;
        });

    /// <summary>创建发射器时间线，由Build绑定代码或数据规则。</summary>
    /// <param name="owner">生命周期所属Boss，空间引用可配置为null。</param>
    /// <param name="manager">本场子弹管理器。</param>
    public void Start(BossController owner, BulletManager manager)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(manager);
        if (Timeline is not null) throw new InvalidOperationException("发射器已启动。");
        _owner = owner;
        Manager = manager;
        Timeline = GlobalEvent.CreateTimeline(this);
        try { Build(manager, owner); }
        catch { Stop(); throw; }
    }

    /// <summary>代码派生类可覆盖；数据实例直接绑定已加载规则。</summary>
    /// <param name="manager">本场子弹管理器。</param>
    /// <param name="owner">所属Boss。</param>
    protected virtual void Build(BulletManager manager, BossController owner)
    {
        if (_hasData) AttachChildren(null, "Emitter");
    }

    /// <summary>供具体Emitter的Build加载并绑定数据，不新增具体类状态。</summary>
    /// <param name="definition">已加载且未启动的数据发射器。</param>
    protected void UseDefinition(BulletEmitter definition)
    {
        if (Timeline is null || !definition._hasData || definition.Timeline is not null)
            throw new InvalidOperationException("须在Build中绑定未启动的数据定义。");
        Core = definition.Core;
        VNodes = definition.VNodes;
        BulletQueues = definition.BulletQueues;
        _hasData = true;
        ValidateData();
        AttachChildren(null, "Emitter");
    }

    /// <summary>停止节点树和未来生成，按策略保留或清除本发射器子弹。</summary>
    public void Stop()
    {
        Timeline?.Cancel();
        Timeline = null;
        // 先脱离参考链，保留停止瞬间的世界位置。
        foreach (var bullet in _bullets) bullet.DetachReference();
        foreach (var node in _nodes.ToArray()) ReleaseNode(node);
        if (_nodeContainer is not null && GodotObject.IsInstanceValid(_nodeContainer)) _nodeContainer.QueueFree();
        _nodeContainer = null;
        if (Core.StopMode == "ClearBullets" && Manager is not null && GodotObject.IsInstanceValid(Manager)) Manager.ClearEmitter(this);
    }

    /// <summary>在Boss本步移动后推进发射器及父先子后的纯节点。</summary>
    /// <param name="units">非负整数逻辑时间单位。</param>
    internal void AdvanceUnits(long units)
    {
        if (units < 0) throw new ArgumentOutOfRangeException(nameof(units));
        if (Timeline is null) return;
        Timeline.AdvanceUnits(units);
        // 只处理步开始前的成员，步末新生节点从下一步开始运动。
        foreach (var node in _nodes.ToArray())
        {
            if (!node.IsAlive) continue;
            node.Advance(units / (double)VTimerProcessor.UnitsPerSecond);
            if (node.Expired) ReleaseNode(node);
        }
    }

    /// <summary>为具体父节点登记全部子生成规则，不为队列创建时钟。</summary>
    /// <param name="parent">实际父VNode；根规则为空。</param>
    /// <param name="parentId">父VNodeQueue标识或Emitter。</param>
    internal void AttachChildren(VNode? parent, string parentId)
    {
        if (!_children.TryGetValue(parentId, out var definitions)) return;
        // 每个闭包捕获当前父实例；重复批次不会覆盖既有绑定。
        VTimeline clock = parent?.Timeline ?? Timeline!;
        int parentIndex = parent?.BirthIndex ?? 0;
        foreach (var definition in definitions)
            foreach (var rule in definition.Timeline)
                rule.Register(clock, parentIndex, () =>
                {
                    if (Timeline is null || (parent is not null && !parent.IsAlive)) return;
                    Node2D? reference = parent is not null ? parent : Core.RefObject == "Boss" ? _owner : null;
                    Vector2 source = parent is not null ? parent.WorldPosition : reference?.GlobalPosition ?? Vector2.Zero;
                    VNodeQueue batch = definition.CreateInstance(source);
                    batch.BindReference(reference, parent);
                    batch.Emit(this, Manager!);
                });
    }

    /// <summary>生成纯节点并登记独立时间线，不参与子弹容量或碰撞。</summary>
    /// <param name="position">全局逻辑像素位置。</param>
    /// <param name="move">已求值的运动参数。</param>
    /// <param name="lifeTimeS">有限正数寿命秒数或持久节点的null。</param>
    /// <param name="sameAngle">是否沿外部Angle加速。</param>
    /// <returns>已登记的节点。</returns>
    internal VNode AddNode(Vector2 position, BulletMoveAttribute move, double? lifeTimeS, bool sameAngle)
    {
        if (Timeline is null || Manager is null) throw new InvalidOperationException("纯节点只能由活动Emitter生成。");
        // 场景容器独立于逻辑父子关系，避免级联误删子弹。
        if (_nodeContainer is null)
        {
            _nodeContainer = new Node2D { Name = "VNodes" };
            Manager.GetParent().AddChild(_nodeContainer);
        }
        VNode.ValidateMotion(position, move, lifeTimeS);
        var node = new VNode();
        try
        {
            node.ConfigureMotion(position, move, lifeTimeS, sameAngle);
            node.Emitter = this;
            node.Timeline = GlobalEvent.CreateTimeline(node);
            node.SpawnPosition = node.Position = _nodeContainer.ToLocal(position);
            _nodeContainer.AddChild(node);
            _nodes.Add(node);
            return node;
        }
        catch { node.Deactivate(); node.Free(); throw; }
    }

    /// <summary>释放节点及其逻辑后代，不清除任何已出生子弹。</summary>
    /// <param name="node">本发射器拥有的纯VNode。</param>
    /// <param name="free">是否移出场景并释放；外部退出通知使用false。</param>
    internal void ReleaseNode(VNode node, bool free = true)
    {
        if (!_nodes.Contains(node)) return;
        // 即便Snapshot也按逻辑父引用结束后代。
        foreach (var child in _nodes.Where(child => ReferenceEquals(child.ParentVNode, node)).ToArray()) ReleaseNode(child);
        foreach (var bullet in _bullets.Where(bullet => ReferenceEquals(bullet.ParentVNode, node))) bullet.DetachReference();
        node.Deactivate();
        _nodes.Remove(node);
        node.Emitter = null;
        if (free)
        {
            node.GetParent()?.RemoveChild(node);
            node.QueueFree();
        }
    }

    /// <summary>统一代码子弹生成入口。</summary>
    /// <param name="manager">本场管理器。</param>
    /// <param name="settings">完整出生参数，位置为全局逻辑像素。</param>
    /// <returns>成功生成的子弹，满额为空。</returns>
    protected Bullet? AddBullet(BulletManager manager, BulletDefaultSet settings)
    {
        Bullet? bullet = manager.Spawn(settings, this);
        if (bullet is not null) _bullets.Add(bullet);
        return bullet;
    }
    /// <summary>由代码时间线生成一个子弹队列。</summary>
    /// <param name="manager">本场管理器。</param>
    /// <param name="queue">独立子弹批次。</param>
    protected void AddQueue(BulletManager manager, BulletQueue queue) => queue.Emit(this, manager);
    /// <summary>提供队列内部的子弹登记入口。</summary>
    /// <param name="manager">本场管理器。</param>
    /// <param name="settings">完整出生参数。</param>
    /// <returns>成功生成的子弹或满额的null。</returns>
    internal Bullet? AddQueueBullet(BulletManager manager, BulletDefaultSet settings) => AddBullet(manager, settings);
    /// <summary>管理器释放子弹时注销发射器成员。</summary>
    /// <param name="bullet">已经释放的子弹。</param>
    internal void Unregister(Bullet bullet) => _bullets.Remove(bullet);

    /// <summary>验证树引用、共通参数及所有派生时间边界。</summary>
    private void ValidateData()
    {
        if (string.IsNullOrWhiteSpace(Core.Id) || Core.Version != 2 || Core.Damage <= 0 || !Enum.IsDefined(Core.Team)
            || Core.RefObject is not (null or "Boss") || Core.StopMode is not ("KeepBullets" or "ClearBullets"))
            throw new JsonException("Emitter.Core参数无效。");
        // 字典只用于查找；登记顺序始终来自原始数组。
        VNodeQueue[] definitions = VNodes.Concat<VNodeQueue>(BulletQueues).ToArray();
        var ids = new Dictionary<string, VNodeQueue>(StringComparer.Ordinal);
        foreach (var definition in definitions)
            if (!ids.TryAdd(definition.Core.Id, definition)) throw new JsonException("重复队列Id：" + definition.Core.Id);
        _children.Clear();
        foreach (var definition in definitions)
        {
            string parentId = definition.PositionAttributes.RefObject;
            int parentAmount = 1;
            if (parentId != "Emitter")
            {
                if (!ids.TryGetValue(parentId, out var parent) || parent is BulletQueue)
                    throw new JsonException(definition.Core.Id + ": RefObject必须引用Emitter或本地VNodeQueue。");
                parentAmount = parent.Core.Amount;
            }
            // 沿祖先链查环，禁止自引用与多节点循环。
            var ancestors = new HashSet<string>(StringComparer.Ordinal) { definition.Core.Id };
            string cursor = parentId;
            while (cursor != "Emitter")
            {
                if (!ancestors.Add(cursor)) throw new JsonException("VNode引用存在循环：" + cursor);
                if (!ids.TryGetValue(cursor, out var ancestor) || ancestor is BulletQueue) throw new JsonException("无效祖先引用：" + cursor);
                cursor = ancestor.PositionAttributes.RefObject;
            }
            definition.ValidateSchedules(parentAmount);
            if (!_children.TryGetValue(parentId, out var children)) _children.Add(parentId, children = new List<VNodeQueue>());
            children.Add(definition);
        }
    }
}
