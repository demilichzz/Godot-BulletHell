using Godot;
using System;
using System.Collections.Generic;

/// <summary>管理 Boss 生命、贴图、碰撞轮廓与顺序阶段，默认由战斗管理器更新。</summary>
public partial class BossController : Node2D
{
	/// <summary>当前生命点数。</summary>
	public int Hp { get; private set; } = BattleConfig.BossHp;
	/// <summary>当前 Boss 的最大生命点数。</summary>
	public int MaxHp { get; private set; } = BattleConfig.BossHp;
	/// <summary>当前 Boss 的碰撞半径，单位为逻辑像素。</summary>
	public float CollisionRadius { get; private set; } = BattleConfig.BossRadius;
	/// <summary>当前 Boss 的显示名称。</summary>
	public string DisplayName { get; private set; } = "环形守卫";
	// 独立配置的战斗贴图；旧接口未配置时加载原图。
	private Texture2D? _texture;
	/// <summary>在入树前应用独立配置。</summary>
	/// <param name="data">已经定义贴图、生命与碰撞尺寸的 Boss 配置。</param>
	public void Configure(BossData data)
	{
		if (IsInsideTree()) throw new InvalidOperationException("Boss 配置须在入树前应用。");
		data.Validate();
		Hp = MaxHp = data.MaxHp;
		CollisionRadius = data.CollisionRadius;
		DisplayName = data.DisplayName;
		_texture = data.Texture;
	}
	/// <summary>独立弹幕容器。</summary>
	public Node2D BulletParent { get; private set; } = null!;
	/// <summary>当前阶段，尚未进入或已结束时为空。</summary>
	public BossPhase? CurrentPhase { get; private set; }
	/// <summary>本物理步开始时的全局位置，单位为像素。</summary>
	public Vector2 PreviousPosition { get; private set; }
	/// <summary>生命变化事件，参数为剩余点数。</summary>
	public event Action<int>? HealthChanged;
	/// <summary>首次死亡事件。</summary>
	public event Action? Died;
	/// <summary>进入新阶段的通知，参数为阶段实例。</summary>
	public event Action<BossPhase>? PhaseChanged;
	// 有序阶段列表与当前索引。
	private List<BossPhase> _phases = new() { new Boss_01Phase() };
	private int _phaseIndex;
	// 独立贴图，缩放不影响碰撞半径。
	private readonly Sprite2D _sprite = new() { Name = "Sprite" };
	/// <summary>初始化容器与外观，保留旧版签名。</summary>
	/// <param name="bulletParent">接收子弹的独立父节点。</param>
	/// <param name="visualScale">正数有限贴图倍率，默认3。</param>
	public void Initialize(Node2D bulletParent, float visualScale = BattleConfig.BossScale)
	{
		if (!float.IsFinite(visualScale) || visualScale <= 0) throw new ArgumentOutOfRangeException(nameof(visualScale));
		BulletParent = bulletParent ?? throw new ArgumentNullException(nameof(bulletParent));
		_sprite.Scale = Vector2.One * visualScale;
	}
	/// <summary>在入树前设置非空有序阶段列表。</summary>
	/// <param name="phases">至少一个非空阶段；每场战斗使用新的实例。</param>
	public void SetPhases(IEnumerable<BossPhase> phases)
	{
		if (IsInsideTree()) throw new InvalidOperationException("阶段须在入树前配置。");
		// 先验证副本，避免无效配置污染原列表。
		var configured = new List<BossPhase>(phases);
		if (configured.Count == 0 || configured.Exists(phase => phase is null)) throw new ArgumentException("阶段不可为空。", nameof(phases));
		_phases = configured;
	}
	/// <summary>加载图像并进入首个阶段。</summary>
	public override void _Ready()
	{
		_sprite.Texture = _texture ?? GD.Load<Texture2D>("res://Assets/Units/Boss_01.png");
		_sprite.Centered = true;
		// 贴图绘制在父节点轮廓下方，避免遮住真实碰撞范围。
		_sprite.ShowBehindParent = true;
		_sprite.TextureFilter = TextureFilterEnum.Nearest;
		AddChild(_sprite);
		EnterPhase();
	}
	/// <summary>按实际碰撞半径绘制橙红色圆形轮廓，线宽为2逻辑像素，不受贴图倍率影响。</summary>
	public override void _Draw()
	{
		DrawArc(Vector2.Zero, CollisionRadius, 0, Mathf.Tau, 128, Colors.OrangeRed, 2, true);
	}
	/// <summary>进入当前索引对应阶段并通知。</summary>
	private void EnterPhase()
	{
		CurrentPhase = _phases[_phaseIndex];
		CurrentPhase.Enter(this);
		PhaseChanged?.Invoke(CurrentPhase);
	}
	/// <summary>推进阶段并在条件满足时切换，最后阶段保持运行。</summary>
	/// <param name="delta">经过的非负秒数。</param>
	public void Advance(double delta)
	{
		PreviousPosition = GlobalPosition;
		if (Hp == 0 || CurrentPhase is null) return;
		if (_phaseIndex + 1 < _phases.Count && CurrentPhase.ShouldEnd(this))
		{
			Stop();
			_phaseIndex++;
			EnterPhase();
		}
		CurrentPhase!.Advance(this, delta);
	}
	/// <summary>施加伤害，死亡与阶段退出只发生一次。</summary>
	/// <param name="damage">正整数伤害点数，非正数忽略。</param>
	/// <returns>是否造成有效伤害。</returns>
	public bool TakeDamage(int damage)
	{
		if (Hp == 0 || damage <= 0) return false;
		Hp = Math.Max(0, Hp - damage);
		HealthChanged?.Invoke(Hp);
		if (Hp == 0) { Stop(); Died?.Invoke(); }
		return true;
	}
	/// <summary>终止当前阶段，允许重复调用。</summary>
	public void Stop()
	{
		// 清空引用后回调，避免重入时重复退出。
		var phase = CurrentPhase;
		CurrentPhase = null;
		phase?.Exit(this);
	}
	/// <summary>离开场景时结束尚未退出的阶段。</summary>
	public override void _ExitTree() => Stop();
}
