using System;

/// <summary>Boss03预留的第3阶段，可切入且暂不绑定发射器。</summary>
public sealed class B03_Phase03 : BossPhase
{
    /// <summary>预留阶段的显示名称。</summary>
    public override string Name => "Boss 03 · 阶段03（预留）";
    /// <summary>直接切入时恢复本阶段的初始血线。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>最大生命减200，至少保留1点。</returns>
    public override int GetInitialHp(BossController boss) => Math.Max(1, boss.MaxHp - 200);
}
