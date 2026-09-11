using Godot;

/// <summary>定义 Boss 阶段的进入、更新、退出与结束条件。</summary>
public abstract class BossPhase
{
	/// <summary>阶段名称，用于状态显示。</summary>
	public abstract string Name { get; }
	/// <summary>初始化阶段独占状态。</summary>
	/// <param name="boss">所属 Boss。</param>
	public virtual void Enter(BossController boss) { }
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
	public virtual void Exit(BossController boss) { }
}
/// <summary>保留原有每秒24发环形弹幕的默认阶段。</summary>
public sealed class RingBossPhase : BossPhase
{
	// 本次进入阶段使用的发射时钟。
	private BulletEmitter _emitter = null!;
	/// <summary>默认阶段显示名称。</summary>
	public override string Name => "环形弹幕";
	/// <summary>每次进入重新开始发射计时。</summary>
	/// <param name="boss">所属 Boss。</param>
	public override void Enter(BossController boss) => _emitter = new(BattleConfig.BossInterval, new RingPattern(), BulletTeam.Enemy);
	/// <summary>按 Boss 全局位置发射，已发弹幕不跟随其变换。</summary>
	/// <param name="boss">提供全局发射点与独立容器的 Boss。</param>
	/// <param name="delta">经过的非负秒数。</param>
	public override void Advance(BossController boss, double delta) => _emitter.Advance(delta, boss.BulletParent, boss.GlobalPosition);
}
