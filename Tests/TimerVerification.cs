using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>验证计时器边界、目标注销、分段运动及固定输入重现。</summary>
public partial class TimerVerification : Node
{
    // 累计断言数。
    private int _checks;
    /// <summary>场景就绪后延迟执行同步验证。</summary>
    public override void _Ready() => Callable.From(Run).CallDeferred();
    /// <summary>失败时抛出明确的验证说明。</summary>
    /// <param name="condition">预期条件。</param>
    /// <param name="message">失败说明。</param>
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    /// <summary>验证操作抛出指定异常类型。</summary>
    /// <typeparam name="T">预期异常类型。</typeparam>
    /// <param name="action">待验证操作。</param>
    private void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { _checks++; return; }
        throw new Exception($"未抛出{typeof(T).Name}");
    }
    /// <summary>建立在树内的独立目标。</summary>
    /// <returns>调用方负责释放的节点。</returns>
    private Node2D Target()
    {
        // 计时目标必须已在战场场景树中。
        var target = new Node2D();
        AddChild(target);
        return target;
    }
    /// <summary>运行完整验证，失败时返回非零退出状态。</summary>
    private void Run()
    {
        try
        {
            VerifyTiming();
            VerifyUnbound();
            VerifyTargets();
            VerifyMutation();
            VerifyBattle();
            GD.Print($"PASS: {_checks} timer assertions");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    /// <summary>验证无目标计时器的补发、顺序、取消和清场。</summary>
    private void VerifyUnbound()
    {
        var clock = new VTimerProcessor();
        var events = new List<string>();
        var repeating = clock.Register(new VTimer(200, 200, 600, VTimerType.Repeat,
            () => events.Add($"{clock.NowMs}:unbound")));
        var target = Target();
        clock.Register(new VTimer(200, 0, 0, VTimerType.Once, new[] { target },
            _ => events.Add($"{clock.NowMs}:bound")));
        clock.AdvanceByUnits(600 * VTimerProcessor.UnitsPerMillisecond);
        Check(events.SequenceEqual(new[] { "200:unbound", "200:bound", "400:unbound", "600:unbound" })
            && repeating.State == VTimerState.Completed, "无目标计时器按注册顺序执行并包含截止点");
        var cancelled = clock.Register(new VTimer(10, 0, 0, VTimerType.Once,
            () => Check(false, "主动取消的无目标计时器仍执行")));
        cancelled.Cancel();
        clock.AdvanceByUnits(10 * VTimerProcessor.UnitsPerMillisecond);
        Check(cancelled.State == VTimerState.Cancelled && clock.ActiveCount == 0, "无目标计时器可主动取消");
        var cleared = clock.Register(new VTimer(10, 10, 0, VTimerType.RepeatForever,
            () => Check(false, "清场后的无目标计时器仍执行")));
        clock.Clear(true);
        clock.AdvanceByUnits(100 * VTimerProcessor.UnitsPerMillisecond);
        Check(cleared.State == VTimerState.Cancelled && clock.ActiveCount == 0, "清场取消无目标计时器");
        Throws<ArgumentNullException>(() => new VTimer(0, 0, 0, VTimerType.Once, (Action)null!));
        target.Free();
    }
    /// <summary>验证三种类型、截止、注册时刻及参数边界。</summary>
    private void VerifyTiming()
    {
        // 整数毫秒序列无需浮点边界容差。
        var target = Target();
        var clock = new VTimerProcessor();
        var times = new List<double>();
        var timer = new VTimer(2000, 500, 3000, VTimerType.Repeat, new[] { target }, _ => times.Add(clock.NowMs));
        clock.Register(timer);
        clock.AdvanceByUnits(119999);
        Check(times.Count == 0 && timer.RemainingMs > 0, "首次等待");
        clock.AdvanceByUnits(60001);
        Check(times.SequenceEqual(new[] { 2000.0, 2500, 3000 }) && timer.State == VTimerState.Completed, "终点包含且补齐周期");
        Check(timer.RemainingMs == 0 && timer.Targets.Count == 0 && clock.ActiveCount == 0, "完成解除引用");
        Throws<InvalidOperationException>(() => clock.Register(timer));
        times.Clear();
        timer = clock.Register(new VTimer(0, 100, 250, VTimerType.Repeat, new[] { target }, _ => times.Add(clock.NowMs)));
        Check(times.Count == 0, "注册不立即调用行为");
        clock.AdvanceByUnits(15000);
        Check(times.SequenceEqual(new[] { 3000.0, 3100, 3200 }) && timer.State == VTimerState.Completed, "非周期截止无额外动作");
        times.Clear();
        var once = clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, _ => times.Add(clock.NowMs)));
        clock.AdvanceByUnits(0);
        Check(times.Count == 1 && once.State == VTimerState.Completed, "零延迟单次");
        timer = clock.Register(new VTimer(1, 1, 0, VTimerType.RepeatForever, new[] { target }, _ => times.Add(clock.NowMs)));
        clock.AdvanceByUnits(600);
        Check(times.Count == 11 && timer.State == VTimerState.Running, "永久重复完整补发");
        timer.Cancel(); timer.Cancel();
        clock.AdvanceByUnits(600);
        Check(times.Count == 11 && clock.ActiveCount == 0, "幂等取消");
        foreach (var args in new[] { (-1L, 1L, 0L, VTimerType.Once), (0L, -1L, 0L, VTimerType.Once),
            (0L, 1L, -1L, VTimerType.Once), (0L, 0L, 1L, VTimerType.Repeat),
            (2L, 1L, 1L, VTimerType.Repeat), (0L, 1L, 0L, (VTimerType)99) })
            Throws<ArgumentOutOfRangeException>(() => new VTimer(args.Item1, args.Item2, args.Item3, args.Item4, new[] { target }, _ => { }));
        Throws<OverflowException>(() => new VTimer(long.MaxValue, 0, 0, VTimerType.Once, new[] { target }, _ => { }));
        Throws<ArgumentNullException>(() => new VTimer(0, 0, 0, VTimerType.Once, null!, _ => { }));
        Throws<ArgumentNullException>(() => new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, null!));
        Throws<ArgumentOutOfRangeException>(() => clock.AdvanceByUnits(-1));
        Check(VTimerProcessor.SecondsToMilliseconds(0.2) == 200 && VTimerProcessor.SecondsToMilliseconds(0.0015) == 2, "秒配置换算");
        Throws<ArgumentOutOfRangeException>(() => VTimerProcessor.SecondsToMilliseconds(double.NaN));
        Throws<OverflowException>(() => VTimerProcessor.SecondsToMilliseconds(double.MaxValue));
        clock.Clear(true);
        clock.AdvanceByUnits(long.MaxValue);
        Throws<OverflowException>(() => clock.Register(new VTimer(1, 0, 0, VTimerType.Once, new[] { target }, _ => { })));
        Throws<OverflowException>(() => clock.AdvanceByUnits(1));
        clock.Clear(true);
        target.Free();
    }
    /// <summary>验证注册快照、部分失效、排队释放与逻辑注销。</summary>
    private void VerifyTargets()
    {
        // 回调只接收最初绑定且仍存活的目标。
        var a = Target(); var b = Target(); var c = Target();
        var clock = new VTimerProcessor();
        var source = new List<Node2D> { a, a };
        var seen = new List<Node2D>();
        var timer = new VTimer(10, 10, 0, VTimerType.RepeatForever, source, nodes => seen.AddRange(nodes));
        source.Add(b);
        clock.Register(timer);
        source.Clear(); source.Add(c);
        b.QueueFree();
        clock.AdvanceByUnits(600);
        Check(seen.SequenceEqual(new[] { a }), "注册时复制去重，排除已待释放目标");
        a.Free();
        Check(timer.State == VTimerState.Cancelled && timer.Targets.Count == 0, "离场自动取消最后目标");
        var logical = clock.Register(new VTimer(0, 1, 0, VTimerType.RepeatForever, new[] { c }, _ => Check(false, "逻辑死亡仍执行")));
        clock.NotifyTargetDestroyed(c);
        clock.AdvanceByUnits(0);
        Check(logical.IsFinished && GodotObject.IsInstanceValid(c), "实体未释放也能逻辑注销");
        var empty = clock.Register(new VTimer(0, 0, 0, VTimerType.Once, Array.Empty<Node2D>(), _ => Check(false, "空目标执行")));
        Check(empty.State == VTimerState.Cancelled, "空目标自动取消");
        var dead = clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { c }, _ => Check(false, "重新绑定死目标")));
        Check(dead.IsFinished, "注销目标不能复活绑定");
        c.Free();
        clock.Clear(true);
        Check(clock.ActiveCount == 0 && clock.NowUnits == 0, "清场重置");
        // 主动移出场景树也等价于离场，即使节点尚未释放。
        var detached = Target();
        var detachedTimer = clock.Register(new VTimer(100, 100, 0, VTimerType.RepeatForever,
            new[] { detached }, _ => Check(false, "离场目标仍执行")));
        RemoveChild(detached);
        Check(detachedTimer.IsFinished, "移出场景树立即解除目标");
        detached.Free();
    }
    /// <summary>验证回调增删、稳定顺序、异常和零延迟循环防护。</summary>
    private void VerifyMutation()
    {
        var target = Target();
        var clock = new VTimerProcessor();
        var order = new List<int>();
        VTimer? other = null;
        clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, _ =>
        {
            order.Add(1);
            other!.Cancel();
            clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, _ => order.Add(3)));
        }));
        other = clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, _ => order.Add(99)));
        clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, _ => order.Add(2)));
        clock.AdvanceByUnits(0);
        Check(order.SequenceEqual(new[] { 1, 2, 3 }), "同刻取消并将新增动作排在已有动作之后");
        VTimer? self = null;
        self = clock.Register(new VTimer(0, 1, 0, VTimerType.RepeatForever, new[] { target }, _ => self!.Cancel()));
        clock.AdvanceByUnits(6000);
        Check(self.State == VTimerState.Cancelled && clock.ActiveCount == 0, "回调自取消不再排期");
        var doomed = Target();
        clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, _ => doomed.QueueFree()));
        var doomedTimer = clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { doomed }, _ => Check(false, "同刻销毁仍执行")));
        clock.AdvanceByUnits(0);
        Check(doomedTimer.IsFinished, "每个回调前重查目标");
        clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, _ => throw new ApplicationException("预期异常")));
        Throws<ApplicationException>(() => clock.AdvanceByUnits(600));
        Throws<InvalidOperationException>(() => clock.AdvanceByUnits(0));
        clock.Clear(true);
        clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, _ => clock.AdvanceByUnits(0)));
        Throws<InvalidOperationException>(() => clock.AdvanceByUnits(0));
        clock.Clear(true);
        // 零延迟动作递归新增，必须明确终止而不是挂起主线程。
        Action<IReadOnlyList<Node2D>>? recurse = null;
        recurse = _ => clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, recurse!));
        clock.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { target }, recurse));
        Throws<InvalidOperationException>(() => clock.AdvanceByUnits(0));
        clock.Clear(true);
        clock.Register(new VTimer(1, 0, 0, VTimerType.Once, new[] { target }, _ => clock.Clear()));
        clock.AdvanceByUnits(6000);
        Check(clock.NowMs == 1 && clock.ActiveCount == 0, "清场停止旧时间线");
        target.Free();
    }
    /// <summary>创建可手动注入输入的隔离战斗。</summary>
    /// <param name="world">返回独立场地，调用者负责释放。</param>
    /// <returns>真实战斗管理器。</returns>
    private BattleManager Battle(out Node2D world)
    {
        world = Target();
        var battle = new BattleManager();
        world.AddChild(battle);
        battle.Initialize(world);
        battle.SetPhysicsProcess(false);
        return battle;
    }
    /// <summary>验证真实子弹分段停止转向、状态结束和重开清理。</summary>
    private void VerifyBattle()
    {
        var battle = Battle(out var world);
        battle.Boss.Stop(); battle.Player.Attack.Stop();
        // 远离双方的长寿子弹，避免碰撞影响时间与位置验证。
        var origin = new Vector2(-1000, -1000);
        var bullet = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = origin,
            Speed = 120,
            LifetimeSeconds = 10
        })!;
        var stop = battle.Timers.Register(new VTimer(25, 0, 0, VTimerType.Once, new[] { bullet }, nodes => ((Bullet)nodes[0]).SetSpeed(0)));
        battle.StepFixed(Vector2.Zero, false);
        battle.StepFixed(Vector2.Zero, false);
        Check(bullet.Position.DistanceTo(origin + Vector2.Right * 3) < 0.001 && stop.IsFinished, "25ms事件分段停止，仅移动3像素");
        battle.Bullets.Clear();
        // 回调中途发射，新子弹只运动出生后的半步。
        battle.Timers.Register(new VTimer(25, 0, 0, VTimerType.Once, new[] { battle.Player }, _ =>
            bullet = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
            {
                Position = origin,
                Speed = 120,
                LifetimeSeconds = 10
            })!));
        battle.StepFixed(Vector2.Zero, false); battle.StepFixed(Vector2.Zero, false);
        Check(bullet.Position.DistanceTo(origin + Vector2.Right) < 0.001, "新生子弹不多推进出生前时间");
        battle.Restart();
        battle.Player.Attack.Stop();
        bullet = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = origin,
            Speed = 120,
            LifetimeSeconds = 10
        })!;
        var nodes = new[] { bullet };
        battle.Timers.Register(new VTimer(2000, 0, 0, VTimerType.Once, nodes, alive => ((Bullet)alive[0]).SetSpeed(0)));
        var turn = battle.Timers.Register(new VTimer(2500, 0, 0, VTimerType.Once, nodes, alive =>
        {
            // 所有方向为弧度，π/2向下；恢复120像素/秒。
            var current = (Bullet)alive[0];
            current.SetDirection(Mathf.Pi / 2);
            current.SetSpeed(120);
        }));
        battle.Boss.Stop();
        VerificationClock.BattleSeconds(battle, 2, Vector2.Zero, false);
        Check(bullet.Position.DistanceTo(origin + Vector2.Right * 240) < 0.01 && bullet.Speed == 0, "2秒停止且阶段退出不取消子弹动作");
        VerificationClock.BattleSeconds(battle, 0.5, Vector2.Zero, false);
        Check(bullet.Position.DistanceTo(origin + Vector2.Right * 240) < 0.01 && turn.IsFinished, "停止半秒后转向");
        VerificationClock.BattleSeconds(battle, 0.5, Vector2.Zero, false);
        Check(bullet.Position.DistanceTo(origin + new Vector2(240, 60)) < 0.01, "恢复速度后沿新方向移动");
        var pending = battle.Timers.Register(new VTimer(500, 500, 0, VTimerType.RepeatForever, nodes, _ => Check(false, "清场残留动作")));
        battle.Bullets.Clear();
        Check(pending.IsFinished && pending.Targets.Count == 0, "子弹清场取消参数动作");
        battle.Restart();
        Check(battle.Timers.NowUnits == 0 && battle.Timers.ActiveCount == 3, "重开只保留三个初始周期");
        battle.Boss.Stop();
        Check(battle.Timers.ActiveCount == 1, "阶段退出仅取消阶段周期");
        battle.Player.Attack.Stop();
        battle.StepFixed(Vector2.Right, true);
        VerificationClock.BattleSeconds(battle, 59.0 / 60, Vector2.Zero, false);
        Check(battle.Player.Dodge.Cooldown == 0, "整秒冷却结束");
        battle.StepFixed(Vector2.Right, true);
        Check(battle.Player.Dodge.IsActive, "冷却边界可再次操作");
        battle.Restart(); battle.Boss.Stop(); battle.Player.Attack.Stop();
        Check(battle.Player.Health.TakeDamage(1, false), "受击建立无敌计时器");
        VerificationClock.BattleSeconds(battle, 59.0 / 60, Vector2.Zero, false);
        Check(!battle.Player.Health.TakeDamage(1, false), "到期前仍受保护");
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.Player.Health.TakeDamage(1, false), "到期状态完成后允许下一次伤害");
        var oldPlayer = battle.Player;
        // 玩家负血仍保留目标身份以及受击无敌结束计时器。
        battle.Timers.AdvanceByUnits(60000);
        battle.Player.Health.TakeDamage(99, false);
        Check(GodotObject.IsInstanceValid(oldPlayer) && battle.Player.Health.Hp == -98
            && battle.Timers.ActiveCount == 1, "负血玩家继续保留无敌计时器");
        battle.Restart(); battle.Boss.Stop(); battle.Player.Attack.Stop();
        // 到期子弹先由管理器释放，其同刻参数动作不得再执行。
        var expiring = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = origin,
            Speed = 0,
            LifetimeSeconds = 0.025f
        })!;
        var expiryTimer = battle.Timers.Register(new VTimer(25, 0, 0, VTimerType.Once, new[] { expiring }, _ => Check(false, "过期子弹动作执行")));
        battle.StepFixed(Vector2.Zero, false); battle.StepFixed(Vector2.Zero, false);
        Check(expiryTimer.IsFinished && battle.Bullets.ActiveCount == 0, "同刻寿命到期先注销");
        battle.Restart(); battle.Boss.Stop(); battle.Player.Attack.Stop();
        // 两段折线的转角经过玩家，起终点连线则远离玩家，确保不能合并检测。
        var playerPosition = battle.Player.Position;
        var corner = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = playerPosition + new Vector2(-100, 0),
            Speed = 12000
        })!;
        var cornerTimer = battle.Timers.Register(new VTimer(8, 0, 0, VTimerType.Once,
            new[] { corner }, alive => ((Bullet)alive[0]).SetDirection(Mathf.Pi / 2)));
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.Player.Health.Hp == 2 && cornerTimer.IsFinished, "转向前运动段独立碰撞，命中后取消动作");
        // 额外显示更新不能改变战斗时钟或随机状态。
        long before = battle.Timers.NowUnits;
        for (int frame = 0; frame < 100; frame++) battle.Player.FinishStep();
        Check(battle.Timers.NowUnits == before, "显示刷新不推进逻辑时钟");
        battle.Timers.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { battle.Player }, _ => battle.StopBattle()));
        var stoppedPosition = battle.Player.Position;
        battle.StepFixed(Vector2.Right, true);
        Check(battle.Timers.NowUnits == before && battle.Player.Position == stoppedPosition, "步开始回调停止战斗后不再接受输入");
        battle.Restart();
        battle.Timers.Register(new VTimer(0, 0, 0, VTimerType.Once, new[] { battle.Player }, _ => battle.Restart()));
        battle.StepFixed(Vector2.Right, true);
        Check(battle.Timers.NowUnits == 0 && battle.Player.Position == BattleConfig.PlayerSpawn, "回调重开后不继续旧物理步");
        var oldTimers = battle.Timers;
        world.Free();
        Check(oldTimers.ActiveCount == 0, "直接离场解除全部回调");
    }
}
