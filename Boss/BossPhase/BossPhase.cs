using Godot;
using System.Collections.Generic;

/// <summary>管理Boss阶段的移动、初始生命、活动批次及阶段计时器。</summary>
public abstract class BossPhase
{
    // 当前路段起点、累计秒数和阶段活动标记。
    private Vector2 _travelStart;
    private double _travelSeconds;
    private bool _active;
    // 仅保留仍有活动子弹的发射批次。
    private readonly List<BulletEmitter> _emitters = new();
    // 阶段退出时取消的计时器，不包含子弹自身行为。
    private readonly List<VTimer> _timers = new();
    /// <summary>记录需要随阶段退出取消的计时器。</summary>
    /// <param name="timer">已注册的阶段计时器。</param>
    protected void TrackTimer(VTimer timer)
    {
        _timers.RemoveAll(existing => existing.IsFinished);
        _timers.Add(timer);
    }
    /// <summary>阶段持有的活动批次只读视图。</summary>
    public IReadOnlyList<BulletEmitter> Emitters => _emitters.AsReadOnly();
    /// <summary>记录一次成功产生子弹的批次。</summary>
    /// <param name="emitter">已完成发射的批次。</param>
    protected void TrackEmitter(BulletEmitter emitter)
    {
        if (emitter.Bullets.Count > 0 && !_emitters.Contains(emitter)) _emitters.Add(emitter);
    }
    /// <summary>移除已无活动子弹的批次，避免历史对象积累。</summary>
    protected void PruneEmitters() => _emitters.RemoveAll(emitter => emitter.Bullets.Count == 0);
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
		_active = true;
		SetMoveTarget(boss, GetInitialMoveTarget(boss));
	}
	/// <summary>推进阶段行为。</summary>
	/// <param name="boss">所属 Boss。</param>
	/// <param name="delta">经过的非负秒数。</param>
	public virtual void Advance(BossController boss, double delta)
	{
		if (!double.IsFinite(delta) || delta < 0) throw new System.ArgumentOutOfRangeException(nameof(delta));
		if (!_active || boss.Hp == 0) return;
		PruneEmitters();
		if (!IsMoving) return;
		_travelSeconds += delta;
		boss.Position = _travelStart.MoveToward(MoveTarget, (float)(_travelSeconds * MoveSpeed));
		if (boss.Position == MoveTarget) IsMoving = false;
	}
	/// <summary>判断是否转入列表中的下一阶段。</summary>
	/// <param name="boss">所属 Boss。</param>
	/// <returns>是否结束当前阶段，默认不结束。</returns>
	public virtual bool ShouldEnd(BossController boss) => false;
	/// <summary>释放阶段状态，在切换或终止时执行一次。</summary>
	/// <param name="boss">所属 Boss。</param>
	public virtual void Exit(BossController boss)
    {
        _active = false;
        IsMoving = false;
        foreach (var timer in _timers) timer.Cancel();
        _timers.Clear();
        _emitters.Clear();
    }
}
