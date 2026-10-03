/// <summary>加载Boss03的激光网格和圆弹波次，不绘制激光发射端。</summary>
public sealed class B03P01_Emitter01 : VBulletEmitter
{
    /// <summary>绑定数据定义，所有发射时刻和随机参数由Creator树处理。</summary>
    /// <param name="manager">当前战斗弹幕容器。</param>
    /// <param name="owner">提供出生参考和阶段生命周期的Boss。</param>
    protected override void Build(VBulletManager manager, BossController owner)
    {
        UseDefinition(Load("res://Data/Emitters/B03P01_Emitter01.json"));
    }
}
