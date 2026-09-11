using Godot;
using System;

/// <summary>弹幕所属阵营，用于筛选伤害目标。</summary>
public enum BulletTeam
{
	// Boss 发出的敌弹。
	Enemy,
	// 玩家发出的攻击弹。
	Player
}
/// <summary>按固定间隔生成指定形状的弹幕，保留累计时间余量。</summary>
public sealed class BulletEmitter
{
	// 发射间隔（秒）、弹幕样式与阵营。
	private readonly double _interval;
	private readonly BulletPattern _pattern;
	private readonly BulletTeam _team;
	// 距上次发射的累计秒数。
	private double _elapsed;
	/// <summary>构造一个从零开始计时的发射器。</summary>
	/// <param name="interval">正数有限发射间隔，单位为秒。</param>
	/// <param name="pattern">生成角度的弹幕样式。</param>
	/// <param name="team">所发子弹的阵营。</param>
	public BulletEmitter(double interval, BulletPattern pattern, BulletTeam team)
	{
		if (!double.IsFinite(interval) || interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval));
		_interval = interval;
		_pattern = pattern;
		_team = team;
	}
	/// <summary>推进发射时钟，达到间隔才发射第一波。</summary>
	/// <param name="delta">经过的非负秒数。</param>
	/// <param name="parent">接收子弹的独立容器；支持管理器或旧版普通节点。</param>
	/// <param name="origin">全局发射位置，单位为像素。</param>
	/// <param name="angle">基础角度，单位为度，0向右、90向下，默认0。</param>
	public void Advance(double delta, Node2D parent, Vector2 origin, float angle = 0)
	{
		_elapsed += delta;
		while (_elapsed + 1e-9 >= _interval)
		{
			_elapsed = Math.Max(0, _elapsed - _interval);
			_pattern.Emit(direction => Spawn(parent, origin, direction), angle);
		}
	}
	/// <summary>将一颗配置好的弹幕交给容器。</summary>
	/// <param name="parent">独立的弹幕父节点。</param>
	/// <param name="origin">全局位置，单位为像素。</param>
	/// <param name="angle">角度（度），0向右、90向下。</param>
	private void Spawn(Node2D parent, Vector2 origin, float angle)
	{
		if (parent is BulletManager manager)
		{
			manager.Spawn(origin, angle, _team);
			return;
		}
		// 兼容旧版普通容器，子弹自行驱动物理更新。
		var bullet = new Bullet();
		bullet.ConfigureShot(parent.ToLocal(origin), angle, _team);
		parent.AddChild(bullet);
	}
}
