using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>生成和成员动作共用的整数毫秒时间规则及索引增量。</summary>
public sealed record TimelineAttribute
{
    /// <summary>首次时刻，默认0毫秒，相对父对象或成员出生。</summary>
    public long? StartMs { get; init; }
    /// <summary>正整数重复间隔，省略表示单次。</summary>
    public long? IntervalMs { get; init; }
    /// <summary>包含端点的重复结束时刻，省略表示永久重复。</summary>
    public long? EndMs { get; init; }
    /// <summary>固定时刻数组，不能与首次、间隔和结束时刻混用。</summary>
    public IReadOnlyList<long>? AtMs { get; init; }
    /// <summary>每增加一个索引的起点或固定时刻增量，整数毫秒。</summary>
    public long StartMsAdd { get; init; }
    /// <summary>每增加一个索引的重复间隔增量，整数毫秒。</summary>
    public long IntervalMsAdd { get; init; }
    /// <summary>每增加一个索引的结束时刻增量，整数毫秒。</summary>
    public long EndMsAdd { get; init; }
    /// <summary>仅MemberTimeline填写的原子运行参数动作。</summary>
    public ParameterActionAttribute? Set { get; init; }

    /// <summary>校验所有索引边界的派生时刻和动作适用范围。</summary>
    /// <param name="amount">索引数量，正整数。</param>
    /// <param name="member">是否为成员动作规则。</param>
    internal void Validate(int amount, bool member)
    {
        if (amount <= 0 || member != (Set is not null)) throw new JsonException("Timeline的Set字段与用途不匹配。");
        Set?.Validate();
        if (AtMs is not null)
        {
            if (AtMs.Count == 0 || StartMs.HasValue || IntervalMs.HasValue || EndMs.HasValue || IntervalMsAdd != 0 || EndMsAdd != 0)
                throw new JsonException("AtMs需为非空数组且不能混用周期字段。");
        }
        else if ((!IntervalMs.HasValue && (EndMs.HasValue || IntervalMsAdd != 0 || EndMsAdd != 0))
            || (!EndMs.HasValue && EndMsAdd != 0))
            throw new JsonException("周期增量或结束时刻缺少对应基础字段。");
        // 线性索引变化只需检查两个端点，固定时刻各自检查。
        foreach (int index in new[] { 0, amount - 1 }) Resolve(index);
    }

    /// <summary>判断指定索引是否可能触发多次。</summary>
    /// <param name="index">父节点或成员的零基出生索引。</param>
    /// <returns>该规则会多次触发时为真。</returns>
    internal bool Repeats(int index)
    {
        var time = Resolve(index);
        return AtMs is not null ? AtMs.Count > 1
            : time.Interval.HasValue && (!time.End.HasValue || time.End.Value - time.Start >= time.Interval.Value);
    }

    /// <summary>冻结固定时刻列表，供实例安全共享。</summary>
    /// <returns>具有只读时刻列表的规则。</returns>
    internal TimelineAttribute Freeze() => this with { AtMs = AtMs is null ? null : Array.AsReadOnly(AtMs.ToArray()) };

    /// <summary>将一条规则登记到已有实体时间线。</summary>
    /// <param name="timeline">父对象或实际成员的时间线。</param>
    /// <param name="index">父成员或自身的零基出生索引。</param>
    /// <param name="action">到期时执行的固定生成或参数动作。</param>
    internal void Register(VTimeline timeline, int index, Action action)
    {
        var time = Resolve(index);
        if (AtMs is not null)
        {
            // 保留数组声明顺序与重复时刻。
            foreach (long at in AtMs) timeline.At(checked(at + index * StartMsAdd), action);
        }
        else if (time.Interval.HasValue) timeline.Repeat(time.Start, time.Interval.Value, time.End, action);
        else timeline.At(time.Start, action);
    }

    /// <summary>按索引计算并校验内部时间单位可表示的派生时刻。</summary>
    /// <param name="index">非负出生索引。</param>
    /// <returns>首次、周期与可选结束毫秒。</returns>
    private (long Start, long? Interval, long? End) Resolve(int index)
    {
        // checked同时覆盖索引乘法和时间单位转换。
        long start = checked((StartMs ?? 0) + index * StartMsAdd);
        long? interval = IntervalMs.HasValue ? checked(IntervalMs.Value + index * IntervalMsAdd) : null;
        long? end = EndMs.HasValue ? checked(EndMs.Value + index * EndMsAdd) : null;
        if (AtMs is not null)
        {
            foreach (long at in AtMs) CheckTime(checked(at + index * StartMsAdd));
        }
        else
        {
            CheckTime(start);
            if (interval.HasValue) { CheckTime(interval.Value); if (interval <= 0) throw new JsonException("周期必须为正整数毫秒。"); }
            if (end.HasValue) { CheckTime(end.Value); if (end < start) throw new JsonException("结束时刻早于首次时刻。"); }
        }
        return (start, interval, end);
    }

    /// <summary>检查非负毫秒可转换为内部整数时间单位。</summary>
    /// <param name="milliseconds">待验证的整数毫秒。</param>
    private static void CheckTime(long milliseconds)
    {
        if (milliseconds < 0) throw new JsonException("时刻不能为负。");
        _ = checked(milliseconds * VTimerProcessor.UnitsPerMillisecond);
    }
}
