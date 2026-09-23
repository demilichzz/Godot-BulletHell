using Godot;
using System;

/// <summary>每秒构造24颗等角环形弹幕。</summary>
public sealed class B01P01_Emitter01 : BulletEmitter
{
	/// <summary>首发等待一秒，随后每秒按当前Boss位置生成一圈。</summary>
	/// <param name="manager">统一管理子弹生命周期的容器。</param>
	/// <param name="owner">提供发射时全局起点的Boss。</param>
	protected override void Build(BulletManager manager, BossController owner)
	{
		// 发射参数与周期均在当前Build内定义。
		var template = BulletDefaultSet.Get(BulletType.ScaleSet);
		Timeline!.Repeat(1000, 1000, null, () =>
		{
			AddQueue(manager, new BulletQueue(owner.GlobalPosition, template, new BulletQueueSet
			{
				Amount = 24,
				AngleAdd = Math.Tau / 24
			}));
		});
	}
}
