using Godot;
using System;

/// <summary>在六个随机圆心分别构造12颗等角环形弹幕。</summary>
public sealed class B01P02_Emitter01 : BulletEmitter
{
	/// <summary>首发等待一秒，随后每秒在六个随机圆心生成弹幕。</summary>
	/// <param name="manager">统一管理子弹生命周期的容器。</param>
	/// <param name="owner">提供当前局部位置的Boss。</param>
	protected override void Build(BulletManager manager, BossController owner)
	{
		var template = BulletDefaultSet.Get(BulletType.ScaleSet);
		Timeline!.Repeat(1000, 1000, null, () =>
		{
			for (int i = 0; i < 6; i++)
			{
				Vector2 center = VMath.PolarMove(
					owner.Position,
					i * Math.Tau / 6 + VMath.getRandomDiff(Math.Tau / 12, RandomDiffMode.Center),
					200 + VMath.getRandomDiff(100)
				);
				for (int j = 0; j < 12; j++)
				{
					if (AddBullet(manager, template with
					{
						Position = VMath.PolarMove(center, j * Math.Tau / 12, -20),
						AngleRadians = j * Math.Tau / 12,
						ColorIndex = 1,
						VisualScale = 2,
						Radius = 4
					}) is null) break;
				}
			}
		});
	}
}
