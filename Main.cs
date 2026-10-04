using Godot;

/// <summary>装配战场、输入控制器与只读HUD，绘制场地背景和活动边界。</summary>
public partial class Main : Node2D
{
	/// <summary>入树前指定的 Boss 配置；为空时保留独立演示行为。</summary>
	public BossData? BossData { get; set; }
    /// <summary>入树前指定的AI创建配置，默认关闭。</summary>
    public AICharConfig AIConfig { get; set; } = new();
	/// <summary>当前战斗管理器，供所属 Stage 管理生命周期。</summary>
	public BattleManager Battle => _battle;
	// 统一驱动战斗的管理器。
	private readonly BattleManager _battle = new() { Name = "BattleManager" };
	/// <summary>配置操作并开始第一场战斗。</summary>
	public override void _Ready()
	{
		// 背景先于战斗节点加入，位于角色与弹幕下方。
		AddBackground();
		GameInput.EnsureBindings();
		_battle.Control = new BattleInputController(new GodotBattleInput(), _battle);
		AddChild(_battle);
		_battle.Initialize(this, BossData, AIConfig);
		// 使用独立画布层使文字始终位于战斗图形上方。
		var hud = new CanvasLayer();
		AddChild(hud);
		hud.AddChild(new BattleHud { Source = _battle });
	}
	/// <summary>以等比覆盖并居中裁切的方式铺设基准画面背景。</summary>
	private void AddBackground()
	{
		// 固定逻辑尺寸由窗口统一缩放；忽略原图最小尺寸，不截获输入。
		var background = new TextureRect
		{
			Name = "Background",
			Position = BattleConfig.Bounds.Position,
			Size = BattleConfig.Bounds.Size,
			Texture = GD.Load<Texture2D>("res://Assets/UI/UI_400x600_gamearea_4.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			TextureFilter = TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = -1
		};
		AddChild(background);
	}
	/// <summary>绘制玩家活动区域的浅蓝色圆形边界，半径与移动约束一致，线宽为2逻辑像素。</summary>
	public override void _Draw()
	{
		DrawArc(BattleConfig.ArenaCenter, BattleConfig.ArenaRadius, 0, Mathf.Tau, 256,
			new Color(0.45f, 0.85f, 1f, 0.85f), 2, true);
	}
}
