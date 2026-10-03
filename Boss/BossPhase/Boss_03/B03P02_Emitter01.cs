/// <summary>加载Salamander Shield近似弹幕，通过同级复制形成顺逆时针对称弹带。</summary>
public sealed class B03P02_Emitter01 : VBulletEmitter
{
    /// <summary>绑定数据定义，旋转序列与预警激光的时刻全部由JSON控制。</summary>
    /// <param name="manager">当前战斗弹幕容器。</param>
    /// <param name="owner">提供出生参考和阶段生命周期的Boss。</param>
    protected override void Build(VBulletManager manager, BossController owner)
    {
        UseDefinition(Load("res://Data/Emitters/B03P02_Emitter01.json"));
    }
}
