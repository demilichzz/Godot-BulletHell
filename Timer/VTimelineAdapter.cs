using Godot;
using System;
using System.Collections.Generic;

/// <summary>为无实体任务提供由逻辑时钟自动推进的时间线，并可绑定一组存活目标。</summary>
public sealed class VTimelineAdapter : IVTimelineOwner
{
    // 适配器所属处理器、是否要求目标及按绑定顺序保存的存活目标。
    private readonly VTimerProcessor _clock;
    internal readonly List<Node2D> Targets = new();
    internal bool RequiresTargets { get; }

    /// <summary>适配器时间线；取消或全部动作完成后为空。</summary>
    public VTimeline? Timeline { get; private set; }

    /// <summary>创建并登记独立时间线；传入目标集合时固定存活目标快照。</summary>
    /// <param name="processor">负责推进和派发此时间线的逻辑时钟。</param>
    /// <param name="targets">需要绑定的节点集合；空引用表示无目标任务，空集合则立即取消。</param>
    public VTimelineAdapter(VTimerProcessor processor, IEnumerable<Node2D>? targets = null)
    {
        _clock = processor ?? throw new ArgumentNullException(nameof(processor));
        RequiresTargets = targets is not null;
        Timeline = new VTimeline(processor, this);
        try { processor.AttachAdapter(this, targets); }
        catch { Cancel(); throw; }
    }

    /// <summary>在适配器激活后的指定毫秒执行一次，并传入届时仍存活的目标。</summary>
    /// <param name="atMs">非负整数毫秒，相对适配器创建时刻。</param>
    /// <param name="action">到期动作；参数是按绑定顺序排列的存活目标快照。</param>
    public void At(long atMs, Action<IReadOnlyList<Node2D>> action) => RequireTimeline().At(atMs, Wrap(action));

    /// <summary>从适配器当前年龄延迟指定毫秒执行一次。</summary>
    /// <param name="delayMs">非负整数毫秒。</param>
    /// <param name="action">到期动作；参数是存活目标快照。</param>
    public void After(long delayMs, Action<IReadOnlyList<Node2D>> action) => RequireTimeline().After(delayMs, Wrap(action));

    /// <summary>从激活时刻按固定周期执行，可指定包含在内的结束毫秒。</summary>
    /// <param name="startMs">首次执行时刻，非负整数毫秒。</param>
    /// <param name="intervalMs">正整数毫秒周期。</param>
    /// <param name="endMs">包含在内的结束时刻；空值表示永久重复。</param>
    /// <param name="action">到期动作；参数是存活目标快照。</param>
    public void Repeat(long startMs, long intervalMs, long? endMs, Action<IReadOnlyList<Node2D>> action)
        => RequireTimeline().Repeat(startMs, intervalMs, endMs, Wrap(action));

    /// <summary>立即取消所有未来动作并解除目标订阅，可重复调用。</summary>
    public void Cancel()
    {
        Timeline?.Cancel();
        Timeline = null;
    }

    /// <summary>确认适配器尚可登记动作。</summary>
    /// <returns>活动时间线。</returns>
    private VTimeline RequireTimeline() => Timeline ?? throw new InvalidOperationException("已结束的适配器不能登记动作。");

    /// <summary>为动作建立每次派发前的目标过滤包装。</summary>
    /// <param name="action">需要取得存活目标快照的动作。</param>
    /// <returns>交给时间线登记的无参数动作。</returns>
    private Action Wrap(Action<IReadOnlyList<Node2D>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () =>
        {
            _clock.Prune(this);
            if (Timeline is null) return;
            action(Targets.ToArray());
        };
    }
}
