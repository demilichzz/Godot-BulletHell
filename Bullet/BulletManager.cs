using Godot;
using System.Collections.Generic;

/// <summary>登记弹幕并统一推进、碰撞和释放，同步注销所属发射批次。</summary>
public partial class BulletManager : Node2D
{
	// 活动弹幕列表；待释放节点立即移出，避免重复命中。
	private readonly List<Bullet> _active = new();
	// 满额提示只输出一次，恢复容量后允许再次提示。
	private bool _limitReported;
	/// <summary>当前活动弹幕数量，不包含待释放节点。</summary>
	public int ActiveCount => _active.Count;
	/// <summary>全场活动子弹的只读视图。</summary>
	public IReadOnlyList<Bullet> ActiveBullets => _active.AsReadOnly();
	/// <summary>生成一颗采用集中参数的弹幕。</summary>
	/// <param name="origin">全局发射点，单位为像素。</param>
	/// <param name="angle">屏幕角度（度），0向右、90向下。</param>
	/// <param name="team">子弹所属阵营。</param>
	/// <returns>生成的子弹，容量不足时为空。</returns>
	public Bullet? Spawn(Vector2 origin, float angle, BulletTeam team)
	{
		// 兼容调用通过单发批次进入统一构造流程。
		var emitter = new SingleBulletEmitter(BulletSpawnData.ForTeam(team, angle));
		emitter.Emit(this, origin);
		return emitter.Bullets.Count == 0 ? null : emitter.Bullets[0];
	}
	/// <summary>在构造节点前检查容量，满额提示仅输出一次。</summary>
	/// <returns>是否仍可登记一颗子弹。</returns>
	internal bool CanSpawn()
	{
		if (_active.Count >= BattleConfig.MaxBullets)
		{
			if (!_limitReported) GD.Print("弹幕达到2048上限，本次生成已跳过。");
			_limitReported = true;
			return false;
		}
		_limitReported = false;
		return true;
	}
	/// <summary>登记已初始化的节点，将全局起点转换为容器局部坐标。</summary>
	/// <param name="bullet">由发射器初始化且尚未入树的子弹。</param>
	/// <param name="emitter">所属单次发射批次。</param>
	internal void Register(Bullet bullet, BulletEmitter emitter)
	{
		bullet.SpawnPosition = bullet.Position = ToLocal(bullet.Position);
		bullet.Emitter = emitter;
		AddChild(bullet);
		_active.Add(bullet);
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
				ReleaseAt(index);
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
		// 倒序清理同时注销批次引用，已退出阶段的批次也能释放。
		for (int index = _active.Count - 1; index >= 0; index--) ReleaseAt(index);
		_limitReported = false;
	}
	/// <summary>从两个活动列表注销并延迟释放节点。</summary>
	/// <param name="index">全场活动列表的有效零基索引。</param>
	private void ReleaseAt(int index)
	{
		// 先移除引用，再释放，避免同一步重复命中。
		var bullet = _active[index];
		_active.RemoveAt(index);
		bullet.Emitter?.Unregister(bullet);
		bullet.Emitter = null;
		RemoveChild(bullet);
		bullet.QueueFree();
	}
	/// <summary>管理器离场时同步清除全部批次引用。</summary>
	public override void _ExitTree() => Clear();
}
