using Godot;
using System.Collections.Generic;

/// <summary>定义Boss阶段生命周期，持有需要访问的活动发射批次。</summary>
public abstract class BossPhase
{
    // 仅保留仍有活动子弹的发射批次。
    private readonly List<BulletEmitter> _emitters = new();
    /// <summary>阶段持有的活动批次只读视图。</summary>
    public IReadOnlyList<BulletEmitter> Emitters => _emitters.AsReadOnly();
    /// <summary>记录一次成功产生子弹的批次。</summary>
    /// <param name="emitter">已完成发射的批次。</param>
    protected void TrackEmitter(BulletEmitter emitter)
    {
        if (emitter.Bullets.Count > 0) _emitters.Add(emitter);
    }
    /// <summary>移除已无活动子弹的批次，避免历史对象积累。</summary>
    protected void PruneEmitters() => _emitters.RemoveAll(emitter => emitter.Bullets.Count == 0);
	/// <summary>阶段名称，用于状态显示。</summary>
	public abstract string Name { get; }
	/// <summary>初始化阶段独占状态。</summary>
	/// <param name="boss">所属 Boss。</param>
	public virtual void Enter(BossController boss) => _emitters.Clear();
	/// <summary>推进阶段行为。</summary>
	/// <param name="boss">所属 Boss。</param>
	/// <param name="delta">经过的非负秒数。</param>
	public abstract void Advance(BossController boss, double delta);
	/// <summary>判断是否转入列表中的下一阶段。</summary>
	/// <param name="boss">所属 Boss。</param>
	/// <returns>是否结束当前阶段，默认不结束。</returns>
	public virtual bool ShouldEnd(BossController boss) => false;
	/// <summary>释放阶段状态，在切换或终止时执行一次。</summary>
	/// <param name="boss">所属 Boss。</param>
	public virtual void Exit(BossController boss) => _emitters.Clear();
}
