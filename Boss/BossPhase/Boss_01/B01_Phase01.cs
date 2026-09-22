using Godot;
using System;

/// <summary>调度Boss_01阶段01的双发射器和圆周目标移动。</summary>
public sealed class B01_Phase01 : BossPhase
{
    // 同一模板提供射击间隔与子弹参数。
    private readonly BulletSpawnData _template = new(BulletType.ScaleSet);
    // 战场局部圆心、半径（像素）及移动速度（像素/秒）。
    private static readonly Vector2 MoveCenter = new(640, 250);
    private const float MoveRadius = 200, MoveSpeed = 100;
    // 当前路段起点及累计秒数。
    private Vector2 _travelStart;
    private double _travelSeconds;
    // 退出后禁止直接推进。
    private bool _active;
    /// <summary>当前圆周目标，战场局部逻辑像素。</summary>
    public Vector2 MoveTarget { get; private set; }
    /// <summary>是否正在前往目标。</summary>
    public bool IsMoving { get; private set; }
    /// <summary>阶段显示名称。</summary>
    public override string Name => "环形弹幕 · 阶段01";
    /// <summary>注册先选点后射击的永久重复计时器。</summary>
    /// <param name="boss">已入树的Boss，位置使用战场局部像素。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        _travelSeconds = 0;
        _travelStart = MoveTarget = boss.Position;
        IsMoving = false;
        _active = true;
        // 同刻按注册序号先选点，再从当前位置发射。
        var manager = (BulletManager)boss.BulletParent;
        TrackTimer(manager.Timers.Register(new VTimer(5000, 5000, 0, VTimerType.RepeatForever,
            new[] { boss }, _ => ChooseTarget(boss))));
        long interval = VTimerProcessor.SecondsToMilliseconds(_template.IntervalSeconds);
        TrackTimer(manager.Timers.Register(new VTimer(interval, interval, 0, VTimerType.RepeatForever,
            new[] { boss }, _ =>
            {
                // 每次同时创建两个独立批次，各自保留24颗环形弹幕。
                var first = new B01P01_Emitter01(_template);
                var second = new B01P01_Emitter02(_template);
                first.Emit(manager, boss.GlobalPosition);
                second.Emit(manager, boss.GlobalPosition);
                TrackEmitter(first);
                TrackEmitter(second);
            })));
    }
    /// <summary>推进当前事件段的移动并清除空批次，不独立计时射击。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <param name="delta">非负有限分段秒数。</param>
    public override void Advance(BossController boss, double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!_active || boss.Hp == 0) return;
        PruneEmitters();
        if (!IsMoving) return;
        _travelSeconds += delta;
        boss.Position = _travelStart.MoveToward(MoveTarget, (float)(_travelSeconds * MoveSpeed));
        if (boss.Position == MoveTarget) IsMoving = false;
    }
    /// <summary>仅抽样一次弧度，在固定圆周选取新目标。</summary>
    /// <param name="boss">提供路段起点的Boss。</param>
    private void ChooseTarget(BossController boss)
    {
        // 随机角0向右，顺时针为正，保持原抽样次数和顺序。
        double angle = VMath.getRandomDouble(0, Math.Tau);
        MoveTarget = VMath.PolarMove(MoveCenter, angle, MoveRadius);
        _travelStart = boss.Position;
        _travelSeconds = 0;
        IsMoving = boss.Position != MoveTarget;
    }
    /// <summary>Boss累计损失100点生命时进入下一阶段。</summary>
    /// <param name="boss">所属Boss，损失量为最大生命减当前生命。</param>
    /// <returns>是否到达本阶段结束阈值。</returns>
    public override bool ShouldEnd(BossController boss) => boss.MaxHp - boss.Hp >= 100;
    /// <summary>停止移动并取消阶段计时器，已发子弹继续由管理器处理。</summary>
    /// <param name="boss">退出阶段的Boss。</param>
    public override void Exit(BossController boss)
    {
        _active = false;
        IsMoving = false;
        base.Exit(boss);
    }
}
