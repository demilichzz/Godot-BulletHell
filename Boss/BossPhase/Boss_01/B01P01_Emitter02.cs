using Godot;
using System.Collections.Generic;

/// <summary>为Boss_01阶段01的发射器02构造一次16颗等角环形弹幕，并覆盖速度与颜色。</summary>
public sealed class B01P01_Emitter02 : BulletEmitter
{
	// 本批次参数快照，阶段用同一份模板决定发射间隔。
	private readonly BulletDefaultSet _template;
	/// <summary>保存阶段提供的参数快照；未传入时使用鳞弹预设。</summary>
	/// <param name="template">完整子弹参数，默认空时创建ScaleSet；位置和角度由本批次覆盖。</param>
	public B01P01_Emitter02(BulletDefaultSet? template = null)
	{
		_template = template ?? BulletDefaultSet.Get(BulletType.ScaleSet);
	}
	/// <summary>逐颗初始化环形敌弹，不依赖排列模式。</summary>
	/// <param name="manager">统一管理子弹生命周期的容器。</param>
	/// <param name="origin">本批次全局起点，单位为逻辑像素。</param>
	protected override void Build(BulletManager manager, Vector2 origin)
	{
        int ringCount = 16;
        float baseAngle = VMath.StandardizationAngleFloat(VMath.getB2PAngle());
        // 零基索引决定方向；16颗弹的角度间隔为π/8弧度，顺时针排列。
        for (int index = 0; index < ringCount; index++)
		{
			if (AddBullet(manager, _template with
			{
				Position = origin,
				AngleRadians = baseAngle + index * Mathf.Tau / ringCount,
				ColorIndex = 3,
				Speed = 300
			}) is null) break;
		}
		if (Bullets.Count > 0)
		{
			var targets = new List<Bullet>(Bullets);
			GlobalEvent.RegisterTimer(new VTimer(2000, 0, 0, VTimerType.Once, targets,
				aliveTargets =>
				{
					foreach (var target in aliveTargets)
						if (target is Bullet bullet) bullet.SetSpeed(100);
				}));
		}
	}
}
