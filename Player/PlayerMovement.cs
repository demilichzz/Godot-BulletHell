using Godot;

/// <summary>计算玩家移动与战场边界约束。</summary>
public sealed class PlayerMovement
{
	/// <summary>最近一次非零移动方向，默认朝上。</summary>
	public Vector2 LastDirection { get; private set; } = Vector2.Up;
	/// <summary>规范化输入并记住有效方向。</summary>
	/// <param name="input">屏幕方向，右和下为正；长度大于一时归一化。</param>
	/// <returns>长度不超过一的移动输入。</returns>
	public Vector2 ReadDirection(Vector2 input)
	{
		// 保留模拟摇杆的小幅输入，同时限制斜向速度。
		var direction = input.LimitLength();
		if (!direction.IsZeroApprox()) LastDirection = direction.Normalized();
		return direction;
	}
	/// <summary>沿圆心方向约束角色中心，使判定圆完整位于半径400像素的活动区域内。</summary>
	/// <param name="position">待限制的战场位置，单位为像素。</param>
	/// <returns>合法的角色中心位置，单位为像素。</returns>
	public Vector2 Clamp(Vector2 position) => BattleConfig.ArenaCenter
		+ (position - BattleConfig.ArenaCenter).LimitLength(BattleConfig.ArenaRadius - BattleConfig.PlayerRadius);
}
