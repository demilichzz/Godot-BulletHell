using Godot;
using System;
using System.Collections.Generic;

/// <summary>定义单次、有限重复及永久重复计时器。</summary>
public enum VTimerType
{
    // 在首次时间执行一次。
    Once,
    // 在包含终点的有限时间内重复。
    Repeat,
    // 重复执行直至取消或目标失效。
    RepeatForever
}
/// <summary>记录计时器从创建到完成或取消的状态。</summary>
public enum VTimerState
{
    // 尚未注册。
    Created,
    // 已注册并等待调度。
    Running,
    // 已自然结束。
    Completed,
    // 已主动或自动取消。
    Cancelled
}
/// <summary>保存毫秒配置和固定目标，向回调提供仍存活的节点。</summary>
public sealed class VTimer
{
    // 注册前的目标来源与行为，终止后清除引用。
    private IEnumerable<Node2D>? _source;
    private Action<IReadOnlyList<Node2D>>? _action;
    // 注册时固定的目标成员，按原始顺序排列。
    internal readonly List<Node2D> Targets = new();
    // 所属处理器、绝对触发时刻、截止时刻及稳定注册序号。
    internal VTimerProcessor? Processor;
    internal long Next, End, Sequence;
    /// <summary>首次执行偏移，单位为非负整数毫秒。</summary>
    public long StartTimeMs { get; }
    /// <summary>循环间隔，单位为整数毫秒；重复类型须为正数。</summary>
    public long IntervalMs { get; }
    /// <summary>包含终点的截止偏移，单位为整数毫秒；仅有限重复使用。</summary>
    public long EndTimeMs { get; }
    /// <summary>执行类型。</summary>
    public VTimerType Type { get; }
    /// <summary>当前生命周期状态。</summary>
    public VTimerState State { get; private set; }
    /// <summary>是否已完成或取消。</summary>
    public bool IsFinished => State is VTimerState.Completed or VTimerState.Cancelled;
    /// <summary>距下一动作或截止点的剩余毫秒，未注册或结束时为0。</summary>
    public double RemainingMs => State == VTimerState.Running
        ? Math.Max(0, Due - Processor!.NowUnits) / (double)VTimerProcessor.UnitsPerMillisecond : 0;
    /// <summary>下一调度点，单位为内部整数时间单位。</summary>
    internal long Due => Type == VTimerType.Repeat ? Math.Min(Next, End) : Next;
    /// <summary>定义行为与时间参数，注册后才开始计时。</summary>
    /// <param name="startTimeMs">首次执行偏移，非负毫秒，0在当前调度轮执行。</param>
    /// <param name="intervalMs">重复间隔，毫秒；单次忽略并填写0。</param>
    /// <param name="endTimeMs">有限重复截止偏移，毫秒且不早于起始；其他类型忽略并填写0。</param>
    /// <param name="type">单次、有限重复或永久重复。</param>
    /// <param name="targetList">注册时复制的目标，回调只收到其中存活成员。</param>
    /// <param name="action">到期执行的行为，无存活目标时不调用。</param>
    public VTimer(long startTimeMs, long intervalMs, long endTimeMs, VTimerType type,
        IEnumerable<Node2D> targetList, Action<IReadOnlyList<Node2D>> action)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (startTimeMs < 0 || intervalMs < 0 || endTimeMs < 0) throw new ArgumentOutOfRangeException(nameof(startTimeMs));
        if (type != VTimerType.Once && intervalMs == 0) throw new ArgumentOutOfRangeException(nameof(intervalMs));
        if (type == VTimerType.Repeat && endTimeMs < startTimeMs) throw new ArgumentOutOfRangeException(nameof(endTimeMs));
        // 提前验证毫秒换算，避免注册后才出现部分初始化。
        _ = checked(startTimeMs * VTimerProcessor.UnitsPerMillisecond);
        _ = checked(intervalMs * VTimerProcessor.UnitsPerMillisecond);
        _ = checked(endTimeMs * VTimerProcessor.UnitsPerMillisecond);
        StartTimeMs = startTimeMs; IntervalMs = intervalMs; EndTimeMs = endTimeMs; Type = type;
        _source = targetList ?? throw new ArgumentNullException(nameof(targetList));
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }
    /// <summary>幂等取消，立即禁止后续回调并解除引用。</summary>
    public void Cancel() => Finish(VTimerState.Cancelled);
    /// <summary>注册到处理器并固定目标快照。</summary>
    /// <param name="processor">本次战斗的处理器。</param>
    /// <param name="sequence">递增注册序号。</param>
    internal void Attach(VTimerProcessor processor, long sequence)
    {
        if (State != VTimerState.Created) throw new InvalidOperationException("计时器只能注册一次。");
        // 先计算绝对时刻和目标副本，失败时不改变状态。
        long next = checked(processor.NowUnits + StartTimeMs * VTimerProcessor.UnitsPerMillisecond);
        long end = Type == VTimerType.Repeat
            ? checked(processor.NowUnits + EndTimeMs * VTimerProcessor.UnitsPerMillisecond) : long.MaxValue;
        var snapshot = new List<Node2D>();
        // 保持来源成员顺序，同一节点只绑定一次。
        foreach (var node in _source!)
            if (node is not null && !snapshot.Contains(node) && processor.IsAlive(node)) snapshot.Add(node);
        Targets.AddRange(snapshot);
        _source = null;
        Processor = processor; Sequence = sequence; Next = next; End = end;
        State = VTimerState.Running;
    }
    /// <summary>执行当前到期动作并计算下一时刻。</summary>
    internal void Fire()
    {
        if (State != VTimerState.Running) return;
        Processor!.Prune(this);
        if (IsFinished) return;
        if (Next > End) { Finish(VTimerState.Completed); return; }
        // 回调期间可以取消自身；只读快照不受目标注销修改影响。
        var action = _action!;
        action(Targets.ToArray());
        if (State != VTimerState.Running) return;
        if (Type == VTimerType.Once || (Type == VTimerType.Repeat && Next == End))
        {
            Finish(VTimerState.Completed);
            return;
        }
        // 有限重复在下一周期超出截止点时只调度截止，不制造额外动作。
        long interval = checked(IntervalMs * VTimerProcessor.UnitsPerMillisecond);
        if (Type == VTimerType.Repeat && interval > End - Next)
        {
            Next = End;
            FinishAtEnd = true;
        }
        else Next = checked(Next + interval);
    }
    // 非周期截止点只完成，不执行行为。
    internal bool FinishAtEnd;
    /// <summary>完成或取消并清除处理器与委托引用。</summary>
    /// <param name="state">完成或取消状态。</param>
    internal void Finish(VTimerState state)
    {
        if (IsFinished) return;
        State = state;
        Processor?.Detach(this);
        Processor = null;
        Targets.Clear(); _source = null; _action = null;
    }
}
