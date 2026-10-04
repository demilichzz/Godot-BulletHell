using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>子弹所属阵营，用于筛选伤害目标。</summary>
public enum VBulletTeam
{
    // Boss发出的敌弹。
    Enemy,
    // 玩家发出的攻击弹。
    Player
}

/// <summary>拥有一棵Creator树，控制共通参数、生成生命周期和名称查找。</summary>
public sealed class VBulletEmitter : IVTimelineOwner
{
    /// <summary>发射器共通配置。</summary>
    public EmitterCoreAttribute Core { get; private set; } = new();
    /// <summary>唯一根生成器，允许为VBulletCreator。</summary>
    public VNodeCreator Root { get; private set; } = new();
    /// <summary>活动期间用于根生成规则的本地时间线。</summary>
    public VTimeline? Timeline { get; private set; }
    /// <summary>所有仍存活的纯节点，只读投影，不另存成员。</summary>
    public IReadOnlyList<VNode> Nodes { get; }
    /// <summary>所属子弹的只读投影，包括代码直接创建的子弹。</summary>
    public IReadOnlyList<VBullet> Bullets { get; }
    /// <summary>负责登记、容量与碰撞的本场管理器。</summary>
    internal VBulletManager? Manager { get; private set; }
    // 两类查找标识允许交叉重名；仅作查找，不决定更新顺序。
    private Dictionary<string, VNodeCreator> _ids = new(StringComparer.Ordinal);
    private Dictionary<string, VNodeCreator> _names = new(StringComparer.Ordinal);
    private BossController? _owner;
    private Node2D? _nodeContainer;
    private bool _started;

    /// <summary>建立引用唯一运行来源的只读视图。</summary>
    public VBulletEmitter()
    {
        (_ids, _names) = Root.InitializeTree();
        Nodes = new VNodeCreator.MemberView<VNode>(() => Root.EnumerateMembers(true).Where(node => node is not VBullet));
        Bullets = new VNodeCreator.MemberView<VBullet>(() => Manager is null ? Array.Empty<VNode>()
            : Manager.ActiveBullets.Where(bullet => ReferenceEquals(bullet.Emitter, this)));
    }

    /// <summary>通过ID优先、Name次之查找当前树，区分大小写。</summary>
    /// <param name="idOrName">完整ID或非空名称，不进行模糊匹配。</param>
    /// <returns>匹配的Creator；空输入或没有匹配时为空。</returns>
    public VNodeCreator? GetCreator(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        return _ids.TryGetValue(idOrName, out var creator) ? creator
            : _names.TryGetValue(idOrName, out creator) ? creator : null;
    }

    /// <summary>加载当前JSON结构，不接受版本字段或旧平铺数组。</summary>
    /// <param name="path">Godot资源路径。</param>
    /// <returns>未启动且拥有独立树的发射器。</returns>
    public static VBulletEmitter Load(string path) => FromJson(JsonData.ReadFile(path), path);

    /// <summary>解析单根树并建立只读身份，不消耗业务随机。</summary>
    /// <param name="json">当前格式JSON。</param>
    /// <param name="sourceName">错误消息中的来源。</param>
    /// <returns>校验完成的独立发射器。</returns>
    public static VBulletEmitter FromJson(string json, string sourceName = "内存")
        => JsonData.Parse(json, sourceName, element =>
        {
            JsonData.CheckFields(element, new[] { "Core", "VNodes" });
            var emitter = new VBulletEmitter
            {
                Core = JsonData.Read<EmitterCoreAttribute>(JsonData.Required(element, "Core")),
                Root = VNodeCreator.ReadCreator(JsonData.Required(element, "VNodes"))
            };
            if (emitter.Core.Damage <= 0 || !Enum.IsDefined(emitter.Core.Team)
                || emitter.Core.RefObject is not (null or "Boss") || emitter.Core.StopMode is not ("KeepBullets" or "ClearBullets"))
                throw new JsonException("Emitter.Core参数无效。");
            (emitter._ids, emitter._names) = emitter.Root.InitializeTree();
            return emitter;
        });

    /// <summary>激活本地时钟并登记运行树，同一实例只能启动一次。</summary>
    /// <param name="owner">生命周期所属Boss。</param>
    /// <param name="manager">当前战斗的子弹管理器。</param>
    public void Start(BossController owner, VBulletManager manager)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(manager);
        if (_started) throw new InvalidOperationException("Emitter实例不能重复启动。");
        _started = true;
        _owner = owner;
        Manager = manager;
        Timeline = GlobalEvent.CreateTimeline(this);
        manager.RegisterEmitter(this);
        try { BindGeneration(Root, null); }
        catch { Stop(); throw; }
    }

    /// <summary>登记某个实际父对象的直接子生成规则。</summary>
    /// <param name="parent">已出生的实际父对象。</param>
    /// <param name="creator">产生父对象的Creator。</param>
    internal void AttachChildren(VNode parent, VNodeCreator creator)
    {
        foreach (var child in creator.Children) BindGeneration(child, parent);
    }

    /// <summary>将生成规则绑定到实际父对象，绝不保存最新批次引用。</summary>
    /// <param name="creator">将要生成的定义。</param>
    /// <param name="parent">根规则为空，其他规则为实际父对象。</param>
    private void BindGeneration(VNodeCreator creator, VNode? parent)
    {
        // 同一个Creator可绑定多个父实例，闭包保存各自身份。
        var timeline = parent?.Timeline ?? Timeline!;
        foreach (var rule in creator.Timeline)
        {
            // 固定时刻数组共享此回调，取消时整体移除。
            Action generate = () =>
            {
                if (Timeline is null || parent is { IsAlive: false } || parent is { CanGenerate: false }) return;
                Node2D? reference = parent is not null ? parent : Core.RefObject == "Boss" ? _owner : null;
                var source = parent?.WorldPosition ?? reference?.GlobalPosition ?? Vector2.Zero;
                creator.Emit(this, Manager!, reference, parent, source);
            };
            rule.Register(timeline, parent?.BirthIndex ?? 0, generate);
            parent?.TrackGeneration(generate);
        }
    }

    /// <summary>只推进Emitter自己的年龄；成员由Manager按树统一推进。</summary>
    /// <param name="units">非负整数逻辑时间单位。</param>
    internal void AdvanceUnits(long units) => Timeline?.AdvanceUnits(units);

    /// <summary>创建无显示、无碰撞且不占容量的实际节点。</summary>
    /// <param name="position">世界逻辑像素位置。</param>
    /// <param name="move">求值后的弧度、速度及加速度。</param>
    /// <param name="lifeTimeMs">总寿命整数毫秒数，空表示父对象存活期间有效。</param>
    /// <param name="sameAngle">是否沿外部方向加速。</param>
    /// <returns>已登记出生动作的节点。</returns>
    internal VNode AddNode(Vector2 position, VNodeMoveAttribute move, long? lifeTimeMs, bool sameAngle)
    {
        if (Timeline is null || Manager is null) throw new InvalidOperationException("只能在活动Emitter生成节点。");
        if (_nodeContainer is null)
        {
            _nodeContainer = new Node2D { Name = "VNodes" };
            Manager.GetParent().AddChild(_nodeContainer);
        }
        VNode.ValidateMotion(position, move, lifeTimeMs);
        var node = new VNode();
        try
        {
            node.ConfigureMotion(position, move, lifeTimeMs, sameAngle);
            node.Emitter = this;
            node.Timeline = GlobalEvent.CreateTimeline(node);
            node.SpawnPosition = node.Position = _nodeContainer.ToLocal(position);
            _nodeContainer.AddChild(node);
            Manager.RegisterBirth(node);
            return node;
        }
        catch { node.Deactivate(); node.Free(); throw; }
    }

    /// <summary>结束实际父对象的后代节点和生成，保留后代子弹的成员动作。</summary>
    /// <param name="parent">刚结束的实际父对象，允许为VBullet。</param>
    internal void ReleaseDescendants(VNode parent)
    {
        if (parent.Creator is null) return;
        foreach (var creator in parent.Creator.Children)
        {
            // 释放会改变批次，快照仅用于生命周期变更，不用于每步扫描。
            foreach (var child in creator.EnumerateMembers(false).Where(node => ReferenceEquals(node.ParentVNode, parent)).ToArray())
            {
                if (child is VBullet)
                {
                    ReleaseDescendants(child);
                    child.CancelGeneration();
                    child.DetachReference();
                    child.ParentVNode = null;
                }
                else ReleaseNode(child);
            }
        }
    }

    /// <summary>注销纯VNode及后代生成。</summary>
    /// <param name="node">本Emitter的纯节点。</param>
    /// <param name="free">外部场景退出时为假，避免重复释放。</param>
    internal void ReleaseNode(VNode node, bool free = true)
    {
        if (!node.IsAlive || !ReferenceEquals(node.Emitter, this)) return;
        ReleaseDescendants(node);
        node.Deactivate();
        node.Emitter = null;
        if (free)
        {
            node.GetParent()?.RemoveChild(node);
            node.QueueFree();
        }
    }

    /// <summary>停止未来生成，结束节点，并应用已发子弹保留策略。</summary>
    public void Stop()
    {
        Timeline?.Cancel();
        Timeline = null;
        foreach (var bullet in Bullets) { bullet.CancelGeneration(); bullet.DetachReference(); bullet.ParentVNode = null; }
        foreach (var node in Nodes.ToArray()) ReleaseNode(node);
        if (_nodeContainer is not null && GodotObject.IsInstanceValid(_nodeContainer)) _nodeContainer.QueueFree();
        _nodeContainer = null;
        if (Core.StopMode == "ClearBullets" && Manager is not null && GodotObject.IsInstanceValid(Manager)) Manager.ClearEmitter(this);
    }

    /// <summary>全场结束或遗留子弹耗尽后解除查找索引。</summary>
    internal void Retire() { _ids.Clear(); _names.Clear(); _owner = null; }

    /// <summary>Creator的子弹创建入口。</summary>
    /// <param name="manager">本场子弹管理器。</param>
    /// <param name="settings">求值后的完整出生参数。</param>
    /// <returns>成功生成的子弹，满额为空。</returns>
    internal VBullet? AddCreatorBullet(VBulletManager manager, VBulletDefaultSet settings) => manager.Spawn(settings, this);
}
