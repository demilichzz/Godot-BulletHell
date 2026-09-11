using Godot;
using System;

/// <summary>管理闪避方向、持续时间和从触发开始计算的冷却。</summary>
public sealed class PlayerDodge
{
	/// <summary>剩余闪避时间，单位为秒。</summary>
	public double Remaining { get; private set; }
	/// <summary>剩余冷却时间，单位为秒。</summary>
	public double Cooldown { get; private set; }
	/// <summary>是否仍处于闪避无敌期。</summary>
	public bool IsActive => Remaining > 0;
	/// <summary>本次闪避锁定的单位方向，默认向上。</summary>
	public Vector2 Direction { get; private set; } = Vector2.Up;
	/// <summary>冷却结束时启动闪避。</summary>
	/// <param name="direction">非零屏幕方向，右和下为正；零向量按向上处理。</param>
	/// <returns>是否成功启动。</returns>
	public bool TryStart(Vector2 direction)
	{
		if (Cooldown > 0) return false;
		Direction = direction.IsZeroApprox() ? Vector2.Up : direction.Normalized();
		Remaining = BattleConfig.DodgeDuration;
		Cooldown = BattleConfig.DodgeCooldown;
		return true;
	}
	/// <summary>推进持续时间与冷却。</summary>
	/// <param name="delta">经过的非负秒数。</param>
	public void Advance(double delta)
	{
		Remaining = Math.Max(0, Remaining - delta);
		Cooldown = Math.Max(0, Cooldown - delta);
		// 消除固定步浮点余量，避免持续或冷却多出一帧。
		if (Remaining < 1e-9) Remaining = 0;
		if (Cooldown < 1e-9) Cooldown = 0;
	}
}
