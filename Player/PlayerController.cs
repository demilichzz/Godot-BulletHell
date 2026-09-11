using Godot;
using System;

/// <summary>协调玩家运动、闪避、生命与自动攻击，由战斗管理器驱动。</summary>
public partial class PlayerController : Node2D
{
	/// <summary>移动与边界组件。</summary>
	public PlayerMovement Movement { get; } = new();
	/// <summary>闪避组件。</summary>
	public PlayerDodge Dodge { get; } = new();
	/// <summary>生命组件。</summary>
	public PlayerHealth Health { get; } = new();
	/// <summary>自动攻击组件。</summary>
	public PlayerAttack Attack { get; } = new();
	/// <summary>当前物理步起点的全局位置，单位为像素。</summary>
	public Vector2 PreviousPosition { get; private set; }
	/// <summary>推进输入、位移与攻击；计时在本步碰撞后结束。</summary>
	/// <param name="delta">非负物理步秒数。</param>
	/// <param name="input">屏幕移动输入，右和下为正。</param>
	/// <param name="dodgePressed">本步是否新按下闪避键。</param>
	/// <param name="bullets">接收玩家攻击弹的管理器。</param>
	/// <param name="boss">自动攻击目标。</param>
	public void Advance(double delta, Vector2 input, bool dodgePressed, BulletManager bullets, BossController boss)
	{
		PreviousPosition = GlobalPosition;
		if (Health.Hp == 0) return;
		// 移动方向和本步闪避所占时间，单位分别为单位向量和秒。
		var direction = Movement.ReadDirection(input);
		if (dodgePressed) Dodge.TryStart(Movement.LastDirection);
		var dodgeSeconds = Math.Min(delta, Dodge.Remaining);
		Position = Movement.Clamp(Position + Dodge.Direction * BattleConfig.DodgeSpeed * (float)dodgeSeconds
			+ direction * BattleConfig.MoveSpeed * (float)(delta - dodgeSeconds));
		Attack.Advance(delta, bullets, GlobalPosition, boss);
	}
	/// <summary>结束本步的冷却与闪烁更新。</summary>
	/// <param name="delta">非负物理步秒数。</param>
	/// <param name="hurtAtStart">步开始时的受击无敌秒数，避免新伤害立即消耗一个物理步。</param>
	public void FinishStep(double delta, double hurtAtStart)
	{
		Dodge.Advance(delta);
		if (hurtAtStart > 0) Health.Advance(delta);
		Modulate = new Color(1, 1, 1, Health.Invulnerability > 0 && ((int)(Health.Invulnerability * 12) % 2 == 0) ? 0.3f : 1);
		QueueRedraw();
	}
	/// <summary>绘制占位角色、中心判定点与闪避反馈。</summary>
	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, 10, Dodge.IsActive ? Colors.Gold : Colors.DeepSkyBlue);
		DrawCircle(Vector2.Zero, BattleConfig.PlayerRadius, Colors.White);
	}
}
