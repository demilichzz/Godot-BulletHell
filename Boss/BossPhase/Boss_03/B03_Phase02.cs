using System;

/// <summary>以顺逆时针对称红色弹带和预警瞄准激光近似Salamander Shield。</summary>
public sealed class B03_Phase02 : BossPhase
{
    /// <summary>第二阶段的符卡显示名称。</summary>
    public override string Name => "难题「Salamander Shield」 · 阶段02";
    /// <summary>沿用阶段默认中心移动并绑定数据发射器。</summary>
    /// <param name="boss">所属Boss，出生参考使用其世界位置。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        BindEmitter(new B03P02_Emitter01(), boss);
    }
    /// <summary>直接切入时恢复本阶段的初始血线。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>最大生命减100，至少保留1点。</returns>
    public override int GetInitialHp(BossController boss) => Math.Max(1, boss.MaxHp - 100);
    /// <summary>累计损失200点生命后进入第3阶段。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>达到下一阶段血线时为真。</returns>
    public override bool ShouldEnd(BossController boss) => boss.MaxHp - boss.Hp >= 200;
}
