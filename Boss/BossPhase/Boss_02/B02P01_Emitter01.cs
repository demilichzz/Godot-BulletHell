/// <summary>Boss_02阶段01的路径发射器，每秒从上方扇区随机位置绘制一条静止点弹曲线。</summary>
public sealed class B02P01_Emitter01 : VBulletEmitter
{
    /// <summary>绑定数据定义，随机锚点、曲线及逐颗生成节奏均由JSON配置。</summary>
    /// <param name="manager">当前战斗的弹幕容器。</param>
    /// <param name="owner">提供生命周期的Boss；路径使用世界原点参考。</param>
    protected override void Build(VBulletManager manager, BossController owner)
    {
        UseDefinition(Load("res://Data/Emitters/B02P01_Emitter01.json"));
    }
}
