using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>按整数逻辑时间稳定调度计时器，并管理目标生命周期关联。</summary>
public sealed class VTimerProcessor
{
    // 毫秒、秒及60Hz固定步所对应的内部时间单位。
    public const long UnitsPerMillisecond = 60, UnitsPerSecond = 60000, FixedStepUnits = 1000;
    // 防止同一时刻的零延迟递归无限执行。
    private const int MaxActionsPerTimestamp = 10000;
    // 活动计时器、目标反向索引与离场事件处理器。
    private readonly List<VTimer> _timers = new();
    private readonly Dictionary<Node2D, HashSet<VTimer>> _targets = new();
    private readonly Dictionary<Node2D, Action> _exitHandlers = new();
    // 弱关联逻辑死亡标记，避免长战斗累计所有历史弹幕编号或引用。
    private readonly ConditionalWeakTable<Node2D, object> _dead = new();
    // 注册顺序、清场代次及重入保护。
    private long _sequence, _generation;
    private bool _advancing, _faulted;
    /// <summary>当前整数逻辑时刻，每毫秒60单位。</summary>
    public long NowUnits { get; private set; }
    /// <summary>当前逻辑毫秒，允许物理步边界的小数部分。</summary>
    public double NowMs => NowUnits / (double)UnitsPerMillisecond;
    /// <summary>尚未完成的计时器数量。</summary>
    public int ActiveCount => _timers.Count;
    /// <summary>把秒配置按最近整数毫秒换算，中点远离零。</summary>
    /// <param name="seconds">非负有限秒数。</param>
    /// <returns>整数毫秒；超出范围时报错。</returns>
    public static long SecondsToMilliseconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        return checked((long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));
    }
    /// <summary>注册计时器，复制目标并监听离场；不在此方法内执行回调。</summary>
    /// <param name="timer">尚未注册的计时器。</param>
    /// <returns>同一个计时器，便于保存取消句柄。</returns>
    public VTimer Register(VTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        if (_faulted) throw new InvalidOperationException("调度异常后须清场重置。");
        timer.Attach(this, checked(++_sequence));
        _timers.Add(timer);
        // 每个目标对应共享反向索引，首个关联负责订阅离场事件。
        foreach (var node in timer.Targets)
        {
            if (!_targets.TryGetValue(node, out var timers))
            {
                timers = new HashSet<VTimer>();
                _targets.Add(node, timers);
                // 回调仅通知此节点，不从渲染回调消耗逻辑时间。
                Action handler = () => NotifyTargetDestroyed(node);
                _exitHandlers.Add(node, handler);
                node.TreeExiting += handler;
            }
            timers.Add(timer);
        }
        if (timer.RequiresTargets && timer.Targets.Count == 0) timer.Cancel();
        return timer;
    }
    /// <summary>检查节点是否仍属于可执行动作的战场实体。</summary>
    /// <param name="node">待检查目标。</param>
    /// <returns>实例有效、未待释放、在树内且未逻辑注销时为真。</returns>
    internal bool IsAlive(Node2D node) => GodotObject.IsInstanceValid(node)
        && !node.IsQueuedForDeletion() && node.IsInsideTree() && !_dead.TryGetValue(node, out _);
    /// <summary>即时注销逻辑死亡或离场目标，最后一个目标失效时取消计时器。</summary>
    /// <param name="node">已死亡或即将释放的目标。</param>
    public void NotifyTargetDestroyed(Node2D node)
    {
        if (GodotObject.IsInstanceValid(node)) _dead.GetValue(node, _ => new object());
        if (!_targets.TryGetValue(node, out var timers)) return;
        // 副本允许取消过程中同步移除原集合成员。
        foreach (var timer in timers.ToArray())
        {
            RemoveTarget(timer, node);
            if (timer.Targets.Count == 0) timer.Cancel();
        }
    }
    /// <summary>从单个计时器移除失效成员。</summary>
    /// <param name="timer">待检查计时器。</param>
    internal void Prune(VTimer timer)
    {
        // 节点队列释放尚未真正出树时也要从快照移除。
        foreach (var node in timer.Targets.ToArray())
            if (!IsAlive(node)) RemoveTarget(timer, node);
        if (timer.RequiresTargets && timer.Targets.Count == 0) timer.Cancel();
    }
    /// <summary>解除一个目标关联，最后一个引用解除时移除事件订阅。</summary>
    /// <param name="timer">关联计时器。</param>
    /// <param name="node">关联目标。</param>
    private void RemoveTarget(VTimer timer, Node2D node)
    {
        timer.Targets.Remove(node);
        // 反向索引可能已被同刻其他注销清除。
        if (!_targets.TryGetValue(node, out var timers)) return;
        timers.Remove(timer);
        if (timers.Count != 0) return;
        if (_exitHandlers.Remove(node, out var handler) && GodotObject.IsInstanceValid(node)) node.TreeExiting -= handler;
        _targets.Remove(node);
    }
    /// <summary>从处理器完全解除一个终止计时器。</summary>
    /// <param name="timer">已终止计时器。</param>
    internal void Detach(VTimer timer)
    {
        _timers.Remove(timer);
        // 逐项取消目标订阅，避免已终止委托被节点事件保留。
        foreach (var node in timer.Targets.ToArray()) RemoveTarget(timer, node);
    }
    /// <summary>按事件分段推进逻辑时间，同刻动作按注册序号执行。</summary>
    /// <param name="units">非负整数时间增量，每毫秒60单位；0执行当前时刻动作。</param>
    /// <param name="advanceSegment">可选运动与碰撞推进回调，接收秒数；调用前时钟已到段终点。</param>
    public void AdvanceByUnits(long units, Action<double>? advanceSegment = null)
    {
        if (units < 0) throw new ArgumentOutOfRangeException(nameof(units));
        if (_advancing || _faulted) throw new InvalidOperationException("禁止重入或继续推进异常调度器。");
        // 终点及代次固定，回调清场后不继续执行旧时间线。
        long end = checked(NowUnits + units), generation = _generation;
        // 同刻累计执行数，跨越事件时刻后重新计数。
        long countTime = NowUnits;
        int count = 0;
        _advancing = true;
        try
        {
            while (generation == _generation)
            {
                // 每轮重新筛选目标与最早事件，兼容回调的新增和取消。
                foreach (var timer in _timers.ToArray()) Prune(timer);
                var next = _timers.OrderBy(timer => timer.Due).ThenBy(timer => timer.Sequence).FirstOrDefault();
                long boundary = next is null ? end : Math.Min(end, next.Due);
                if (boundary > NowUnits)
                {
                    // 仅在运动计算入口把精确时间差换算为秒。
                    double seconds = (boundary - NowUnits) / (double)UnitsPerSecond;
                    NowUnits = boundary;
                    advanceSegment?.Invoke(seconds);
                    // 碰撞可能取消计时器、结束战斗或建立新的无敌计时器，重新选择。
                    continue;
                }
                if (next is null || next.Due > end) break;
                if (countTime != NowUnits) { countTime = NowUnits; count = 0; }
                if (++count > MaxActionsPerTimestamp) throw new InvalidOperationException("同刻计时动作超过上限，可能存在零延迟递归。");
                if (next.FinishAtEnd) next.Finish(VTimerState.Completed);
                else next.Fire();
            }
        }
        catch
        {
            _faulted = true;
            throw;
        }
        finally { _advancing = false; }
    }
    /// <summary>取消全部计时器并解除关联；可选重置战斗时钟和注册序号。</summary>
    /// <param name="resetClock">重开时为true；结束战斗时保留时间，默认false。</param>
    public void Clear(bool resetClock = false)
    {
        // 副本允许Cancel同步解除原活动列表与目标事件。
        foreach (var timer in _timers.ToArray()) timer.Cancel();
        _dead.Clear();
        _generation++;
        if (resetClock) { NowUnits = 0; _sequence = 0; _faulted = false; }
    }
}
