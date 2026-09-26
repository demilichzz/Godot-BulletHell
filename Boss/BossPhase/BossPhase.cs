using Godot;
using System.Collections.Generic;

/// <summary>管理Boss阶段的移动、初始生命、绑定发射器及阶段时间线。</summary>
public abstract class BossPhase : IVTimelineOwner
{
    // 当前路段起点、累计秒数和阶段活动标记。
    private Vector2 _travelStart;
    private double _travelSeconds;
    // 仅保留仍有活动子弹的发射批次。
    private readonly List<BulletEmitter> _emitters = new();
    /// <summary>阶段激活时创建的逻辑时间线。</summary>
    public VTimeline? Timeline { get; private set; }
    /// <summary>阶段绑定的可重复发射器只读视图。</summary>
    public IReadOnlyList<BulletEmitter> Emitters { get; }
    /// <summary>缓存阶段发射器的只读视图。</summary>
    protected BossPhase() => Emitters = _emitters.AsReadOnly();
    /// <summary>绑定发射器并启动其独立发射时间线。</summary>
    /// <param name="emitter">本阶段使用的发射器。</param>
    /// <param name="boss">提供发射位置的Boss。</param>
    protected void BindEmitter(BulletEmitter emitter, BossController boss)
    {
        if (_emitters.Contains(emitter)) throw new System.InvalidOperationException("发射器不能重复绑定。");
        emitter.Start(boss, GlobalEvent.GetBulletManager());
        _emitters.Add(emitter);
    }
	/// <summary>阶段名称，用于状态显示。</summary>
	public abstract string Name { get; }
	/// <summary>当前移动目标，战场局部逻辑像素。</summary>
	public Vector2 MoveTarget { get; private set; } = new(640, 240);
	/// <summary>是否正在前往当前目标。</summary>
	public bool IsMoving { get; private set; }
	/// <summary>移动速度，逻辑像素/秒；子类可覆盖。</summary>
	protected virtual float MoveSpeed => 200;
	/// <summary>取得进入阶段时的目标，默认战场局部坐标(640,240)。</summary>
	/// <param name="boss">进入本阶段的Boss。</param>
	/// <returns>战场局部逻辑像素坐标。</returns>
	protected virtual Vector2 GetInitialMoveTarget(BossController boss) => new(640, 240);
	/// <summary>从Boss当前位置开始朝新目标移动。</summary>
	/// <param name="boss">提供当前路段起点的Boss。</param>
	/// <param name="target">新目标，战场局部逻辑像素。</param>
	protected void SetMoveTarget(BossController boss, Vector2 target)
	{
		_travelStart = boss.Position;
		_travelSeconds = 0;
		MoveTarget = target;
		IsMoving = boss.Position != target;
	}
	/// <summary>取得切入本阶段时的初始生命点数。</summary>
	/// <param name="boss">目标 Boss，用于读取最大生命点数。</param>
	/// <returns>本阶段初始生命点数，范围为1至Boss最大生命点数。</returns>
	public virtual int GetInitialHp(BossController boss) => boss.MaxHp;
	/// <summary>初始化阶段独占状态。</summary>
	/// <param name="boss">所属 Boss。</param>
	public virtual void Enter(BossController boss)
	{
		_emitters.Clear();
		Timeline = GlobalEvent.CreateTimeline(this);
		SetMoveTarget(boss, GetInitialMoveTarget(boss));
	}
	/// <summary>推进阶段行为。</summary>
	/// <param name="boss">所属 Boss。</param>
	/// <param name="delta">经过的非负秒数。</param>
	public virtual void Advance(BossController boss, double delta)
	{
		if (!double.IsFinite(delta) || delta < 0) throw new System.ArgumentOutOfRangeException(nameof(delta));
		if (Timeline is null || boss.Hp == 0) return;
		long units = VTimeline.SecondsToUnits(delta);
		Timeline.AdvanceUnits(units);
        // Boss先完成本步移动，再推进跟随它的VNode树。
        if (IsMoving)
        {
            _travelSeconds += delta;
            boss.Position = _travelStart.MoveToward(MoveTarget, (float)(_travelSeconds * MoveSpeed));
            if (boss.Position == MoveTarget) IsMoving = false;
        }
        foreach (var emitter in _emitters) emitter.AdvanceUnits(units);
	}
	/// <summary>判断是否转入列表中的下一阶段。</summary>
	/// <param name="boss">所属 Boss。</param>
	/// <returns>是否结束当前阶段，默认不结束。</returns>
	public virtual bool ShouldEnd(BossController boss) => false;
	/// <summary>释放阶段状态，在切换或终止时执行一次。</summary>
	/// <param name="boss">所属 Boss。</param>
	public virtual void Exit(BossController boss)
    {
        IsMoving = false;
        Timeline?.Cancel();
        Timeline = null;
        foreach (var emitter in _emitters) emitter.Stop();
        _emitters.Clear();
    }
}
