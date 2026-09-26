/// <summary>加载B01P03_Emitter01 的节点树、弹幕队列与数据时间规则。</summary>
public sealed class B01P03_Emitter01 : BulletEmitter
{
    /// <summary>只绑定数据定义，生成周期及成员动作由数据描述。</summary>
    /// <param name="manager">当前战斗的子弹管理器。</param>
    /// <param name="owner">提供生命周期及可选空间参考的Boss。</param>
    protected override void Build(BulletManager manager, BossController owner)
    {
        UseDefinition(Load("res://Data/Emitters/B01P03_Emitter01.json"));
    }
}
