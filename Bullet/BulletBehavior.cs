/// <summary>描述弹幕生成后的运动规则；无状态实例可共享。</summary>
public abstract class BulletBehavior
{
	/// <summary>推进子弹运动。</summary>
	/// <param name="bullet">待更新子弹，位置为父节点局部像素坐标。</param>
	/// <param name="delta">经过的非负秒数。</param>
	public abstract void Advance(Bullet bullet, double delta);
}
/// <summary>按当前速度进行直线运动。</summary>
public sealed class StraightBehavior : BulletBehavior
{
	/// <summary>推进直线位移。</summary>
	/// <param name="bullet">速度单位为像素/秒的子弹。</param>
	/// <param name="delta">经过的非负秒数。</param>
	public override void Advance(Bullet bullet, double delta) => bullet.Position += bullet.Velocity * (float)delta;
}
