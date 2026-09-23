using Godot;
using System;

/// <summary>协调玩家运动、闪避、生命与自动攻击，由战斗管理器驱动。</summary>
public partial class PlayerController : Node2D, IVTimelineOwner
{
	/// <summary>玩家从进入战斗起累计年龄的时间线。</summary>
	public VTimeline? Timeline { get; private set; }
	/// <summary>移动与边界组件。</summary>
	public PlayerMovement Movement { get; } = new();
	/// <summary>闪避组件。</summary>
	public PlayerDodge Dodge { get; } = new();
	/// <summary>生命组件。</summary>
	public PlayerHealth Health { get; } = new();
	/// <summary>自动攻击组件。</summary>
	public PlayerAttack Attack { get; } = new();
	/// <summary>当前事件段起点的全局位置，单位为像素。</summary>
	public Vector2 PreviousPosition { get; private set; }
	/// <summary>从当前战斗时刻激活玩家时间线。</summary>
	public void StartTimeline() => Timeline = GlobalEvent.CreateTimeline(this);
	/// <summary>玩家离场时取消全部未来动作。</summary>
	public override void _ExitTree()
	{
		Timeline?.Cancel();
		Timeline = null;
	}
	/// <summary>按一个逻辑时间段推进玩家年龄与位移。</summary>
	/// <param name="delta">非负逻辑秒数。</param>
	/// <param name="input">屏幕移动输入，右和下为正。</param>
	public void Advance(double delta, Vector2 input)
	{
		PreviousPosition = GlobalPosition;
		Timeline?.AdvanceUnits(VTimeline.SecondsToUnits(delta));
		// 移动方向和本步闪避所占时间，单位分别为单位向量和秒。
		var direction = Movement.ReadDirection(input);
		var dodgeSeconds = Dodge.IsActive ? delta : 0;
		Position = Movement.Clamp(Position + Dodge.Direction * BattleConfig.DodgeSpeed * (float)dodgeSeconds
			+ direction * BattleConfig.MoveSpeed * (float)(delta - dodgeSeconds));
	}
	/// <summary>按剩余无敌时间更新闪烁显示，不消耗逻辑时间。</summary>
	public void FinishStep()
	{
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
