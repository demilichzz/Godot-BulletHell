/// <summary>绑定Boss_AIGen_19第01阶段的数据弹幕，沿用现有阶段生命周期。</summary>
public sealed class B22_Phase01 : BossPhase
{
    /// <summary>战斗界面显示的参考符卡名称或弹幕逻辑描述。</summary>
    public override string Name => "弯月短激光双侧掠过";

    /// <summary>加载本阶段JSON并启动现有数据发射器。</summary>
    /// <param name="boss">所属Boss，提供世界坐标发射参考。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        BindEmitter(VBulletEmitter.Load("res://Data/Emitters/AIGen/Boss_AIGen_19_Phase01.json"), boss);
    }

    /// <summary>累计损失100点生命后进入下一阶段。</summary>
    /// <param name="boss">所属Boss。</param>
    /// <returns>达到本阶段结束血线时为真。</returns>
    public override bool ShouldEnd(BossController boss) => boss.MaxHp - boss.Hp >= 100;
}
