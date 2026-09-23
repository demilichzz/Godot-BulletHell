using Godot;
using System;

/// <summary>调度Boss_01阶段02的发射器和固定目标移动。</summary>
public sealed class B01_Phase02 : BossPhase
{
    // 同一模板提供射击间隔与子弹参数。
    private readonly BulletDefaultSet _template = BulletDefaultSet.Get(BulletType.ScaleSet);
    /// <summary>阶段显示名称。</summary>
    public override string Name => "环形弹幕 · 阶段02";
    /// <summary>取得阶段02初始生命，按最大生命减去一个阶段阈值计算。</summary>
    /// <param name="boss">目标 Boss，用于读取最大生命点数。</param>
    /// <returns>最大生命减100后的合法生命点数。</returns>
    public override int GetInitialHp(BossController boss) => Math.Max(1, boss.MaxHp - 100);
    /// <summary>注册移动与射击期间使用的永久重复计时器。</summary>
    /// <param name="boss">已入树的Boss，位置使用战场局部像素。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        var manager = GlobalEvent.GetBulletManager();
        long interval = VTimerProcessor.SecondsToMilliseconds(_template.IntervalSeconds);
        TrackTimer(GlobalEvent.RegisterTimer(new VTimer(interval, interval, 0, VTimerType.RepeatForever,
            new[] { boss }, _ =>
            {
                // 两个发射器保持独立，当前仅发射器01产生弹幕。
                var first = new B01P02_Emitter01(_template);
                var second = new B01P02_Emitter02(_template);
                first.Emit(manager, boss.GlobalPosition);
                second.Emit(manager, boss.GlobalPosition);
                TrackEmitter(first);
                TrackEmitter(second);
            })));
    }
    /// <summary>Boss累计损失200点生命时进入下一阶段。</summary>
    /// <param name="boss">所属Boss，损失量为最大生命减当前生命。</param>
    /// <returns>是否到达本阶段结束阈值。</returns>
    public override bool ShouldEnd(BossController boss) => boss.MaxHp - boss.Hp >= 200;
}
