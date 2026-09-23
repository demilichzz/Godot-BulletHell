/// <summary>Boss_02的唯一阶段，沿用基类移动且暂不发射弹幕。</summary>
public sealed class B02_Phase01 : BossPhase
{
    /// <summary>阶段显示名称。</summary>
    public override string Name => "Boss 02 · 阶段01";

    /// <summary>本阶段预留的唯一空发射器。</summary>
    /// <summary>绑定本阶段保留的空发射器。</summary>
    /// <param name="boss">所属Boss，提供发射位置。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        BindEmitter(new B02P01_Emitter01(), boss);
    }
}
