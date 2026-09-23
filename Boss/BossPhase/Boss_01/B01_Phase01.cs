using Godot;
using System;

/// <summary>调度Boss_01阶段01的双发射器和圆周目标移动。</summary>
public sealed class B01_Phase01 : BossPhase
{
    // 战场局部圆心及半径（像素）。
    private static readonly Vector2 MoveCenter = new(640, 250);
    /// <summary>圆周移动速度，逻辑像素/秒。</summary>
    protected override float MoveSpeed => 100;
    /// <summary>进入阶段时先留在当前位置，等待首次圆周选点。</summary>
    /// <param name="boss">进入本阶段的Boss。</param>
    /// <returns>Boss当前战场局部位置。</returns>
    protected override Vector2 GetInitialMoveTarget(BossController boss) => boss.Position;
    /// <summary>阶段显示名称。</summary>
    public override string Name => "环形弹幕 · 阶段01";
    /// <summary>先登记阶段选点，再绑定两个独立周期发射器。</summary>
    /// <param name="boss">已入树的Boss，位置使用战场局部像素。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        // 同刻按注册序号先选点，再从当前位置发射。
        Timeline!.Repeat(5000, 5000, null, () => ChooseTarget(boss));
        BindEmitter(new B01P01_Emitter01(), boss);
        BindEmitter(new B01P01_Emitter02(), boss);
    }
    /// <summary>仅抽样一次弧度，在固定圆周选取新目标。</summary>
    /// <param name="boss">提供路段起点的Boss。</param>
    private void ChooseTarget(BossController boss)
    {
        // 随机角0向右，顺时针为正，保持原抽样次数和顺序。
        double angle = VMath.getRandomDouble(0, Math.Tau);
        SetMoveTarget(boss, VMath.PolarMove(MoveCenter, angle, 200));
    }
    /// <summary>Boss累计损失100点生命时进入下一阶段。</summary>
    /// <param name="boss">所属Boss，损失量为最大生命减当前生命。</param>
    /// <returns>是否到达本阶段结束阈值。</returns>
    public override bool ShouldEnd(BossController boss) => boss.MaxHp - boss.Hp >= 100;
}
