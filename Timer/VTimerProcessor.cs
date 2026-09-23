using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>管理战斗逻辑时钟、独立适配器与实体时间线，并在步末统一派发动作。</summary>
public sealed class VTimerProcessor
{
    /// <summary>每毫秒、每秒及每个60Hz固定步对应的整数时间单位。</summary>
    public const long UnitsPerMillisecond = 60, UnitsPerSecond = 60000, FixedStepUnits = 1000;
    // 防止同一次派发中零延迟动作无限生成。
    private const int MaxActionsPerTimestamp = 10000;
    // 时间线只在所有者中保存动作；适配器的目标由此反向索引。
    private readonly List<IVTimelineOwner> _timelineOwners = new();
    private readonly Dictionary<Node2D, HashSet<VTimelineAdapter>> _targets = new();
    private readonly Dictionary<Node2D, Action> _exitHandlers = new();
    // 弱关联逻辑死亡标记不持有历史弹幕。
    private readonly ConditionalWeakTable<Node2D, object> _dead = new();
    // 全场稳定序号、清场代次及重入保护。
    private long _sequence, _generation;
    private bool _advancing, _faulted;

    /// <summary>当前战斗逻辑时刻，单位为整数时间单位。</summary>
    public long NowUnits { get; private set; }
    /// <summary>当前战斗逻辑时刻，单位为毫秒。</summary>
    public double NowMs => NowUnits / (double)UnitsPerMillisecond;
    /// <summary>所有已登记时间线中尚未完成的动作数量。</summary>
    public int TimelineActionCount => _timelineOwners.Sum(owner => owner.Timeline?.ActionCount ?? 0);

    /// <summary>为时间线动作分配全场稳定序号。</summary>
    /// <returns>唯一递增的序号。</returns>
    internal long NextSequence()
    {
        if (_faulted) throw new InvalidOperationException("调度异常后须清场重置。");
        return checked(++_sequence);
    }

    /// <summary>登记一个持有独立年龄的所有者。</summary>
    /// <param name="owner">新激活的时间线所有者。</param>
    internal void RegisterTimelineOwner(IVTimelineOwner owner)
    {
        if (_faulted) throw new InvalidOperationException("调度异常后须清场重置。");
        if (_timelineOwners.Contains(owner)) throw new InvalidOperationException("实体时间线已登记。");
        _timelineOwners.Add(owner);
    }

    /// <summary>所有者取消时间线时解除登记及适配器目标订阅。</summary>
    /// <param name="owner">已取消时间线的所有者。</param>
    internal void UnregisterTimelineOwner(IVTimelineOwner owner)
    {
        _timelineOwners.Remove(owner);
        if (owner is VTimelineAdapter adapter) DetachAdapter(adapter);
    }

    /// <summary>将非负秒数按中点远离零换算为整数毫秒。</summary>
    /// <param name="seconds">非负有限秒数。</param>
    /// <returns>整数毫秒。</returns>
    public static long SecondsToMilliseconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        return checked((long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));
    }

    /// <summary>在适配器创建时固定目标快照并监听离场。</summary>
    /// <param name="adapter">待绑定的独立时间线适配器。</param>
    /// <param name="source">原始目标集合；空引用表示无需绑定目标。</param>
    internal void AttachAdapter(VTimelineAdapter adapter, IEnumerable<Node2D>? source)
    {
        if (source is null) return;
        // 先完成可失败的枚举，再变更订阅和索引。
        var snapshot = new List<Node2D>();
        foreach (var node in source)
            if (node is not null && !snapshot.Contains(node) && IsAlive(node)) snapshot.Add(node);
        adapter.Targets.AddRange(snapshot);
        foreach (var node in snapshot)
        {
            if (!_targets.TryGetValue(node, out var adapters))
            {
                adapters = new HashSet<VTimelineAdapter>();
                _targets.Add(node, adapters);
                Action handler = () => NotifyTargetDestroyed(node);
                _exitHandlers.Add(node, handler);
                node.TreeExiting += handler;
            }
            adapters.Add(adapter);
        }
        if (snapshot.Count == 0) adapter.Cancel();
    }

    /// <summary>判断绑定目标是否仍属于可执行动作的场景实体。</summary>
    /// <param name="node">待检查节点。</param>
    /// <returns>有效、未待释放、在树内且未逻辑注销时为真。</returns>
    internal bool IsAlive(Node2D node) => GodotObject.IsInstanceValid(node)
        && !node.IsQueuedForDeletion() && node.IsInsideTree() && !_dead.TryGetValue(node, out _);

    /// <summary>即时注销目标，最后一个目标失效时取消对应适配器。</summary>
    /// <param name="node">已逻辑死亡或即将离场的节点。</param>
    public void NotifyTargetDestroyed(Node2D node)
    {
        if (GodotObject.IsInstanceValid(node)) _dead.GetValue(node, _ => new object());
        if (!_targets.TryGetValue(node, out var adapters)) return;
        foreach (var adapter in adapters.ToArray()) RemoveTarget(adapter, node);
    }

    /// <summary>每次派发前剔除排队释放或已经离场的目标。</summary>
    /// <param name="adapter">待检查的独立适配器。</param>
    internal void Prune(VTimelineAdapter adapter)
    {
        foreach (var node in adapter.Targets.ToArray())
            if (!IsAlive(node)) RemoveTarget(adapter, node);
    }

    /// <summary>从一个适配器移除失效目标及对应的反向索引。</summary>
    /// <param name="adapter">目标所属适配器。</param>
    /// <param name="node">失效节点。</param>
    private void RemoveTarget(VTimelineAdapter adapter, Node2D node)
    {
        adapter.Targets.Remove(node);
        if (_targets.TryGetValue(node, out var adapters))
        {
            adapters.Remove(adapter);
            if (adapters.Count == 0)
            {
                if (_exitHandlers.Remove(node, out var handler) && GodotObject.IsInstanceValid(node)) node.TreeExiting -= handler;
                _targets.Remove(node);
            }
        }
        if (adapter.RequiresTargets && adapter.Targets.Count == 0) adapter.Cancel();
    }

    /// <summary>适配器结束时解除全部目标订阅。</summary>
    /// <param name="adapter">已经结束的适配器。</param>
    private void DetachAdapter(VTimelineAdapter adapter)
    {
        foreach (var node in adapter.Targets.ToArray()) RemoveTarget(adapter, node);
    }

    /// <summary>推进逻辑时钟和运动碰撞，并按原定时间与登记序号派发到期动作。</summary>
    /// <param name="units">非负整数时间增量；0会派发当前时刻的动作。</param>
    /// <param name="advanceSegment">可选运动碰撞回调，接收本次完整秒数。</param>
    public void AdvanceByUnits(long units, Action<double>? advanceSegment = null)
    {
        if (units < 0) throw new ArgumentOutOfRangeException(nameof(units));
        if (_advancing || _faulted) throw new InvalidOperationException("禁止重入或继续推进异常调度器。");
        // 只给本步开始前存在的适配器计龄；步内新建者从步末零龄开始。
        var adapters = _timelineOwners.OfType<VTimelineAdapter>().ToArray();
        long end = checked(NowUnits + units), generation = _generation;
        int count = 0;
        _advancing = true;
        try
        {
            NowUnits = end;
            foreach (var adapter in adapters) adapter.Timeline?.AdvanceUnits(units);
            if (units > 0) advanceSegment?.Invoke(units / (double)UnitsPerSecond);
            while (generation == _generation)
            {
                // 目标可能由前一个同刻回调释放；每轮都重新扫描。
                foreach (var adapter in _timelineOwners.OfType<VTimelineAdapter>().ToArray()) Prune(adapter);
                VTimelineEvent? nextAction = null;
                long actionDue = long.MaxValue;
                foreach (var owner in _timelineOwners.ToArray())
                {
                    var timeline = owner.Timeline;
                    var candidate = timeline?.NextDue();
                    if (candidate is null) continue;
                    long candidateDue = timeline!.AbsoluteDue(candidate.Due);
                    if (candidateDue > end) continue;
                    if (nextAction is null || candidateDue < actionDue || (candidateDue == actionDue && candidate.Sequence < nextAction.Sequence))
                    { nextAction = candidate; actionDue = candidateDue; }
                }
                if (nextAction is null) break;
                if (++count > MaxActionsPerTimestamp) throw new InvalidOperationException("同刻计时动作超过上限，可能存在零延迟递归。");
                nextAction.Fire();
            }
            // 已自然完成的独立适配器不再占用所有者列表或节点订阅。
            if (generation == _generation)
                foreach (var adapter in _timelineOwners.OfType<VTimelineAdapter>().ToArray())
                    if (adapter.Timeline?.ActionCount == 0) adapter.Cancel();
        }
        catch
        {
            _faulted = true;
            throw;
        }
        finally { _advancing = false; }
    }

    /// <summary>取消所有时间线；可选重置战斗时钟和序号。</summary>
    /// <param name="resetClock">为真时重置逻辑时钟与异常状态。</param>
    public void Clear(bool resetClock = false)
    {
        foreach (var owner in _timelineOwners.ToArray())
        {
            if (owner is VTimelineAdapter adapter) adapter.Cancel();
            else owner.Timeline?.Cancel();
        }
        _timelineOwners.Clear();
        _dead.Clear();
        _generation++;
        if (resetClock) { NowUnits = 0; _sequence = 0; _faulted = false; }
    }
}
