/// <summary>描述节点和子弹共用的运动规则；无状态实例可共享。</summary>
public abstract class BulletBehavior
{
	/// <summary>推进节点或子弹的共用运动。</summary>
	/// <param name="bullet">待更新对象，位置为场景容器局部像素坐标。</param>
	/// <param name="delta">经过的非负秒数。</param>
	public abstract void Advance(VNode bullet, double delta);
}
/// <summary>按当前实际速度推进节点或子弹的直线位移。</summary>
public sealed class StraightBehavior : BulletBehavior
{
	/// <summary>推进直线位移。</summary>
	/// <param name="bullet">实际速度单位为像素/秒的节点或子弹。</param>
	/// <param name="delta">经过的非负秒数。</param>
	public override void Advance(VNode bullet, double delta) => bullet.Position += bullet.Velocity * (float)delta;
}
