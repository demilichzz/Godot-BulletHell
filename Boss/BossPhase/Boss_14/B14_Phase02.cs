using System;

/// <summary>绑定Boss_AIGen_11第02阶段的数据弹幕，沿用现有阶段生命周期。</summary>
public sealed class B14_Phase02 : BossPhase
{
    /// <summary>战斗界面显示的参考符卡名称或弹幕逻辑描述。</summary>
    public override string Name => "菱形阵列左右分离";

    /// <summary>加载本阶段JSON并启动现有数据发射器。</summary>
    /// <param name="boss">所属Boss，提供世界坐标发射参考。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        BindEmitter(VBulletEmitter.Load("res://Data/Emitters/AIGen/Boss_AIGen_11_Phase02.json"), boss);
    }

    /// <summary>直接切入本阶段时恢复对应初始血线。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>最大生命减100点，至少保留1点。</returns>
    public override int GetInitialHp(BossController boss) => Math.Max(1, boss.MaxHp - 100);

    /// <summary>累计损失200点生命后进入下一阶段。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>达到本阶段结束血线时为真。</returns>
    public override bool ShouldEnd(BossController boss) => boss.MaxHp - boss.Hp >= 200;
}
