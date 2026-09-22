using Godot;

/// <summary>组装当前战场、活动区域轮廓、键盘操作和简易中文状态显示。</summary>
public partial class Main : Node2D
{
	/// <summary>入树前指定的 Boss 配置；为空时保留独立演示行为。</summary>
	public BossData? BossData { get; set; }
	/// <summary>当前战斗管理器，供所属 Stage 管理生命周期。</summary>
	public BattleManager Battle => _battle;
	// 统一驱动战斗的管理器。
	private readonly BattleManager _battle = new() { Name = "BattleManager" };
	// 不参与碰撞的文字状态显示。
	private readonly Label _status = new() { Position = new Vector2(12, 10), MouseFilter = Control.MouseFilterEnum.Ignore };
	/// <summary>配置操作并开始第一场战斗。</summary>
	public override void _Ready()
	{
		// 背景先于战斗节点加入，位于角色与弹幕下方。
		AddBackground();
		GameInput.EnsureBindings();
		AddChild(_battle);
		_battle.Initialize(this, BossData);
		// 使用独立画布层使文字始终位于战斗图形上方。
		var hud = new CanvasLayer();
		AddChild(hud);
		hud.AddChild(_status);
		_status.AddThemeFontOverride("font", GD.Load<Font>("res://Assets/fonts/lxgl/LXGWWenKaiGBScreen.ttf"));
		_status.AddThemeFontSizeOverride("font_size", 18);
		_status.AddThemeColorOverride("font_outline_color", new Color(0.04f, 0.06f, 0.09f));
		_status.AddThemeConstantOverride("outline_size", 4);
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
	/// <summary>刷新生命、闪避冷却与结束提示。</summary>
	/// <param name="delta">渲染帧间隔秒数，不用于战斗模拟。</param>
	public override void _Process(double delta)
	{
		// 结束状态提示，仅在胜负后显示重开键。
		var result = _battle.State switch { BattleState.Victory => "胜利！按 R 重新开始", _ => "战斗中" };
		_status.Text = $"玩家 HP {_battle.Player.Health.Hp}/{BattleConfig.PlayerHp}    Boss HP {_battle.Boss.Hp}/{_battle.Boss.MaxHp}\n"
			+ $"闪避冷却 {_battle.Player.Dodge.Cooldown:0.0} 秒    时间 {_battle.Elapsed:0.0} 秒\n"
			+ "WASD / 方向键移动 · 空格闪避 · 自动攻击 · Esc 返回选择\n" + result;
	}
}
