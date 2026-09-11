using Godot;

/// <summary>旧版 Boss 兼容入口，普通弹幕容器下自行驱动新控制器。</summary>
public partial class Boss : BossController
{
	/// <summary>建立默认阶段，并根据容器选择更新所有者。</summary>
	public override void _Ready()
	{
		base._Ready();
		SetPhysicsProcess(BulletParent is not BulletManager);
	}
	/// <summary>兼容原独立 Boss 的自动物理更新。</summary>
	/// <param name="delta">本次物理更新秒数。</param>
	public override void _PhysicsProcess(double delta) => Advance(delta);
}
