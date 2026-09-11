using System;

/// <summary>管理玩家生命、受击无敌与死亡通知。</summary>
public sealed class PlayerHealth
{
	/// <summary>当前生命，范围为零至初始生命。</summary>
	public int Hp { get; private set; } = BattleConfig.PlayerHp;
	/// <summary>剩余受击无敌秒数。</summary>
	public double Invulnerability { get; private set; }
	/// <summary>生命变化通知，参数为变化后的生命点数。</summary>
	public event Action<int>? HealthChanged;
	/// <summary>生命首次归零时通知。</summary>
	public event Action? Died;
	/// <summary>尝试施加一次伤害。</summary>
	/// <param name="damage">正整数伤害点数；非正数不生效。</param>
	/// <param name="dodging">当前是否处于闪避无敌状态。</param>
	/// <returns>是否造成有效伤害。</returns>
	public bool TakeDamage(int damage, bool dodging)
	{
		if (Hp == 0 || damage <= 0 || dodging || Invulnerability > 0) return false;
		Hp = Math.Max(0, Hp - damage);
		Invulnerability = BattleConfig.HurtInvulnerability;
		HealthChanged?.Invoke(Hp);
		if (Hp == 0) Died?.Invoke();
		return true;
	}
	/// <summary>推进受击无敌计时。</summary>
	/// <param name="delta">经过的非负秒数。</param>
	public void Advance(double delta)
	{
		Invulnerability = Math.Max(0, Invulnerability - delta);
		if (Invulnerability < 1e-9) Invulnerability = 0;
	}
}
