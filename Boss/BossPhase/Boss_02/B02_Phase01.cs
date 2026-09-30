/// <summary>Boss_02的唯一阶段，沿用基类移动并每秒生成一条静止点弹曲线。</summary>
public sealed class B02_Phase01 : BossPhase
{
    /// <summary>阶段显示名称。</summary>
    public override string Name => "Boss 02 · 阶段01";

    /// <summary>绑定本阶段的数据化路径发射器。</summary>
    /// <param name="boss">所属Boss，提供阶段及发射器生命周期。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        BindEmitter(new B02P01_Emitter01(), boss);
    }
}
