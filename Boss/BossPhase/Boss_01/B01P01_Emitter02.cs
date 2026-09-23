using Godot;
using System;

/// <summary>每秒构造16颗瞄准环形弹幕，并在出生两秒后降速。</summary>
public sealed class B01P01_Emitter02 : BulletEmitter
{
	/// <summary>首发等待一秒，随后每秒生成一圈并安排子弹变速。</summary>
	/// <param name="manager">统一管理子弹生命周期的容器。</param>
	/// <param name="owner">提供发射时全局起点的Boss。</param>
	protected override void Build(BulletManager manager, BossController owner)
	{
		var template = BulletDefaultSet.Get(BulletType.ScaleSet);
		Timeline!.Repeat(1000, 1000, null, () =>
		{
			// 每轮重新读取当前瞄准角和Boss位置。
			double baseAngle = VMath.getB2PAngle();
			var queue = new BulletQueue(owner.GlobalPosition, template with
			{
				AngleRadians = baseAngle,
				ColorIndex = 3,
				Speed = 300
			}, new BulletQueueSet
			{
				Amount = 16,
				AngleAdd = Math.Tau / 16
			});
			AddQueue(manager, queue);
			// 已出生子弹的动作独立于发射器继续运行。
			foreach (var bullet in queue.BulletList)
				bullet.Timeline!.After(2000, () => bullet.SetSpeed(100));
		});
	}
}
