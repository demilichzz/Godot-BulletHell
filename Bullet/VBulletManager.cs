using Godot;
using System.Collections.Generic;
using System;
using System.Linq;

/// <summary>统一推进弹幕与碰撞，释放时注销发射器、队列和时间线引用。</summary>
public partial class VBulletManager : Node2D
{
	// 活动弹幕列表；待释放节点立即移出，避免重复命中。
	private readonly List<VBullet> _active = new();
	// 全场活动子弹的固定只读视图。
	private readonly IReadOnlyList<VBullet> _activeView;
	/// <summary>缓存只读视图，不在每次读取时重新包装列表。</summary>
	public VBulletManager() => _activeView = _active.AsReadOnly();
    // 按启动顺序保留活动和停止后仍有遗留子弹的树。
    private readonly List<VBulletEmitter> _emitters = new();
    // 新生对象的零龄动作按FIFO派发，不递归进入子树。
    private readonly Queue<VNode> _births = new();
    // 清场改变代次，使进行中的遍历立即停止。
    private long _generation;

    /// <summary>登记一个运行树，阶段退出不直接解除登记。</summary>
    /// <param name="emitter">只启动一次的发射器。</param>
    internal void RegisterEmitter(VBulletEmitter emitter) => _emitters.Add(emitter);

    /// <summary>登记新生对象，先配置完生成及成员动作再统一派发。</summary>
    /// <param name="node">零龄的实际对象。</param>
    internal void RegisterBirth(VNode node) { node.PendingBirth = true; _births.Enqueue(node); }

    /// <summary>在控制时间线之后按树派发成员动作和零龄初始化。</summary>
    internal void DispatchTimelines()
    {
        // 出生对象由FIFO处理，不能在常规遍历中提前执行。
        long generation = _generation;
        for (int index = 0; index < _emitters.Count && generation == _generation; index++)
        {
            var emitter = _emitters[index];
            emitter.Timeline?.DispatchDue();
            emitter.Root.VisitMembers(node =>
            {
                if (generation == _generation && !node.PendingBirth) node.Timeline?.DispatchDue();
            });
        }
        for (int index = 0; index < _active.Count && generation == _generation;)
        {
            var bullet = _active[index];
            if (bullet.Creator is null && !bullet.PendingBirth) bullet.Timeline?.DispatchDue();
            if (index < _active.Count && ReferenceEquals(_active[index], bullet)) index++;
        }
        while (_births.Count > 0 && generation == _generation)
        {
            var node = _births.Dequeue();
            node.PendingBirth = false;
            if (node.IsAlive) node.Timeline?.DispatchDue();
        }
        // 没有成员的停止树不再占更新入口或名称索引。
        for (int index = _emitters.Count - 1; index >= 0; index--)
            if (_emitters[index].Timeline is null && !HasEmitterBullets(_emitters[index]))
            {
                _emitters[index].Retire();
                _emitters.RemoveAt(index);
            }
    }

	// 满额提示只输出一次，恢复容量后允许再次提示。
	private bool _limitReported;
	/// <summary>当前活动弹幕数量，不包含待释放节点。</summary>
	public int ActiveCount => _active.Count;
	/// <summary>全场活动子弹的只读视图。</summary>
	public IReadOnlyList<VBullet> ActiveBullets => _activeView;
	/// <summary>直接生成一颗采用完整初始化参数的弹幕。</summary>
	/// <param name="settings">完整初始化参数，位置为全局逻辑像素。</param>
	/// <returns>生成的子弹，容量不足时为空。</returns>
	public VBullet? Spawn(VBulletDefaultSet settings) => Spawn(settings, null);
	/// <summary>统一创建、配置并登记单颗子弹，可关联发射器。</summary>
	/// <param name="settings">完整出生参数，位置为全局逻辑像素。</param>
	/// <param name="emitter">所属发射器；玩家直接生成时为空。</param>
	/// <returns>成功生成的子弹；容量不足时为空。</returns>
	internal VBullet? Spawn(VBulletDefaultSet settings, VBulletEmitter? emitter)
	{
		// 本次完整校验返回的资源直接交给显示初始化，满额时也不跳过校验。
		var texture = VBullet.Validate(settings);
		if (!CanSpawn()) return null;
		// 直接生成仍经过统一初始化、登记和释放流程。
		var bullet = new VBullet();
		try
		{
			bullet.Configure(settings, texture);
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
	internal void Register(VBullet bullet, VBulletEmitter? emitter)
	{
		// 先建立出生时钟，失败时尚未把节点交给管理器。
		bullet.Timeline = GlobalEvent.CreateTimeline(bullet);
		bullet.SpawnPosition = bullet.Position = ToLocal(bullet.Position);
		bullet.Emitter = emitter;
		AddChild(bullet);
		_active.Add(bullet);
        RegisterBirth(bullet);
	}
	/// <summary>推进所有弹幕，并用目标相对位移检测连续碰撞。</summary>
	/// <param name="delta">经过的非负秒数。</param>
	/// <param name="player">敌弹的伤害目标。</param>
	/// <param name="boss">玩家弹的伤害目标。</param>
    /// <param name="ai">可选陪练AI，命中不会释放敌弹，默认空。</param>
    public void Advance(double delta, PlayerController player, BossController boss, AICharacter? ai = null)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        // Creator前序、批次生成序、成员出生序；碰撞注销不会跳过下一个成员。
        long generation = _generation;
        // 碰撞可切换阶段并追加新树；本步只推进开始时登记的树。
        int emitterCount = _emitters.Count;
        for (int emitterIndex = 0; emitterIndex < emitterCount; emitterIndex++)
        {
            var emitter = _emitters[emitterIndex];
            emitter.AdvanceUnits(VTimeline.SecondsToUnits(delta));
            emitter.Root.VisitMembers(node =>
            {
                if (generation != _generation || node.PendingBirth) return;
                if (node is VBullet bullet) AdvanceBullet(bullet, delta, player, boss, ai);
                else
                {
                    node.Advance(delta);
                    if (node.Expired) emitter.ReleaseNode(node);
                }
            });
            if (generation != _generation) return;
        }
        // 无Creator的直接生成子弹仍按出生顺序更新一次。
        for (int index = 0; index < _active.Count;)
        {
            var bullet = _active[index];
            if (bullet.Creator is null && !bullet.PendingBirth) AdvanceBullet(bullet, delta, player, boss, ai);
            if (index < _active.Count && ReferenceEquals(_active[index], bullet)) index++;
        }
    }

    /// <summary>推进一颗子弹并计算相对目标位移的连续碰撞。</summary>
    /// <param name="bullet">仍存活的实际子弹。</param>
    /// <param name="delta">非负逻辑秒数。</param>
    /// <param name="player">敌弹目标。</param>
    /// <param name="boss">玩家弹目标。</param>
    /// <param name="ai">可选陪练AI，使用独立受击判定。</param>
    private void AdvanceBullet(VBullet bullet, double delta, PlayerController player, BossController boss, AICharacter? ai)
    {
        if (bullet is VLaser laser)
        {
            AdvanceLaser(laser, delta, player, ai);
            return;
        }
        var start = bullet.GlobalPosition;
        bullet.Advance(delta);
        bullet.UpdateOutsideTime(delta);
        // AI不消费子弹；先完成独立判定，再执行原玩家/Boss的命中释放流程。
        if (ai is not null && bullet.Team == VBulletTeam.Enemy
            && bullet.SweptRegionHit(start, ai.PreviousPosition, ai.GlobalPosition,
                bullet.Radius + BattleConfig.PlayerRadius)) ai.TakeHit(bullet.Damage);
        var targetStart = bullet.Team == VBulletTeam.Enemy ? player.PreviousPosition : boss.PreviousPosition;
        var targetEnd = bullet.Team == VBulletTeam.Enemy ? player.GlobalPosition : boss.GlobalPosition;
        var radius = bullet.Radius + (bullet.Team == VBulletTeam.Enemy ? BattleConfig.PlayerRadius : boss.CollisionRadius);
        bool hit = bullet.SweptRegionHit(start, targetStart, targetEnd, radius)
            && (bullet.Team == VBulletTeam.Enemy ? player.Health.TakeDamage(bullet.Damage, player.Dodge.IsActive) : boss.TakeDamage(bullet.Damage));
        if (hit || bullet.Expired || bullet.OutsideExpired) Release(bullet);
    }

    /// <summary>统一注销子弹及其后代生成，允许外部场景退出调用。</summary>
    /// <param name="bullet">待注销子弹。</param>
    /// <param name="free">是否同时移出场景并释放。</param>
    internal void Release(VBullet bullet, bool free = true)
    {
        int index = _active.IndexOf(bullet);
        if (index < 0) return;
        bullet.Emitter?.ReleaseDescendants(bullet);
        bullet.Deactivate();
        _active.RemoveAt(index);
        bullet.Emitter = null;
        if (free)
        {
            RemoveChild(bullet);
            bullet.QueueFree();
        }
    }

	/// <summary>清理全部活动弹幕，同时重置满额提示。</summary>
    public void Clear()
    {
        _generation++;
        foreach (var emitter in _emitters) { emitter.Stop(); emitter.Retire(); }
        _emitters.Clear();
        while (_active.Count > 0) Release(_active[^1]);
        _births.Clear();
        _limitReported = false;
    }

    /// <summary>仅清除指定发射器的存活子弹，沿用统一注销入口。</summary>
    /// <param name="emitter">需要清弹的所属发射器。</param>
    public void ClearEmitter(VBulletEmitter emitter)
    {
        // 倒序删除不改变其余子弹的相对顺序。
        for (int index = _active.Count - 1; index >= 0; index--)
            if (ReferenceEquals(_active[index].Emitter, emitter)) Release(_active[index]);
    }
	/// <summary>管理器离场时同步清除全部批次引用。</summary>
	public override void _ExitTree() => Clear();
}
