using Godot;

/// <summary>Boss_01阶段02的空发射器，未定义时间线时不发射。</summary>
public sealed class B01P02_Emitter02 : BulletEmitter
{
	/// <summary>保留空定义，未登记时间线动作时不发射。</summary>
	/// <param name="manager">统一管理子弹生命周期的容器。</param>
	/// <param name="owner">所属Boss。</param>
	protected override void Build(BulletManager manager, BossController owner)
	{ }
}
