using Godot;
using System.Collections.Generic;

/// <summary>集中生成、移动、碰撞检测与释放弹幕。</summary>
public partial class BulletManager : Node2D
{
	// 活动弹幕列表；待释放节点立即移出，避免重复命中。
	private readonly List<Bullet> _active = new();
	// 满额提示只输出一次，恢复容量后允许再次提示。
	private bool _limitReported;
	/// <summary>当前活动弹幕数量，不包含待释放节点。</summary>
	public int ActiveCount => _active.Count;
	/// <summary>生成一颗采用集中参数的弹幕。</summary>
	/// <param name="origin">全局发射点，单位为像素。</param>
	/// <param name="angle">屏幕角度（度），0向右、90向下。</param>
	/// <param name="team">子弹所属阵营。</param>
	/// <returns>生成的子弹，容量不足时为空。</returns>
	public Bullet? Spawn(Vector2 origin, float angle, BulletTeam team)
	{
		if (_active.Count >= BattleConfig.MaxBullets)
		{
			if (!_limitReported) GD.Print("弹幕达到2048上限，本次生成已跳过。");
			_limitReported = true;
			return null;
		}
		_limitReported = false;
		// 子弹使用容器局部坐标，与 Boss 的变换分离。
		var bullet = new Bullet();
		bullet.ConfigureShot(ToLocal(origin), angle, team);
		AddChild(bullet);
		_active.Add(bullet);
		return bullet;
	}
	/// <summary>推进所有弹幕，并用目标相对位移检测连续碰撞。</summary>
	/// <param name="delta">经过的非负秒数。</param>
	/// <param name="player">敌弹的伤害目标。</param>
	/// <param name="boss">玩家弹的伤害目标。</param>
	public void Advance(double delta, PlayerController player, BossController boss)
	{
		// 倒序索引使删除当前弹幕不影响未更新对象。
		for (int index = _active.Count - 1; index >= 0; index--)
		{
			// 保存步起点及阵营所对应的目标扫掠线段。
			var bullet = _active[index];
			var start = bullet.GlobalPosition;
			bullet.Advance(delta);
			var targetStart = bullet.Team == BulletTeam.Enemy ? player.PreviousPosition : boss.PreviousPosition;
			var targetEnd = bullet.Team == BulletTeam.Enemy ? player.GlobalPosition : boss.GlobalPosition;
			var radius = bullet.Radius + (bullet.Team == BulletTeam.Enemy ? BattleConfig.PlayerRadius : boss.CollisionRadius);
			var hit = false;
			if (SweptHit(start - targetStart, bullet.GlobalPosition - targetEnd, radius))
				hit = bullet.Team == BulletTeam.Enemy
					? player.Health.TakeDamage(bullet.Damage, player.Dodge.IsActive)
					: boss.TakeDamage(bullet.Damage);
			if (hit || bullet.Expired)
			{
				_active.RemoveAt(index);
				bullet.QueueFree();
			}
		}
	}
	/// <summary>判断相对运动线段是否穿过原点圆。</summary>
	/// <param name="start">相对起点，单位为像素。</param>
	/// <param name="end">相对终点，单位为像素。</param>
	/// <param name="radius">双方半径之和，单位为非负像素。</param>
	/// <returns>线段接触或穿过圆时为真。</returns>
	public static bool SweptHit(Vector2 start, Vector2 end, float radius)
	{
		// 将原点投影到有限线段，退化线段按点处理。
		var movement = end - start;
		var lengthSquared = movement.LengthSquared();
		var fraction = lengthSquared > 0 ? Mathf.Clamp(-start.Dot(movement) / lengthSquared, 0, 1) : 0;
		return (start + movement * fraction).LengthSquared() <= radius * radius;
	}
	/// <summary>清理全部活动弹幕，同时重置满额提示。</summary>
	public void Clear()
	{
		// 逐个释放本管理器持有的活动节点。
		foreach (var bullet in _active) bullet.QueueFree();
		_active.Clear();
		_limitReported = false;
	}
}
