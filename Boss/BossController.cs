using Godot;
using System;
using System.Collections.Generic;

/// <summary>管理 Boss 生命、图集动画、跨阶段时间线与顺序阶段，由战斗管理器更新。</summary>
public partial class BossController : Node2D, IVTimelineOwner
{
	/// <summary>从首次启动阶段起持续到战斗结束的 Boss 时间线。</summary>
	public VTimeline? Timeline { get; private set; }
	/// <summary>当前生命点数。</summary>
	public int Hp { get; private set; } = BattleConfig.BossHp;
	/// <summary>当前 Boss 的最大生命点数。</summary>
	public int MaxHp { get; private set; } = BattleConfig.BossHp;
	/// <summary>当前 Boss 的碰撞半径，单位为逻辑像素。</summary>
	public float CollisionRadius { get; private set; } = BattleConfig.BossRadius;
	/// <summary>当前 Boss 的显示名称。</summary>
	public string DisplayName { get; private set; } = "环形守卫";
	// 独立配置的战斗贴图；未配置时加载Boss_01原图。
	private Texture2D? _texture;
	// 图集行列及每秒帧数；未配置时使用Boss_01四帧默认值。
	private int _hframes = 2, _vframes = 2;
	private double _animationFps = 4;
	// 当前循环内已经过的秒数，重建实例时归零。
	private double _animationSeconds;
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
		_hframes = data.Hframes;
		_vframes = data.Vframes;
		_animationFps = data.AnimationFps;
	}
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
	private List<BossPhase> _phases = new() { new B01_Phase01(), new B01_Phase02(), new B01_Phase03() };
	private int _phaseIndex;
	// 独立贴图，缩放不影响碰撞半径。
	private readonly Sprite2D _sprite = new() { Name = "Sprite" };
	// 防止节点生命周期重复启动阶段。
	private bool _phasesStarted;
	/// <summary>切换到相邻阶段并设置该阶段初始生命。</summary>
	/// <param name="direction">阶段方向，-1为上一阶段，1为下一阶段。</param>
	/// <returns>成功切换时为真；首尾阶段或已结束时为假，无效方向抛出异常。</returns>
	public bool TrySwitchAdjacentPhase(int direction)
	{
		if (direction != -1 && direction != 1) throw new ArgumentOutOfRangeException(nameof(direction));
		if (CurrentPhase is null || Hp == 0) return false;
		// 计算目标阶段索引并检查首尾边界。
		int targetIndex = _phaseIndex + direction;
		if (targetIndex < 0 || targetIndex >= _phases.Count) return false;
		// 读取目标阶段及其相对最大生命的初始值。
		var targetPhase = _phases[targetIndex];
		// 校验目标阶段初始生命位于合法范围。
		int targetHp = targetPhase.GetInitialHp(this);
		if (targetHp < 1 || targetHp > MaxHp) throw new InvalidOperationException("阶段初始生命超出Boss范围。");
		ExitCurrentPhase();
		_phaseIndex = targetIndex;
		Hp = targetHp;
		HealthChanged?.Invoke(Hp);
		EnterPhase();
		return true;
	}
	/// <summary>在入树前配置 Boss 的贴图倍率。</summary>
	/// <param name="visualScale">正数有限贴图倍率，默认3。</param>
	public void Initialize(float visualScale = BattleConfig.BossScale)
	{
		if (!float.IsFinite(visualScale) || visualScale <= 0) throw new ArgumentOutOfRangeException(nameof(visualScale));
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
	/// <summary>加载图像；阶段由战斗管理器在双方实体就绪后启动。</summary>
	public override void _Ready()
	{
		_sprite.Texture = _texture ?? GD.Load<Texture2D>("res://Assets/Units/Boss_01.png");
		_sprite.Hframes = _hframes;
		_sprite.Vframes = _vframes;
		_sprite.Frame = 0;
		_animationSeconds = 0;
		_sprite.Centered = true;
		// 贴图绘制在父节点轮廓下方，避免遮住真实碰撞范围。
		_sprite.ShowBehindParent = true;
		_sprite.TextureFilter = TextureFilterEnum.Nearest;
		AddChild(_sprite);
	}
	/// <summary>在双方实体和全局战斗服务准备后启动首个阶段。</summary>
	internal void StartPhases()
	{
		if (_phasesStarted) return;
		_phasesStarted = true;
		Timeline = GlobalEvent.CreateTimeline(this);
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
	/// <summary>推进图集动画与阶段并在条件满足时切换，最后阶段保持运行。</summary>
	/// <param name="delta">经过的非负有限秒数。</param>
	public void Advance(double delta)
	{
		if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
		PreviousPosition = GlobalPosition;
		if (Hp == 0 || CurrentPhase is null) return;
		AdvanceAnimation(delta);
		UpdatePhase();
		Timeline?.AdvanceUnits(VTimeline.SecondsToUnits(delta));
		CurrentPhase!.Advance(this, delta);
	}
    /// <summary>立即处理满足条件的阶段切换，单次伤害可跨过多个阈值。</summary>
    private void UpdatePhase()
    {
        // 保持阶段有序进入和退出，旧阶段时间线在步末发射前取消。
        while (Hp > 0 && CurrentPhase is not null && _phaseIndex + 1 < _phases.Count && CurrentPhase.ShouldEnd(this))
        {
			ExitCurrentPhase();
            _phaseIndex++;
            EnterPhase();
        }
    }
	/// <summary>按累计秒数推进逐行排列的图集帧，保留余量并支持一次跨越多个循环。</summary>
	/// <param name="delta">本次更新的非负有限秒数。</param>
	private void AdvanceAnimation(double delta)
	{
		// 总帧数与循环周期，周期单位为秒；单帧无需更新。
		int frameCount = _hframes * _vframes;
		if (frameCount == 1 || _animationFps <= 0) return;
		double duration = frameCount / _animationFps;
		_animationSeconds = (_animationSeconds + delta % duration) % duration;
		// 消除60Hz累计在换帧边界附近的浮点误差，索引仍限制在图集内。
		_sprite.Frame = (int)Math.Floor(_animationSeconds * _animationFps + 1e-9) % frameCount;
	}
	/// <summary>施加伤害并立即处理血线切阶段；归零时只执行一次死亡与退出。</summary>
	/// <param name="damage">正整数伤害点数，非正数忽略。</param>
	/// <returns>是否造成有效伤害。</returns>
	public bool TakeDamage(int damage)
	{
		if (Hp == 0 || damage <= 0) return false;
		Hp = Math.Max(0, Hp - damage);
		HealthChanged?.Invoke(Hp);
		if (Hp == 0) { GlobalEvent.TryNotifyTargetDestroyed(this); Stop(); Died?.Invoke(); }
        else UpdatePhase();
		return true;
	}
	/// <summary>只退出当前阶段，切换阶段时保留 Boss 自身的时间线。</summary>
	private void ExitCurrentPhase()
	{
		// 清空引用后回调，避免重入时重复退出。
		var phase = CurrentPhase;
		CurrentPhase = null;
		phase?.Exit(this);
	}
	/// <summary>结束 Boss 生命周期及当前阶段，允许重复调用。</summary>
	public void Stop()
	{
		ExitCurrentPhase();
		Timeline?.Cancel();
		Timeline = null;
	}
	/// <summary>离开场景时结束尚未退出的阶段。</summary>
	public override void _ExitTree() => Stop();
}
