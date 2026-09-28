using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>局部派发入口的取消、排序、清场和重入定向验证。</summary>
public partial class TimerVerification
{
    /// <summary>不参与全场候选扫描的隔离时间线所有者。</summary>
    private sealed class LocalOwner : IVTimelineOwner
    {
        /// <summary>由调用方局部派发的时间线。</summary>
        public VTimeline? Timeline { get; }
        /// <summary>在指定战斗时钟登记本地所有者。</summary>
        /// <param name="clock">负责清场及派发保护的处理器。</param>
        public LocalOwner(VTimerProcessor clock) => Timeline = new VTimeline(clock, this, local: true);
    }

    /// <summary>验证本地动作不被全局重复派发，并沿用所有保护机制。</summary>
    private void VerifyLocalDispatch()
    {
        // 控制动作先于本地动作，本地对象之间由调用顺序决定。
        var clock = new VTimerProcessor();
        var control = new TimelineOwner(clock).Timeline!;
        var first = new LocalOwner(clock).Timeline!;
        var second = new LocalOwner(clock).Timeline!;
        var order = new List<string>();
        control.At(15, () => order.Add("control"));
        first.At(10, () => order.Add("first10"));
        first.At(5, () => order.Add("first5"));
        second.At(1, () => order.Add("second1"));
        clock.AdvanceByUnits(1000, _ => { control.AdvanceUnits(1000); first.AdvanceUnits(1000); second.AdvanceUnits(1000); });
        Check(order.SequenceEqual(new[] { "control" }), "本地所有者不进入全场候选扫描");
        clock.AdvanceByUnits(0, dispatchLocal: () => { first.DispatchDue(); second.DispatchDue(); });
        Check(order.SequenceEqual(new[] { "control", "first5", "first10", "second1" }), "本地跨对象顺序及内部时刻排序");

        // 同一生成回调的多个时刻全部取消，但独立成员动作保留。
        Action spawn = () => order.Add("spawn");
        first.At(20, spawn);
        first.At(30, spawn);
        first.At(20, () => order.Add("member"));
        first.CancelAction(spawn);
        Check(first.ActionCount == 1, "取消生成回调真正移除全部时刻");
        clock.AdvanceByUnits(1000, _ => first.AdvanceUnits(1000), first.DispatchDue);
        Check(order[^1] == "member" && !order.Contains("spawn"), "取消生成保留成员动作");
        first.After(0, () => clock.Clear());
        second.After(0, () => order.Add("after-clear"));
        clock.AdvanceByUnits(0, dispatchLocal: () => { first.DispatchDue(); second.DispatchDue(); });
        Check(!order.Contains("after-clear") && clock.TimelineActionCount == 0, "全场清理包含本地所有者并停止当前派发");

        // 回调不得重入时钟；异常后必须重置才能继续。
        var reentrantClock = new VTimerProcessor();
        var reentrant = new LocalOwner(reentrantClock).Timeline!;
        reentrant.At(0, () => reentrantClock.AdvanceByUnits(1));
        Throws<InvalidOperationException>(() => reentrantClock.AdvanceByUnits(0, dispatchLocal: reentrant.DispatchDue));
        reentrantClock.Clear(true);

        // 零延迟链通过迭代执行并受统一动作预算约束。
        var boundedClock = new VTimerProcessor();
        var bounded = new LocalOwner(boundedClock).Timeline!;
        int calls = 0;
        Action repeat = null!;
        repeat = () => { calls++; bounded.After(0, repeat); };
        bounded.At(0, repeat);
        Throws<InvalidOperationException>(() => boundedClock.AdvanceByUnits(0, dispatchLocal: bounded.DispatchDue));
        Check(calls == 10000, "局部零延迟动作链受10000次预算保护");
        boundedClock.Clear(true);
    }
}
