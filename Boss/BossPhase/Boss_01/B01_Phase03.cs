using Godot;
using System;

/// <summary>调度Boss_01阶段03的斜线逐圈发射和圆周目标移动。</summary>
public sealed class B01_Phase03 : BossPhase
{
    // 同一模板提供射击间隔与子弹参数。
    private readonly BulletDefaultSet _template = BulletDefaultSet.Get(BulletType.ScaleSet);
    // 战场局部圆心及半径（像素）。
    private static readonly Vector2 MoveCenter = new(640, 250);
    private const float MoveRadius = 200;
    /// <summary>圆周移动速度，逻辑像素/秒。</summary>
    protected override float MoveSpeed => 100;
    /// <summary>进入阶段时先留在当前位置，等待首次圆周选点。</summary>
    /// <param name="boss">进入本阶段的Boss。</param>
    /// <returns>Boss当前战场局部位置。</returns>
    protected override Vector2 GetInitialMoveTarget(BossController boss) => boss.Position;
    /// <summary>阶段显示名称。</summary>
    public override string Name => "环形弹幕 · 阶段03";
    /// <summary>取得阶段03初始生命，按最大生命减去两个阶段阈值计算。</summary>
    /// <param name="boss">目标 Boss，用于读取最大生命点数。</param>
    /// <returns>最大生命减200后的合法生命点数。</returns>
    public override int GetInitialHp(BossController boss) => Math.Max(1, boss.MaxHp - 200);
    /// <summary>注册先选点后射击的永久重复计时器。</summary>
    /// <param name="boss">已入树的Boss，位置使用战场局部像素。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        // 同刻按注册序号先选点，再从当前位置发射。
        var manager = GlobalEvent.GetBulletManager();
        TrackTimer(GlobalEvent.RegisterTimer(new VTimer(5000, 5000, 0, VTimerType.RepeatForever,
            new[] { boss }, _ => ChooseTarget(boss))));
        TrackTimer(GlobalEvent.RegisterTimer(new VTimer(1000, 2000, 0, VTimerType.RepeatForever,
            new[] { boss }, _ =>
            {
                // 固定本轮斜线，第一圈立即生成；其余五圈按200毫秒间隔生成。
                var first = new B01P03_Emitter01(_template);
                var second = new B01P03_Emitter02(_template);
                first.Emit(manager, boss.GlobalPosition);
                second.Emit(manager, boss.GlobalPosition);
                TrackEmitter(first);
                TrackEmitter(second);
                TrackTimer(GlobalEvent.RegisterTimer(new VTimer(200, 200, 1000, VTimerType.Repeat,
                    () =>
                    {
                        first.EmitNextRing();
                        TrackEmitter(first);
                    })));
            })));
    }
    /// <summary>仅抽样一次弧度，在固定圆周选取新目标。</summary>
    /// <param name="boss">提供路段起点的Boss。</param>
    private void ChooseTarget(BossController boss)
    {
        // 随机角0向右，顺时针为正，保持原抽样次数和顺序。
        double angle = VMath.getRandomDouble(0, Math.Tau);
        SetMoveTarget(boss, VMath.PolarMove(MoveCenter, angle, MoveRadius));
    }
}
