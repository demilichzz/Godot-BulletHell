using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>验证实体及独立时间线的边界、目标注销和步末派发。</summary>
public partial class TimerVerification : Node
{
    /// <summary>隔离验证用、由调用方手动推进年龄的实体。</summary>
    private sealed class TimelineOwner : IVTimelineOwner
    {
        /// <summary>此测试实体的时间线。</summary>
        public VTimeline? Timeline { get; }
        /// <summary>在指定逻辑时钟创建测试实体。</summary>
        /// <param name="clock">所属逻辑时钟。</param>
        public TimelineOwner(VTimerProcessor clock) => Timeline = new VTimeline(clock, this);
    }

    // 成功断言的累计数量。
    private int _checks;
    /// <summary>场景就绪后运行验证。</summary>
    public override void _Ready() => Callable.From(Run).CallDeferred();
    /// <summary>核对条件，失败时附带说明。</summary>
    /// <param name="condition">预期条件。</param>
    /// <param name="message">失败说明。</param>
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    /// <summary>核对指定操作抛出的异常类型。</summary>
    /// <typeparam name="T">预期异常类型。</typeparam>
    /// <param name="action">待执行操作。</param>
    private void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { _checks++; return; }
        throw new Exception($"未抛出{typeof(T).Name}");
    }
    /// <summary>创建已经进入场景树的目标节点。</summary>
    /// <returns>调用方负责释放的节点。</returns>
    private Node2D Target()
    {
        var node = new Node2D();
        AddChild(node);
        return node;
    }
    /// <summary>运行全部计时专项验证并设置退出状态。</summary>
    private void Run()
    {
        try
        {
            VerifyTimeline();
            VerifyAdapter();
            VerifyTargets();
            VerifyMutation();
            VerifyPlayer();
            GD.Print($"PASS: {_checks} timeline assertions");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    /// <summary>验证实体年龄、适配器自动年龄、全场排序与周期补发。</summary>
    private void VerifyTimeline()
    {
        var clock = new VTimerProcessor();
        var first = new TimelineOwner(clock).Timeline!;
        var second = new TimelineOwner(clock).Timeline!;
        var adapter = new VTimelineAdapter(clock);
        var order = new List<string>();
        first.At(25, () => order.Add("第一实体"));
        adapter.At(25, _ => order.Add("独立适配器"));
        second.At(25, () => order.Add("第二实体"));
        first.Repeat(30, 10, 50, () => order.Add($"周期@{clock.NowMs}"));
        double moved = 0;
        clock.AdvanceByUnits(1500, seconds =>
        {
            moved += seconds;
            first.AdvanceUnits(1500);
            second.AdvanceUnits(1500);
            Check(order.Count == 0 && adapter.Timeline!.ElapsedUnits == 1500, "运动先于派发且适配器自动计龄");
        });
        Check(Math.Abs(moved - 0.025) < 1e-12
            && order.SequenceEqual(new[] { "第一实体", "独立适配器", "第二实体" }), "同刻按登记顺序执行");
        clock.AdvanceByUnits(1500, _ => { first.AdvanceUnits(1500); second.AdvanceUnits(1500); });
        Check(order.Skip(3).SequenceEqual(new[] { "周期@50", "周期@50", "周期@50" }), "有限周期包含终点并在步末补发");
        Check(adapter.Timeline is null && clock.TimelineActionCount == 0, "适配器最后动作完成自动解除");
        second.After(10, () => order.Add("已取消"));
        second.Cancel();
        clock.AdvanceByUnits(600);
        Check(!order.Contains("已取消"), "实体时间线取消未来动作");
        Throws<ArgumentOutOfRangeException>(() => first.Repeat(0, 0, null, () => { }));
        Throws<ArgumentOutOfRangeException>(() => first.At(0, () => { }));
        Throws<InvalidOperationException>(() => second.At(0, () => { }));
        clock.Clear(true);

        var step = new VTimerProcessor();
        var early = new TimelineOwner(step).Timeline!;
        var late = new TimelineOwner(step).Timeline!;
        var detached = new VTimelineAdapter(step);
        var due = new List<int>();
        late.At(12, () => due.Add(12));
        detached.At(8, _ => due.Add(8));
        early.At(5, () => due.Add(5));
        step.AdvanceByUnits(VTimerProcessor.FixedStepUnits, _ =>
        {
            early.AdvanceUnits(VTimerProcessor.FixedStepUnits);
            late.AdvanceUnits(VTimerProcessor.FixedStepUnits);
        });
        Check(due.SequenceEqual(new[] { 5, 8, 12 }) && step.NowUnits == 1000, "步内按原定到期时刻排序");
        step.Clear();
    }

    /// <summary>验证独立适配器的起点、截止、清理及参数边界。</summary>
    private void VerifyAdapter()
    {
        var clock = new VTimerProcessor();
        var calls = new List<double>();
        var repeating = new VTimelineAdapter(clock);
        repeating.Repeat(200, 200, 600, _ => calls.Add(clock.NowMs));
        clock.AdvanceByUnits(600 * VTimerProcessor.UnitsPerMillisecond);
        Check(calls.SequenceEqual(new[] { 600.0, 600.0, 600.0 }) && repeating.Timeline is null,
            "无目标适配器周期补发并自然结束");
        var finite = new VTimelineAdapter(clock);
        finite.Repeat(0, 100, 250, _ => calls.Add(clock.NowMs));
        clock.AdvanceByUnits(250 * VTimerProcessor.UnitsPerMillisecond);
        Check(calls.Count == 6 && finite.Timeline is null, "非周期终点不额外执行");
        var once = new VTimelineAdapter(clock);
        once.After(0, _ => calls.Add(clock.NowMs));
        clock.AdvanceByUnits(0);
        Check(calls.Count == 7 && once.Timeline is null, "零延迟单次完成自动注销");
        var cancelled = new VTimelineAdapter(clock);
        cancelled.After(10, _ => Check(false, "主动取消后执行"));
        cancelled.Cancel(); cancelled.Cancel();
        clock.AdvanceByUnits(600);
        Check(cancelled.Timeline is null && clock.TimelineActionCount == 0, "主动取消幂等");
        var cleared = new VTimelineAdapter(clock);
        cleared.Repeat(10, 10, null, _ => Check(false, "清场后执行"));
        clock.Clear(true);
        clock.AdvanceByUnits(6000);
        Check(cleared.Timeline is null && clock.NowUnits == 6000, "清场解除独立适配器");
        Throws<ArgumentNullException>(() => new VTimelineAdapter(null!));
        Throws<ArgumentNullException>(() => new VTimelineAdapter(clock).After(0, null!));
        Throws<ArgumentOutOfRangeException>(() => new VTimelineAdapter(clock).Repeat(0, 0, null, _ => { }));
        Throws<ArgumentOutOfRangeException>(() => clock.AdvanceByUnits(-1));
        Check(VTimerProcessor.SecondsToMilliseconds(0.0015) == 2, "秒配置按中点远离零换算");
        Throws<ArgumentOutOfRangeException>(() => VTimerProcessor.SecondsToMilliseconds(double.NaN));
        Throws<OverflowException>(() => VTimerProcessor.SecondsToMilliseconds(double.MaxValue));
        clock.Clear(true);
        int lateCalls = 0;
        clock.AdvanceByUnits(600, _ =>
        {
            var late = new VTimelineAdapter(clock);
            late.After(10, _ => lateCalls++);
        });
        Check(lateCalls == 0, "步内创建的适配器从步末零龄开始");
        clock.AdvanceByUnits(600);
        Check(lateCalls == 1, "新适配器在下一段计龄后触发");
        clock.Clear(true);
    }

    /// <summary>验证目标快照、部分失效、逻辑注销和离场订阅。</summary>
    private void VerifyTargets()
    {
        var a = Target(); var b = Target(); var c = Target();
        var clock = new VTimerProcessor();
        var source = new List<Node2D> { a, a, b };
        var seen = new List<Node2D>();
        var adapter = new VTimelineAdapter(clock, source);
        adapter.Repeat(10, 10, null, nodes => seen.AddRange(nodes));
        source.Clear(); source.Add(c);
        b.QueueFree();
        clock.AdvanceByUnits(600);
        Check(seen.SequenceEqual(new[] { a }), "注册时复制去重且过滤待释放目标");
        a.Free();
        Check(adapter.Timeline is null && adapter.Targets.Count == 0, "最后目标离场即时取消适配器");
        var logical = new VTimelineAdapter(clock, new[] { c });
        logical.At(0, _ => Check(false, "逻辑死亡后仍执行"));
        clock.NotifyTargetDestroyed(c);
        clock.AdvanceByUnits(0);
        Check(logical.Timeline is null && GodotObject.IsInstanceValid(c), "逻辑注销无需释放节点");
        var empty = new VTimelineAdapter(clock, Array.Empty<Node2D>());
        Check(empty.Timeline is null, "空目标集合立即取消");
        var dead = new VTimelineAdapter(clock, new[] { c });
        Check(dead.Timeline is null, "已注销目标不能重新绑定");
        c.Free();
        clock.Clear(true);
        Check(clock.TimelineActionCount == 0 && clock.NowUnits == 0, "清场重置");
        var detached = Target();
        var detachedAdapter = new VTimelineAdapter(clock, new[] { detached });
        detachedAdapter.After(100, _ => Check(false, "离场后仍执行"));
        RemoveChild(detached);
        Check(detachedAdapter.Timeline is null, "节点移出树即解除目标");
        detached.Free();
    }

    /// <summary>验证派发期间增删动作、异常保护及零延迟循环防护。</summary>
    private void VerifyMutation()
    {
        var clock = new VTimerProcessor();
        var order = new List<int>();
        var first = new VTimelineAdapter(clock);
        var other = new VTimelineAdapter(clock);
        var second = new VTimelineAdapter(clock);
        first.At(0, _ =>
        {
            order.Add(1);
            other.Cancel();
            second.After(0, _ => order.Add(3));
        });
        other.At(0, _ => order.Add(99));
        second.At(0, _ => order.Add(2));
        clock.AdvanceByUnits(0);
        Check(order.SequenceEqual(new[] { 1, 2, 3 }), "同刻取消及新增动作顺序稳定");
        var self = new VTimelineAdapter(clock);
        self.Repeat(0, 1, null, _ => self.Cancel());
        clock.AdvanceByUnits(6000);
        Check(self.Timeline is null && clock.TimelineActionCount == 0, "回调自取消停止周期");
        var doomed = Target();
        var killer = new VTimelineAdapter(clock);
        var victim = new VTimelineAdapter(clock, new[] { doomed });
        killer.At(0, _ => doomed.QueueFree());
        victim.At(0, _ => Check(false, "同刻释放的目标仍执行"));
        clock.AdvanceByUnits(0);
        Check(victim.Timeline is null, "每个同刻动作前重查目标");
        var fault = new VTimelineAdapter(clock);
        fault.At(0, _ => throw new ApplicationException("预期异常"));
        Throws<ApplicationException>(() => clock.AdvanceByUnits(0));
        Throws<InvalidOperationException>(() => clock.AdvanceByUnits(0));
        clock.Clear(true);
        var reentrant = new VTimelineAdapter(clock);
        reentrant.At(0, _ => clock.AdvanceByUnits(0));
        Throws<InvalidOperationException>(() => clock.AdvanceByUnits(0));
        clock.Clear(true);
        var recursive = new VTimelineAdapter(clock);
        Action<IReadOnlyList<Node2D>>? action = null;
        action = _ => recursive.After(0, action!);
        recursive.At(0, action);
        Throws<InvalidOperationException>(() => clock.AdvanceByUnits(0));
        clock.Clear(true);
        var clear = new VTimelineAdapter(clock);
        clear.At(1, _ => clock.Clear());
        clock.AdvanceByUnits(6000);
        Check(clock.NowMs == 100 && clock.TimelineActionCount == 0, "回调清场停止旧动作");
        doomed.Free();
    }

    /// <summary>建立一场可用固定输入直接推进的战斗。</summary>
    /// <param name="world">返回所属场景节点，调用方负责释放。</param>
    /// <returns>初始化完成的战斗管理器。</returns>
    private BattleManager Battle(out Node2D world)
    {
        world = Target();
        var battle = new BattleManager();
        world.AddChild(battle);
        battle.Initialize(world);
        battle.SetPhysicsProcess(false);
        return battle;
    }

    /// <summary>验证玩家年龄、攻击补发、闪避和受伤保护及战斗清场。</summary>
    private void VerifyPlayer()
    {
        var battle = Battle(out var world);
        battle.Boss.Stop();
        var player = battle.Player;
        Check(player.Timeline?.ElapsedUnits == 0, "玩家时间线出生年龄为零");
        for (int tick = 0; tick < 12; tick++) battle.StepFixed(Vector2.Zero, false);
        Check(player.Timeline?.ElapsedUnits == 12000 && battle.Bullets.ActiveCount == 1, "玩家首发在200毫秒");
        player.Attack.Stop();
        battle.Bullets.Clear();
        battle.StepFixed(Vector2.Zero, true);
        Check(player.Dodge.IsActive && player.Dodge.Remaining > 0, "闪避开始后保持状态");
        for (int tick = 0; tick < 8; tick++) battle.StepFixed(Vector2.Zero, false);
        Check(!player.Dodge.IsActive && player.Dodge.Cooldown > 0, "九步结束闪避且冷却仍在");
        for (int tick = 9; tick < 60; tick++) battle.StepFixed(Vector2.Zero, false);
        Check(player.Dodge.Cooldown == 0 && player.Dodge.TryStart(Vector2.Right), "六十步恢复闪避");
        Check(player.Health.TakeDamage(1, false) && !player.Health.TakeDamage(1, false), "受击保护阻止连续扣血");
        for (int tick = 0; tick < 60; tick++) battle.StepFixed(Vector2.Zero, false);
        Check(player.Health.Invulnerability == 0 && player.Health.TakeDamage(99, false) && player.Health.Hp < 0,
            "保护满一秒结束且负血继续战斗");
        battle.StepFixed(Vector2.Zero, false);
        player.Attack.Initialize(player, battle.Bullets);
        long resumedAt = player.Timeline!.ElapsedUnits;
        // 先跨过一个不足200毫秒的格点，再推进到下一个格点。
        for (int tick = 0; tick < 12; tick++) battle.StepFixed(Vector2.Zero, false);
        Check(battle.Bullets.ActiveCount == 0 && player.Timeline.ElapsedUnits == resumedAt + 12000,
            "重新启动只在满足等待时间的周期点发射");
        for (int tick = 0; tick < 12; tick++) battle.StepFixed(Vector2.Zero, false);
        Check(battle.Bullets.ActiveCount >= 1, "重新启动后的下一格点恢复射击");
        var oldClock = battle.Timers;
        battle.Restart();
        Check(oldClock.TimelineActionCount == 0 && battle.Player.Timeline?.ElapsedUnits == 0,
            "重开清理旧时间线并重置玩家年龄");
        battle.StopBattle();
        Check(battle.Timers.TimelineActionCount == 0, "离场清理玩家及阶段时间线");
        world.Free();

        var resumed = Battle(out var resumedWorld);
        resumed.Boss.Stop();
        for (int tick = 0; tick < 13; tick++) resumed.StepFixed(Vector2.Zero, false);
        resumed.Player.Attack.Stop();
        resumed.Bullets.Clear();
        int actionCount = resumed.Timers.TimelineActionCount;
        resumed.Player.Attack.Initialize(resumed.Player, resumed.Bullets);
        Check(resumed.Timers.TimelineActionCount == actionCount, "重新初始化不叠加永久周期");
        resumed.Timers.AdvanceByUnits(30000, _ => resumed.Player.Timeline!.AdvanceUnits(30000));
        Check(resumed.Bullets.ActiveCount == 1, "大步长补发跳过重启前格点");
        resumed.Timers.AdvanceByUnits(24000, _ => resumed.Player.Timeline!.AdvanceUnits(24000));
        Check(resumed.Bullets.ActiveCount == 3, "重启后的有效周期按原定时刻补发");
        resumedWorld.Free();
    }
}
