using Godot;
using System.Collections.Generic;

/// <summary>在六个随机圆心分别构造12颗等角环形弹幕。</summary>
public sealed class B01P02_Emitter01 : BulletEmitter
{
	// 本Boss单次环形发射的固定子弹数量。
	private const int RingCount = 24;
	// 本批次参数快照，阶段用同一份模板决定发射间隔。
	private readonly BulletDefaultSet _template;
	/// <summary>保存阶段提供的参数快照；未传入时使用鳞弹预设。</summary>
	/// <param name="template">完整子弹参数，默认空时创建ScaleSet；位置和角度由本批次覆盖。</param>
	public B01P02_Emitter01(BulletDefaultSet? template = null)
	{
		_template = template ?? BulletDefaultSet.Get(BulletType.ScaleSet);
	}
	/// <summary>逐颗初始化环形敌弹，不依赖排列模式。</summary>
	/// <param name="manager">统一管理子弹生命周期的容器。</param>
	/// <param name="origin">本批次全局起点，单位为逻辑像素。</param>
	protected override void Build(BulletManager manager, Vector2 origin)
	{
        for (int i = 0; i < 6; i++)
        {
			Vector2 center = VMath.PolarMove(
				GlobalEvent.GetBoss().Position,
				i * Mathf.Tau / 6 + VMath.getRandomDiff(Mathf.Tau / 12, RandomDiffMode.Center),
				200 + VMath.getRandomDiff(100)
				);
            for (int j = 0; j < 12; j++)
            {
				if (AddBullet(manager, _template with
				{
					Position = VMath.PolarMove(center, j * Mathf.Tau / 12, -20),
					AngleRadians = j * Mathf.Tau / 12,
					ColorIndex = 1,
					VisualScale = 2,
					Radius = 4
                }) is null) break;
            }
        }
    }
}
