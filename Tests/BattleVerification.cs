using Godot;
using System;

/// <summary>独立运行的战斗回归验证节点，不进入正式游戏场景。</summary>
public partial class BattleVerification : Node
{
	// 累计通过的断言数量。
	private int _checks;
	/// <summary>等待场景入树完成后运行同步物理步验证。</summary>
	public override void _Ready() => Callable.From(Run).CallDeferred();
	/// <summary>检查条件并报告明确的失败原因。</summary>
	/// <param name="condition">预期为真的条件。</param>
	/// <param name="message">失败时显示的说明。</param>
	private void Check(bool condition, string message)
	{
		if (!condition) throw new Exception(message);
		_checks++;
	}
	/// <summary>创建禁止自动更新的隔离战斗。</summary>
	/// <param name="world">返回独立战场节点，调用者负责释放。</param>
	/// <returns>可手动推进的战斗管理器。</returns>
	private BattleManager CreateBattle(out Node2D world)
	{
		world = new Node2D();
		AddChild(world);
		// 单一管理器以显式 Step 调用推进，避免渲染时序干扰测试。
		var battle = new BattleManager();
		world.AddChild(battle);
		battle.Initialize(world);
		battle.SetPhysicsProcess(false);
		return battle;
	}
	/// <summary>执行回归集，失败时以非零状态退出。</summary>
	private void Run()
	{
		try
		{
			VerifyOriginal();
			VerifyPlayer();
			VerifyCombat();
			VerifyPhases();
            VerifyBatches();
			VerifyInput();
			GD.Print($"PASS: {_checks} battle regression assertions");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}
	/// <summary>隔离验证原环形参数、贴图与旧接口。</summary>
	private void VerifyOriginal()
	{
		// 测试场地、独立容器及兼容 Boss。
		var world = new Node2D();
		AddChild(world);
		var container = new BulletManager();
		world.AddChild(container);
		var boss = new Boss { Position = BattleConfig.BossSpawn };
		boss.Initialize(container);
		world.AddChild(boss);
		boss.SetPhysicsProcess(false);
		Check(boss.Position == new Vector2(640, 250), "Boss位置");
		Check(boss.GetNode<Sprite2D>("Sprite").Scale == Vector2.One * 3, "Boss倍率3");
		boss.Advance(0.99);
		Check(container.GetChildCount() == 0, "第一秒前不能发射");
		boss.Advance(0.01);
		Check(container.GetChildCount() == 24, "第一秒24发");
		// 每颗弹的序号对应15度间距。
		for (int index = 0; index < 24; index++)
		{
			var bullet = container.GetChild<Bullet>(index);
			var sprite = bullet.GetNode<Sprite2D>("Sprite");
			Check(Mathf.IsEqualApprox(bullet.AngleDegrees, index * 15), "环形角度");
			Check(bullet.Speed == 180 && bullet.LifetimeSeconds == 4, "原弹速寿命");
			Check(sprite.Scale == Vector2.One * 3 && sprite.Frame == 0 && sprite.Centered, "原弹幕外观");
			Check(sprite.TextureFilter == CanvasItem.TextureFilterEnum.Nearest, "最近邻");
		}
		boss.Advance(2.5);
		Check(container.GetChildCount() == 72, "大步长补发");
		boss.Advance(0.5);
		Check(container.GetChildCount() == 96, "发射余量");
		// 所有实际子弹由管理器推进，外部初始化入口不再自行更新。
        var sampleBatch = new SingleBulletEmitter(new BulletSpawnData
        {
            AngleDegrees = 90, Speed = 100, LifetimeSeconds = 0.25f, VisualScale = 2
        });
        sampleBatch.Emit(container, new Vector2(-1000, -1000));
        var sample = sampleBatch.Bullets[0];
        Check(!sample.IsPhysicsProcessing(), "子弹不自行推进");
        Check(sample.GetNode<Sprite2D>("Sprite").Scale == Vector2.One * 2, "自定义倍率");
        container.Clear();
        Check(sampleBatch.Bullets.Count == 0, "清场注销批次");
        // 枚举图集颜色，迁移后仍逐颗保留外观配置。
        for (int color = 0; color < 10; color++)
        {
            var colored = new SingleBulletEmitter(new BulletSpawnData { ColorIndex = color, VisualScale = 1 });
            colored.Emit(container, Vector2.Zero);
            Check(colored.Bullets[0].GetNode<Sprite2D>("Sprite").Frame == color, "图集颜色");
        }
        // 无效参数应在创建节点之前失败，不污染任一列表。
        foreach (var invalidData in new[] { new BulletSpawnData { ColorIndex = 10 },
            new BulletSpawnData { VisualScale = float.NaN }, new BulletSpawnData { Speed = -1 } })
        {
            var invalidBatch = new SingleBulletEmitter(invalidData);
            var before = container.GetChildCount();
            try { invalidBatch.Emit(container, Vector2.Zero); Check(false, "无效参数未校验"); }
            catch (ArgumentOutOfRangeException) { _checks++; }
            Check(invalidBatch.Bullets.Count == 0 && container.GetChildCount() == before, "无效参数不留节点");
        }
        world.Free();
	}
	/// <summary>验证移动、闪避与生命组件。</summary>
	private void VerifyPlayer()
	{
		// 用精确时间推进独立组件，避免输入设备干扰。
		var movement = new PlayerMovement();
		Check(Mathf.IsEqualApprox(movement.ReadDirection(Vector2.One).Length(), 1), "斜向归一化");
		// 圆心和圆内点不移动，四轴和斜向越界均沿径向投影到395像素。
		var center = new Vector2(640, 400);
		Check(movement.Clamp(center) == center, "圆心不变");
		Check(movement.Clamp(center + new Vector2(30, -40)) == center + new Vector2(30, -40), "圆内不变");
		foreach (var direction in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right, Vector2.One.Normalized(), new Vector2(-1, 1).Normalized() })
		{
			Check(movement.Clamp(center + direction * 1000).DistanceTo(center + direction * 395) < 0.001, "圆形径向约束");
			Check(movement.Clamp(center + direction * 395).DistanceTo(center + direction * 395) < 0.001, "圆周位置稳定");
		}
		var dodge = new PlayerDodge();
		Check(dodge.TryStart(Vector2.Zero) && dodge.Direction == Vector2.Up, "默认向上闪避");
		Check(!dodge.TryStart(Vector2.Right), "闪避冷却阻止重复");
		dodge.Advance(0.15);
		Check(!dodge.IsActive && Math.Abs(dodge.Cooldown - 0.85) < 1e-6, "闪避与冷却独立");
		dodge.Advance(0.85);
		Check(dodge.TryStart(Vector2.Right), "冷却恢复");
				// 60Hz下不允许浮点余量额外延长闪避、冷却和无敌。
		var fixedDodge = new PlayerDodge();
		fixedDodge.TryStart(Vector2.Up);
		for (int tick = 0; tick < 9; tick++) fixedDodge.Advance(1.0 / 60);
		Check(!fixedDodge.IsActive, "9个固定步结束闪避");
		for (int tick = 9; tick < 60; tick++) fixedDodge.Advance(1.0 / 60);
		Check(fixedDodge.Cooldown == 0, "60个固定步恢复冷却");
		var fixedHealth = new PlayerHealth();
		fixedHealth.TakeDamage(1, false);
		for (int tick = 0; tick < 60; tick++) fixedHealth.Advance(1.0 / 60);
		Check(fixedHealth.TakeDamage(1, false), "60个固定步结束受击无敌");
		// 独立生命组件与事件计数。
		var health = new PlayerHealth();
		var deaths = 0;
		var changes = 0;
		health.Died += () => deaths++;
		health.HealthChanged += hp => changes++;
		Check(!health.TakeDamage(1, true), "闪避无敌");
		Check(health.TakeDamage(1, false) && !health.TakeDamage(1, false), "受击防重复");
		health.Advance(1);
		Check(health.TakeDamage(99, false) && health.Hp == 0, "生命下限");
		Check(!health.TakeDamage(1, false) && deaths == 1 && changes == 2, "死亡事件一次");
		var battle = CreateBattle(out var world);
		battle.Step(0.1, Vector2.One, false);
		Check(Math.Abs(battle.Player.Position.DistanceTo(BattleConfig.PlayerSpawn) - 24) < 0.001, "实际移动速度");
		battle.Step(0.15, Vector2.Zero, true);
		Check(battle.Player.Position.DistanceTo(BattleConfig.PlayerSpawn + Vector2.One.Normalized() * 132) < 0.01, "无输入沿上次方向闪避");
		// 单独验证普通移动及闪避无法越过圆形边界，位置单位为像素。
		battle.Restart();
		battle.Player.Position = center + Vector2.Right * 390;
		battle.Step(0.1, Vector2.Right, false);
		Check(battle.Player.Position.DistanceTo(center + Vector2.Right * 395) < 0.001, "普通移动圆边界");
		battle.Restart();
		battle.Player.Position = center + Vector2.One.Normalized() * 390;
		battle.Step(0.15, Vector2.One, true);
		Check(battle.Player.Position.DistanceTo(center + Vector2.One.Normalized() * 395) < 0.001, "闪避圆边界");
		battle.Restart();
		Check(battle.Player.Position == new Vector2(640, 600), "新玩家出生位置");
		world.Free();
	}
	/// <summary>验证连续碰撞、自动瞄准、胜负及重开。</summary>
	private void VerifyCombat()
	{
		Check(BulletManager.SweptHit(new Vector2(-100, 0), new Vector2(100, 0), 1), "高速穿越");
		Check(!BulletManager.SweptHit(new Vector2(-100, 2), new Vector2(100, 2), 1), "擦身未命中");
		Check(BulletManager.SweptHit(Vector2.Zero, Vector2.Zero, 1), "静止重叠");
		// 隔离攻击发射，验证首次时机与瞄准方向。
		var battle = CreateBattle(out var world);
		battle.Step(0.19, Vector2.Zero, false);
		Check(battle.Bullets.ActiveCount == 0, "自动攻击首发等待");
		battle.Step(0.01, Vector2.Zero, false);
		Check(battle.Bullets.ActiveCount == 1, "自动攻击首发");
		var shot = battle.Bullets.GetChild<Bullet>(0);
		Check(shot.Team == BulletTeam.Player && shot.Velocity.DistanceTo(Vector2.Up * 600) < 0.01, "自动瞄准");
		Check(!shot.IsPhysicsProcessing(), "托管子弹无重复更新");
		battle.Bullets.Clear();
		// 高速敌弹穿过玩家，实际连续碰撞必须扣血。
		var enemy = battle.Bullets.Spawn(battle.Player.GlobalPosition - new Vector2(100, 0), 0, BulletTeam.Enemy)!;
		enemy.Velocity = Vector2.Right * 12000;
		battle.Step(1.0 / 60, Vector2.Zero, false);
		Check(battle.Player.Health.Hp == 2, "实际高速敌弹命中");
		Check(battle.Boss.Hp == 100, "敌弹不伤Boss");
		var passing = battle.Bullets.Spawn(battle.Player.GlobalPosition, 0, BulletTeam.Enemy)!;
		passing.Velocity = Vector2.Zero;
		battle.Step(1.0 / 60, Vector2.Zero, false);
		Check(battle.Player.Health.Hp == 2 && battle.Bullets.ActiveCount == 1, "无敌期间敌弹穿过");
		battle.Restart();
		var ended = 0;
		battle.BattleEnded += state => ended++;
		battle.Boss.TakeDamage(100);
		battle.Step(1.0 / 60, Vector2.Zero, false);
		Check(battle.State == BattleState.Victory && ended == 1 && battle.Bullets.ActiveCount == 0, "胜利清理");
		var elapsed = battle.Elapsed;
		battle.Step(1, Vector2.One, true);
		Check(battle.Elapsed == elapsed && ended == 1, "结束后冻结");
		// 多轮重开必须重新生成实例并恢复所有初始状态。
		for (int round = 0; round < 3; round++)
		{
			battle.Restart();
			Check(battle.Player.Health.Hp == 3 && battle.Boss.Hp == 100 && battle.Elapsed == 0, "重开生命时钟");
			Check(battle.Player.Position == BattleConfig.PlayerSpawn && battle.Player.Dodge.Cooldown == 0, "重开位置冷却");
			Check(battle.Bullets.ActiveCount == 0 && world.GetChildCount() == 4, "重开无残留节点");
		}
		battle.Player.Health.TakeDamage(2, false);
		battle.Player.Health.Advance(1);
		battle.Boss.TakeDamage(99);
		battle.Bullets.Spawn(battle.Player.GlobalPosition, 0, BulletTeam.Enemy)!.Velocity = Vector2.Zero;
		battle.Bullets.Spawn(battle.Boss.GlobalPosition, 0, BulletTeam.Player)!.Velocity = Vector2.Zero;
		battle.Step(1.0 / 60, Vector2.Zero, false);
		Check(battle.Boss.Hp == 0 && battle.Player.Health.Hp == 0 && battle.State == BattleState.Defeat, "同帧死亡失败优先");
		battle.Restart();
		// 容量测试保持所有弹幕远离目标。
		for (int index = 0; index < BattleConfig.MaxBullets; index++) battle.Bullets.Spawn(new Vector2(-10000, 0), 0, BulletTeam.Enemy);
		Check(battle.Bullets.Spawn(Vector2.Zero, 0, BulletTeam.Enemy) is null, "容量上限");
		battle.Bullets.Clear();
		Check(battle.Bullets.ActiveCount == 0, "容量清理");
		world.Free();
	}
	/// <summary>验证阶段切换、退出与死亡不会重复。</summary>
	private void VerifyPhases()
	{
		// 自定义两个轻量阶段，不改变正式 Boss 默认内容。
		var world = new Node2D();
		AddChild(world);
		var boss = new BossController();
		var first = new ProbePhase();
		var second = new ProbePhase();
		boss.Initialize(world);
		boss.SetPhases(new[] { first, second });
		var changes = 0;
		var deaths = 0;
		boss.PhaseChanged += phase => changes++;
		boss.Died += () => deaths++;
		world.AddChild(boss);
		boss.Advance(0.1);
		Check(first.Enters == 1 && first.Updates == 1, "首阶段进入更新");
		first.End = true;
		boss.Advance(0.1);
		Check(first.Exits == 1 && second.Enters == 1 && changes == 2, "阶段切换一次");
		boss.TakeDamage(100);
		boss.TakeDamage(100);
		boss.Stop();
		Check(second.Exits == 1 && deaths == 1, "死亡退出一次");
		world.Free();
	}
    /// <summary>验证单次批次、双列表注销、外部控制和可选角度排列。</summary>
    private void VerifyBatches()
    {
        // 隔离场景，所有批次远离双方，避免非目标碰撞。
        var battle = CreateBattle(out var world);
        var manager = battle.Bullets;
        manager.Position = new Vector2(30, 40);
        var origin = new Vector2(-10000, -10000);
        var first = new Boss_01Emitter();
        var second = new Boss_01Emitter();
        first.Emit(manager, origin);
        second.Emit(manager, origin);
        Check(first.Bullets.Count == 24 && second.Bullets.Count == 24 && manager.ActiveCount == 48, "独立发射批次");
        Check(ReferenceEquals(first.Bullets[0], manager.ActiveBullets[0]), "双列表为同一对象");
        Check(first.Bullets[0].GlobalPosition == origin, "全局起点转为容器局部位置");
        try { first.Emit(manager, origin); Check(false, "重复发射未阻止"); }
        catch (InvalidOperationException) { _checks++; }
        // 外部代码选中一个批次调整，另一批次保持不变。
        foreach (var bullet in first.Bullets)
        {
            bullet.SetDirection(90);
            bullet.SetSpeed(50);
        }
        manager.Advance(0.1, battle.Player, battle.Boss);
        Check(first.Bullets[0].GlobalPosition.DistanceTo(origin + Vector2.Down * 5) < 0.01, "外部批次转向变速");
        Check(second.Bullets[0].GlobalPosition.DistanceTo(origin + Vector2.Right * 18) < 0.01, "其他批次不受影响");
        Check(Mathf.IsEqualApprox(first.Bullets[0].GetNode<Sprite2D>("Sprite").RotationDegrees, 90), "转向同步贴图");
        manager.Advance(4, battle.Player, battle.Boss);
        Check(first.Bullets.Count == 0 && second.Bullets.Count == 0 && manager.ActiveCount == 0, "过期同步注销");
        // 命中与清场通过同一注销入口，容量不足时不能新增节点。
        var hit = new SingleBulletEmitter(BulletSpawnData.ForTeam(BulletTeam.Enemy) with { Speed = 0 });
        hit.Emit(manager, battle.Player.GlobalPosition);
        manager.Advance(0, battle.Player, battle.Boss);
        Check(hit.Bullets.Count == 0 && manager.ActiveCount == 0, "命中同步注销");
        for (int index = 0; index < BattleConfig.MaxBullets - 1; index++) manager.Spawn(origin, 0, BulletTeam.Enemy);
        var partial = new Boss_01Emitter();
        partial.Emit(manager, origin);
        Check(partial.Bullets.Count == 1 && manager.ActiveCount == BattleConfig.MaxBullets
            && manager.GetChildCount() == BattleConfig.MaxBullets, "满额只登记成功对象");
        var blocked = new Boss_01Emitter();
        blocked.Emit(manager, origin);
        Check(blocked.Bullets.Count == 0 && manager.GetChildCount() == BattleConfig.MaxBullets, "满额不产生孤立节点");
        manager.Clear();
        Check(partial.Bullets.Count == 0 && manager.ActiveCount == 0, "清场同步注销");
        // 阶段退出后子弹仍存活，空批次在下一次推进时移除。
        battle.Boss.Advance(1);
        var phase = battle.Boss.CurrentPhase!;
        var surviving = phase.Emitters[0];
        battle.Boss.Stop();
        Check(phase.Emitters.Count == 0 && surviving.Bullets.Count == 24, "退出阶段保留已发弹幕");
        manager.Advance(0.1, battle.Player, battle.Boss);
        Check(surviving.Bullets[0].Age > 0, "退出后管理器继续推进");
        battle.Restart();
        Check(surviving.Bullets.Count == 0, "重开注销旧批次");
        battle.Boss.Advance(1);
        phase = battle.Boss.CurrentPhase!;
        battle.Bullets.Clear();
        battle.Boss.Advance(0);
        Check(phase.Emitters.Count == 0, "阶段移除空批次");
        battle.Boss.Advance(1);
        surviving = phase.Emitters[0];
        world.Free();
        Check(surviving.Bullets.Count == 0, "直接离场注销引用");
        // 可选排列辅助工具只生成方向，不创建任何节点。
        var angles = new System.Collections.Generic.List<float>();
        new AngularPattern().Emit(angles.Add, 0);
        Check(angles.Count == 24 && angles[0] == 0 && angles[23] == 345, "圆环无重复终点");
        angles.Clear();
        new AngularPattern(7, 15).Emit(angles.Add, -45);
        Check(angles.Count == 7 && angles[0] == -45 && angles[6] == 45, "90度扇形");
        angles.Clear();
        new AngularPattern(3, 15, false).Emit(angles.Add, 30);
        Check(angles[0] == 30 && angles[1] == 15 && angles[2] == 0, "逆时针排列");
        angles.Clear();
        new AngularPattern(1, 15).Emit(angles.Add, 123);
        Check(angles.Count == 1 && angles[0] == 123, "单发初始方向");
        angles.Clear();
        new AngularPattern(3, 0).Emit(angles.Add, 30);
        Check(angles.Count == 3 && angles.TrueForAll(angle => angle == 30), "零间隔同方向");
        try { _ = new AngularPattern(0); Check(false, "数量校验"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
        try { _ = new AngularPattern(1, float.NaN); Check(false, "间隔校验"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
    }
	/// <summary>确认正式场景同时支持物理键和辅助设备逻辑键。</summary>
	private void VerifyInput()
	{
		// 装配正式入口，检查实际注册的动作而非复制输入规则。
		var main = new Main();
		AddChild(main);
		main.GetNode<BattleManager>("BattleManager").SetPhysicsProcess(false);
		// 通过真实资源加载验证脚本迁移路径，并检查正式背景配置。
		Check(ResourceLoader.Exists("res://Boss/Boss.cs") && ResourceLoader.Exists("res://Bullet/Bullet.cs"), "模块脚本资源路径");
		var background = main.GetNode<TextureRect>("Background");
		Check(background.Texture is not null && background.Texture.GetSize() == new Vector2(400, 600), "背景素材加载");
		Check(background.Size == new Vector2(1280, 800) && background.Position == Vector2.Zero, "背景逻辑覆盖范围");
		Check(background.StretchMode == TextureRect.StretchModeEnum.KeepAspectCovered && background.ExpandMode == TextureRect.ExpandModeEnum.IgnoreSize, "背景等比裁切");
		Check(background.ZIndex < 0 && background.MouseFilter == Control.MouseFilterEnum.Ignore, "背景层级与输入穿透");
		Check(ProjectSettings.GetSetting("display/window/size/viewport_width").AsInt32() == 1280
			&& ProjectSettings.GetSetting("display/window/size/viewport_height").AsInt32() == 800, "逻辑分辨率");
		Check(ProjectSettings.GetSetting("display/window/stretch/mode").AsString() == "canvas_items"
			&& ProjectSettings.GetSetting("display/window/stretch/aspect").AsString() == "keep", "等比窗口缩放配置");
		var bindings = new (string Action, Key Key)[]
		{
			("move_left", Key.A), ("move_left", Key.Left),
			("move_right", Key.D), ("move_right", Key.Right),
			("move_up", Key.W), ("move_up", Key.Up),
			("move_down", Key.S), ("move_down", Key.Down),
			("player_dodge", Key.Space), ("battle_restart", Key.R)
		};
		// 每个默认绑定均验证物理与逻辑输入匹配。
		foreach (var binding in bindings)
		{
			using var physical = new InputEventKey { PhysicalKeycode = binding.Key, Pressed = true };
			using var logical = new InputEventKey { Keycode = binding.Key, Pressed = true };
			Check(InputMap.EventIsAction(physical, binding.Action), "物理按键映射");
			Check(InputMap.EventIsAction(logical, binding.Action), "逻辑按键映射");
		}
		main.Free();
	}
	/// <summary>记录阶段生命周期调用次数的测试实现。</summary>
	private sealed class ProbePhase : BossPhase
	{
		// 各生命周期回调次数和可控结束条件。
		public int Enters, Updates, Exits;
		public bool End;
		/// <summary>测试阶段名称。</summary>
		public override string Name => "测试阶段";
		/// <summary>记录进入。</summary>
		/// <param name="boss">所属 Boss。</param>
		public override void Enter(BossController boss) => Enters++;
		/// <summary>记录更新。</summary>
		/// <param name="boss">所属 Boss。</param>
		/// <param name="delta">测试步秒数。</param>
		public override void Advance(BossController boss, double delta) => Updates++;
		/// <summary>返回测试指定的结束状态。</summary>
		/// <param name="boss">所属 Boss。</param>
		/// <returns>是否切换阶段。</returns>
		public override bool ShouldEnd(BossController boss) => End;
		/// <summary>记录退出。</summary>
		/// <param name="boss">所属 Boss。</param>
		public override void Exit(BossController boss) => Exits++;
	}
}
