using Godot;
using System;

/// <summary>产生相对于发射方向的弹幕角度。</summary>
public abstract class BulletPattern
{
	/// <summary>逐发返回角度，不负责创建节点。</summary>
	/// <param name="emit">接收角度的回调；单位为度，0向右、90向下。</param>
	/// <param name="angle">基础角度，单位为度，0向右、90向下。</param>
	public abstract void Emit(Action<float> emit, float angle);
}
/// <summary>沿基础方向发射单颗子弹。</summary>
public sealed class SinglePattern : BulletPattern
{
	/// <summary>输出一颗子弹的角度。</summary>
	/// <param name="emit">接收角度（度）的回调。</param>
	/// <param name="angle">屏幕角度（度），0向右、90向下。</param>
	public override void Emit(Action<float> emit, float angle) => emit(angle);
}
/// <summary>按等角间距发射完整圆环。</summary>
public sealed class RingPattern : BulletPattern
{
	// 每圈子弹数量，必须为正数。
	private readonly int _count;
	/// <summary>配置圆环数量。</summary>
	/// <param name="count">正整数数量，默认24。</param>
	public RingPattern(int count = BattleConfig.RingCount)
	{
		if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
		_count = count;
	}
	/// <summary>依次输出各子弹方向。</summary>
	/// <param name="emit">接收角度（度）的回调。</param>
	/// <param name="angle">首颗角度（度），0向右、90向下。</param>
	public override void Emit(Action<float> emit, float angle)
	{
		// 当前子弹在环内的索引，从零开始。
		for (int index = 0; index < _count; index++) emit(angle + index * 360f / _count);
	}
}
