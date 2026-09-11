using Godot;

/// <summary>周期性瞄准 Boss 当前中心发射直线弹。</summary>
public sealed class PlayerAttack
{
	// 首次在0.2秒后触发的单发发射器。
	private readonly BulletEmitter _emitter = new(BattleConfig.PlayerInterval, new SinglePattern(), BulletTeam.Player);
	/// <summary>推进自动攻击。</summary>
	/// <param name="delta">经过的非负秒数。</param>
	/// <param name="bullets">战场弹幕管理器。</param>
	/// <param name="origin">玩家全局中心，单位为像素。</param>
	/// <param name="boss">当前目标，死亡后停止发射。</param>
	public void Advance(double delta, BulletManager bullets, Vector2 origin, BossController boss)
	{
		if (boss.Hp > 0) _emitter.Advance(delta, bullets, origin, Mathf.RadToDeg((boss.GlobalPosition - origin).Angle()));
	}
}
