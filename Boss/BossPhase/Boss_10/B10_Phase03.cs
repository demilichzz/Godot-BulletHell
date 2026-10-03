using System;

/// <summary>绑定Boss_AIGen_07第03阶段的数据弹幕，沿用现有阶段生命周期。</summary>
public sealed class B10_Phase03 : BossPhase
{
    /// <summary>战斗界面显示的参考符卡名称或弹幕逻辑描述。</summary>
    public override string Name => "芙兰朵露 · 禁弹「Starbow Break」";

    /// <summary>加载本阶段JSON并启动现有数据发射器。</summary>
    /// <param name="boss">所属Boss，提供世界坐标发射参考。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        BindEmitter(VBulletEmitter.Load("res://Data/Emitters/AIGen/Boss_AIGen_07_Phase03.json"), boss);
    }

    /// <summary>直接切入本阶段时恢复对应初始血线。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>最大生命减200点，至少保留1点。</returns>
    public override int GetInitialHp(BossController boss) => Math.Max(1, boss.MaxHp - 200);
}
