using System;
using System.Collections.Generic;

/// <summary>拥有独立年龄和待派发动作的时间线。</summary>
public interface IVTimelineOwner
{
    /// <summary>当前实体的时间线；未激活时为空。</summary>
    VTimeline? Timeline { get; }
}

/// <summary>保存所有者年龄与相对激活时刻的动作，由战斗处理器统一派发。</summary>
public sealed class VTimeline
{
    // 归属时钟、实体所有者、激活时刻和仍待执行的动作。
    private readonly VTimerProcessor _clock;
    private readonly IVTimelineOwner _owner;
    private readonly long _origin;
    private readonly List<VTimelineEvent> _events = new();
    private bool _cancelled;
    // 本实体实际经过的整数逻辑时间单位。
    private long _elapsedUnits;

    /// <summary>创建从当前逻辑时刻开始、由实体自行推进的时间线。</summary>
    /// <param name="clock">所属战斗的唯一逻辑时钟。</param>
    /// <param name="owner">持有此时间线的实体。</param>
    public VTimeline(VTimerProcessor clock, IVTimelineOwner owner)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _origin = clock.NowUnits;
        _clock.RegisterTimelineOwner(owner);
    }

    /// <summary>从激活时刻起经过的整数逻辑时间单位。</summary>
    public long ElapsedUnits => _elapsedUnits;

    /// <summary>累计所有者的逻辑年龄，动作由处理器在步末执行。</summary>
    /// <param name="units">非负整数逻辑时间单位。</param>
    public void AdvanceUnits(long units)
    {
        if (units < 0) throw new ArgumentOutOfRangeException(nameof(units));
        if (!_cancelled) _elapsedUnits = checked(_elapsedUnits + units);
    }

    /// <summary>把非负秒数转换为整数逻辑时间单位。</summary>
    /// <param name="seconds">实体本次更新的秒数。</param>
    /// <returns>四舍五入后的逻辑时间单位。</returns>
    public static long SecondsToUnits(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        return checked((long)Math.Round(seconds * VTimerProcessor.UnitsPerSecond, MidpointRounding.AwayFromZero));
    }

    /// <summary>在激活后的指定毫秒执行一次。</summary>
    /// <param name="atMs">非负整数毫秒。</param>
    /// <param name="action">到期执行的动作。</param>
    public void At(long atMs, Action action) => Add(ToUnits(atMs), 0, null, action);

    /// <summary>从当前逻辑时刻延迟指定毫秒执行一次。</summary>
    /// <param name="delayMs">非负整数毫秒。</param>
    /// <param name="action">到期执行的动作。</param>
    public void After(long delayMs, Action action) => Add(checked(_elapsedUnits + ToUnits(delayMs)), 0, null, action);

    /// <summary>从激活时刻起按固定周期执行，可指定包含在内的结束时刻。</summary>
    /// <param name="startMs">首次执行时刻，非负整数毫秒。</param>
    /// <param name="intervalMs">正整数毫秒周期。</param>
    /// <param name="endMs">包含在内的结束时刻，空值表示永久重复。</param>
    /// <param name="action">每次到期执行的动作。</param>
    public void Repeat(long startMs, long intervalMs, long? endMs, Action action)
    {
        if (intervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMs));
        if (endMs is not null && endMs < startMs) throw new ArgumentOutOfRangeException(nameof(endMs));
        long end = endMs is null ? long.MaxValue : ToUnits(endMs.Value);
        Add(ToUnits(startMs), ToUnits(intervalMs), end, action);
    }

    /// <summary>立即取消本实体的全部未来动作。</summary>
    public void Cancel()
    {
        if (_cancelled) return;
        _cancelled = true;
        // 快照允许单个动作在取消时移出原列表。
        foreach (var item in _events.ToArray()) item.Cancel();
        _events.Clear();
        _clock.UnregisterTimelineOwner(_owner);
    }

    /// <summary>移除已经完成的动作引用。</summary>
    /// <param name="item">已完成或取消的动作。</param>
    internal void Remove(VTimelineEvent item)
    {
        _events.Remove(item);
    }

    /// <summary>返回该实体已到期且原定时间最早的动作。</summary>
    /// <returns>待派发动作；没有到期动作时为空。</returns>
    internal VTimelineEvent? NextDue()
    {
        VTimelineEvent? result = null;
        foreach (var item in _events)
            if (!item.Cancelled && item.Due <= _elapsedUnits && (result is null || item.Due < result.Due
                || (item.Due == result.Due && item.Sequence < result.Sequence))) result = item;
        return result;
    }

    /// <summary>当前仍待执行的动作数。</summary>
    internal int ActionCount => _events.Count;

    /// <summary>把实体相对触发时刻换算为全场排序时刻。</summary>
    /// <param name="due">本实体相对出生的整数时间单位。</param>
    /// <returns>对应的战斗时钟时刻。</returns>
    internal long AbsoluteDue(long due) => checked(_origin + due);

    /// <summary>登记绝对时刻的动作。</summary>
    /// <param name="due">实体出生后的整数逻辑时间单位。</param>
    /// <param name="interval">重复间隔，0为单次。</param>
    /// <param name="end">重复结束时刻；空值为单次，最大值为永久重复。</param>
    /// <param name="action">到期动作。</param>
    private void Add(long due, long interval, long? end, Action action)
    {
        if (_cancelled) throw new InvalidOperationException("已取消的时间线不能再登记动作。");
        ArgumentNullException.ThrowIfNull(action);
        if (due < _elapsedUnits) throw new ArgumentOutOfRangeException(nameof(due), "不能在过去登记动作。");
        // 新动作取得统一的全场顺序号。
        var item = new VTimelineEvent(this, due, interval, end, action);
        item.Sequence = _clock.NextSequence();
        _events.Add(item);
    }

    /// <summary>将非负毫秒换算为整数逻辑时间单位。</summary>
    /// <param name="milliseconds">非负整数毫秒。</param>
    /// <returns>逻辑时间单位。</returns>
    private static long ToUnits(long milliseconds)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        return checked(milliseconds * VTimerProcessor.UnitsPerMillisecond);
    }
}

/// <summary>实体时间线中的单个待调度动作。</summary>
internal sealed class VTimelineEvent
{
    // 所属时间线、到期动作及重复边界。
    private readonly VTimeline _owner;
    private Action? _action;
    private readonly long _interval;
    private readonly long? _end;
    /// <summary>下一次到期时刻，相对实体激活的整数逻辑时间单位。</summary>
    internal long Due { get; private set; }
    /// <summary>跨实体的稳定注册序号。</summary>
    internal long Sequence { get; set; }
    /// <summary>动作是否已结束。</summary>
    internal bool Cancelled { get; private set; }

    /// <summary>保存一个时间点或周期动作。</summary>
    /// <param name="owner">所属实体时间线。</param>
    /// <param name="due">首次相对实体激活的到期时刻。</param>
    /// <param name="interval">重复间隔，0为单次。</param>
    /// <param name="end">重复结束时刻。</param>
    /// <param name="action">到期动作。</param>
    internal VTimelineEvent(VTimeline owner, long due, long interval, long? end, Action action)
    {
        _owner = owner;
        Due = due;
        _interval = interval;
        _end = end;
        _action = action;
    }

    /// <summary>执行当前动作并安排下一次到期。</summary>
    internal void Fire()
    {
        if (Cancelled) return;
        _action!();
        if (Cancelled) return;
        if (_interval == 0 || (_end is not null && _interval > _end.Value - Due))
        {
            Cancel();
            return;
        }
        Due = checked(Due + _interval);
    }

    /// <summary>立即失效并释放回调引用。</summary>
    internal void Cancel()
    {
        if (Cancelled) return;
        Cancelled = true;
        _action = null;
        _owner.Remove(this);
    }
}
