using Godot;

/// <summary>为Boss_01阶段02的发射器02构造一次24颗等角环形弹幕。</summary>
public sealed class B01P02_Emitter02 : BulletEmitter
{
	// 本Boss单次环形发射的固定子弹数量。
	private const int RingCount = 24;
	// 本批次参数快照，阶段用同一份模板决定发射间隔。
	private readonly BulletSpawnData _template;
	/// <summary>保存阶段提供的参数快照；未传入时使用鳞弹预设。</summary>
	/// <param name="template">完整子弹参数，默认空时创建ScaleSet；位置和角度由本批次覆盖。</param>
	public B01P02_Emitter02(BulletSpawnData? template = null)
	{
		_template = template is null ? new BulletSpawnData(BulletType.ScaleSet) : template with { };
		_template.Validate();
	}
	/// <summary>逐颗初始化环形敌弹，不依赖排列模式。</summary>
	/// <param name="manager">统一管理子弹生命周期的容器。</param>
	/// <param name="origin">本批次全局起点，单位为逻辑像素。</param>
	protected override void Build(BulletManager manager, Vector2 origin)
	{
		// 零基索引决定方向；24颗弹的角度间隔为π/12弧度，顺时针排列。
		for (int index = 0; index < RingCount; index++)
		{
			if (AddBullet(manager, _template with
			{
				Position = origin,
				AngleRadians = index * Mathf.Tau / RingCount
			}) is null) break;
		}
	}
}
