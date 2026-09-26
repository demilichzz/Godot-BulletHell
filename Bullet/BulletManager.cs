using Godot;
using System.Collections.Generic;

/// <summary>统一推进弹幕与碰撞，释放时注销发射器、队列和时间线引用。</summary>
public partial class BulletManager : Node2D
{
	// 活动弹幕列表；待释放节点立即移出，避免重复命中。
	private readonly List<Bullet> _active = new();
	// 全场活动子弹的固定只读视图。
	private readonly IReadOnlyList<Bullet> _activeView;
	/// <summary>缓存只读视图，不在每次读取时重新包装列表。</summary>
	public BulletManager() => _activeView = _active.AsReadOnly();
	// 满额提示只输出一次，恢复容量后允许再次提示。
	private bool _limitReported;
	/// <summary>当前活动弹幕数量，不包含待释放节点。</summary>
	public int ActiveCount => _active.Count;
	/// <summary>全场活动子弹的只读视图。</summary>
	public IReadOnlyList<Bullet> ActiveBullets => _activeView;
	/// <summary>直接生成一颗采用完整初始化参数的弹幕。</summary>
	/// <param name="settings">完整初始化参数，位置为全局逻辑像素。</param>
	/// <returns>生成的子弹，容量不足时为空。</returns>
	public Bullet? Spawn(BulletDefaultSet settings) => Spawn(settings, null);
	/// <summary>统一创建、配置并登记单颗子弹，可关联发射器。</summary>
	/// <param name="settings">完整出生参数，位置为全局逻辑像素。</param>
	/// <param name="emitter">所属发射器；玩家直接生成时为空。</param>
	/// <returns>成功生成的子弹；容量不足时为空。</returns>
	internal Bullet? Spawn(BulletDefaultSet settings, BulletEmitter? emitter)
	{
		Bullet.Validate(settings);
		if (!CanSpawn()) return null;
		// 直接生成仍经过统一初始化、登记和释放流程。
		var bullet = new Bullet();
		try
		{
			bullet.Configure(settings);
			Register(bullet, emitter);
			return bullet;
		}
		catch
		{
			bullet.Free();
			throw;
		}
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
	/// <param name="emitter">所属发射器；直接生成的单颗子弹为空。</param>
	internal void Register(Bullet bullet, BulletEmitter? emitter)
	{
		// 先建立出生时钟，失败时尚未把节点交给管理器。
		bullet.Timeline = GlobalEvent.CreateTimeline(bullet);
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
	public static bool SweptHit(Vector2 start, Vector2 end, double radius)
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

    /// <summary>仅清除指定发射器的存活子弹，沿用统一注销入口。</summary>
    /// <param name="emitter">需要清弹的所属发射器。</param>
    public void ClearEmitter(BulletEmitter emitter)
    {
        // 倒序删除不改变其余子弹的相对顺序。
        for (int index = _active.Count - 1; index >= 0; index--)
            if (ReferenceEquals(_active[index].Emitter, emitter)) ReleaseAt(index);
    }
	/// <summary>从两个活动列表注销并延迟释放节点。</summary>
	/// <param name="index">全场活动列表的有效零基索引。</param>
	private void ReleaseAt(int index)
	{
		// 先移除引用，再释放，避免同一步重复命中。
		var bullet = _active[index];
		bullet.Deactivate();
		_active.RemoveAt(index);
		bullet.Emitter?.Unregister(bullet);
		bullet.Emitter = null;
		RemoveChild(bullet);
		bullet.QueueFree();
	}
	/// <summary>管理器离场时同步清除全部批次引用。</summary>
	public override void _ExitTree() => Clear();
}
