using Godot;
using System;
using System.Linq;

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
    /// <summary>执行回归集或弹幕重构相关验证，失败时以非零状态退出。</summary>
    private void Run()
    {
        try
        {
            var userArgs = OS.GetCmdlineUserArgs();
            if (userArgs.Contains("--targeted-vnode"))
            {
                VerifyVNodeModel();
                VerifyVNodeSchedules();
                VerifyVNodeLifecycle();
                VerifyVNodeValidation();
                VerifyVNodeReplay();
                GD.Print($"PASS: {_checks} targeted VNode assertions");
            }
            else if (userArgs.Contains("--targeted-data"))
            {
                VerifyBulletData();
                VerifyOriginal();
                VerifyBatches();
                VerifyDefaultSets();
                GD.Print($"PASS: {_checks} targeted data and bullet assertions");
            }
            else if (userArgs.Contains("--targeted-sprites"))
            {
                VerifySpriteSets();
                GD.Print($"PASS: {_checks} targeted sprite assertions");
            }
            else if (userArgs.Contains("--targeted-bullet"))
            {
                VerifyOriginal();
                VerifyBatches();
                VerifyDefaultSets();
                GD.Print($"PASS: {_checks} targeted bullet assertions");
            }
            else if (userArgs.Contains("--targeted-phase-switch"))
            {
                VerifyPhaseSwitch();
                GD.Print($"PASS: {_checks} targeted phase switch assertions");
            }
            else if (userArgs.Contains("--targeted-global-services"))
            {
                VerifyGlobalServices();
                GD.Print($"PASS: {_checks} targeted global service assertions");
            }
            else if (userArgs.Contains("--targeted-b01"))
            {
                VerifyBoss01Sequence();
                VerifyBoss01Stages();
                VerifyPhaseSwitch();
                GD.Print($"PASS: {_checks} targeted B01 assertions");
            }
            else
            {
                VerifyOriginal();
                VerifyPlayer();
                VerifyCombat();
                VerifyPhases();
                VerifyBoss01Stages();
                VerifyNegativePlayerHealth();
                VerifyBatches();
                VerifyDefaultSets();
                VerifyInput();
                GD.Print($"PASS: {_checks} battle regression assertions");
            }
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
		var battle = CreateBattle(out var world);
		battle.Player.Attack.Stop();
		var container = battle.Bullets;
		var boss = battle.Boss;
		Check(boss.Position == new Vector2(640, 250), "Boss位置");
		Check(boss.GetNode<Sprite2D>("Sprite").Scale == Vector2.One * 3, "Boss倍率3");
		VerificationClock.BossSeconds(battle, 0.99);
		Check(container.GetChildCount() == 0, "第一秒前不能发射");
		VerificationClock.BossSeconds(battle, 0.01);
		Check(container.GetChildCount() == 40, $"第一秒双发射器各自按既有参数发射，实际{container.GetChildCount()}发");
		double aimedAngle = VMath.getB2PAngle();
		// 前24颗为默认环形弹，后16颗为既有定制环形弹。
		for (int index = 0; index < 40; index++)
		{
			var bullet = container.GetChild<Bullet>(index);
			var sprite = bullet.GetNode<Sprite2D>("Sprite");
			var isDefault = index < 24;
			var localIndex = isDefault ? index : index - 24;
			var expectedCount = isDefault ? 24 : 16;
			var expectedSpeed = isDefault ? 180 : 300;
			var expectedColor = isDefault ? 0 : 3;
			double expectedAngle = VMath.StandardizationAngle((isDefault ? 0 : aimedAngle)
				+ localIndex * Math.Tau / expectedCount);
			Check(Math.Abs(bullet.AngleRadians - expectedAngle) < 1e-12, "环形角度");
			Check(bullet.Speed == expectedSpeed && bullet.LifetimeSeconds == 4, "原弹速寿命");
			Check(sprite.Scale == Vector2.One * 3 && sprite.Frame == expectedColor && sprite.Centered, "原弹幕外观");
			Check(sprite.TextureFilter == CanvasItem.TextureFilterEnum.Nearest, "最近邻");
		}
		VerificationClock.BossSeconds(battle, 2.5);
		Check(container.GetChildCount() == 120, "大步长补发");
		VerificationClock.BossSeconds(battle, 0.5);
		Check(container.GetChildCount() == 160, "发射余量");
		// 所有实际子弹由管理器推进，外部初始化入口不再自行更新。
        var sample = container.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = new Vector2(-1000, -1000),
            AngleRadians = Mathf.Pi / 2,
            Speed = 100,
            LifetimeSeconds = 0.25f,
            VisualScale = 2
        })!;
        Check(!sample.IsPhysicsProcessing(), "子弹不自行推进");
        Check(sample.GetNode<Sprite2D>("Sprite").Scale == Vector2.One * 2, "自定义倍率");
        container.Clear();
        Check(container.ActiveCount == 0, "清场注销直接生成的子弹");
        // 枚举图集颜色，迁移后仍逐颗保留外观配置。
        for (int color = 0; color < 10; color++)
        {
            var colored = container.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
            {
                Position = Vector2.Zero,
                ColorIndex = color,
                VisualScale = 1
            })!;
            Check(colored.GetNode<Sprite2D>("Sprite").Frame == color, "图集颜色");
        }
        // 无效参数应在创建节点之前失败，不污染任一列表。
        foreach (var invalidData in new[]
        {
            BulletDefaultSet.Get(BulletType.ScaleSet) with { ColorIndex = 10 },
            BulletDefaultSet.Get(BulletType.ScaleSet) with { VisualScale = double.NaN },
            BulletDefaultSet.Get(BulletType.ScaleSet) with { VisualScale = double.Epsilon },
            BulletDefaultSet.Get(BulletType.ScaleSet) with { Radius = double.Epsilon },
            BulletDefaultSet.Get(BulletType.ScaleSet) with { Speed = double.MaxValue },
            BulletDefaultSet.Get(BulletType.ScaleSet) with { Speed = -double.MaxValue }
        })
        {
            var before = container.GetChildCount();
            try { container.Spawn(invalidData); Check(false, "无效参数未校验"); }
            catch (ArgumentOutOfRangeException) { _checks++; }
            Check(container.GetChildCount() == before, "无效参数不留节点");
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
		// 组件使用完整战斗提供的全局计时器，测试仍直接推进本场处理器。
        var componentBattle = CreateBattle(out var componentWorld);
        componentBattle.Player.Attack.Stop();
        var componentOwner = componentBattle.Player;
        var componentTimers = componentBattle.Timers;
        var dodge = new PlayerDodge();
        dodge.Initialize(componentOwner);
		Check(dodge.TryStart(Vector2.Zero) && dodge.Direction == Vector2.Up, "默认向上闪避");
		Check(!dodge.TryStart(Vector2.Right), "闪避冷却阻止重复");
		componentTimers.AdvanceByUnits(9000, _ => componentOwner.Timeline!.AdvanceUnits(9000));
		Check(!dodge.IsActive && Math.Abs(dodge.Cooldown - 0.85) < 1e-6, "闪避与冷却独立");
		componentTimers.AdvanceByUnits(51000, _ => componentOwner.Timeline!.AdvanceUnits(51000));
		Check(dodge.TryStart(Vector2.Right), "冷却恢复");
				// 60Hz下不允许浮点余量额外延长闪避、冷却和无敌。
        var fixedDodge = new PlayerDodge();
        fixedDodge.Initialize(componentOwner);
		fixedDodge.TryStart(Vector2.Up);
		for (int tick = 0; tick < 9; tick++) componentTimers.AdvanceByUnits(1000, _ => componentOwner.Timeline!.AdvanceUnits(1000));
		Check(!fixedDodge.IsActive, "9个固定步结束闪避");
		for (int tick = 9; tick < 60; tick++) componentTimers.AdvanceByUnits(1000, _ => componentOwner.Timeline!.AdvanceUnits(1000));
		Check(fixedDodge.Cooldown == 0, "60个固定步恢复冷却");
		var fixedHealth = new PlayerHealth();
        fixedHealth.Initialize(componentOwner);
		fixedHealth.TakeDamage(1, false);
		for (int tick = 0; tick < 60; tick++) componentTimers.AdvanceByUnits(1000, _ => componentOwner.Timeline!.AdvanceUnits(1000));
		Check(fixedHealth.TakeDamage(1, false), "60个固定步结束受击无敌");
		// 独立生命组件与事件计数。
        var health = new PlayerHealth();
        health.Initialize(componentOwner);
		var changes = 0;
		health.HealthChanged += hp => changes++;
		Check(!health.TakeDamage(1, true), "闪避无敌");
		Check(health.TakeDamage(1, false) && !health.TakeDamage(1, false), "受击防重复");
		componentTimers.AdvanceByUnits(60000, _ => componentOwner.Timeline!.AdvanceUnits(60000));
		Check(health.TakeDamage(99, false) && health.Hp == -97, "生命允许降为负数");
		Check(!health.TakeDamage(1, false) && changes == 2, "负血保留受击保护与通知");
		componentWorld.Free();
		var battle = CreateBattle(out var world);
		VerificationClock.BattleSeconds(battle, 0.1, Vector2.One, false);
		Check(Math.Abs(battle.Player.Position.DistanceTo(BattleConfig.PlayerSpawn) - 24) < 0.001, "实际移动速度");
		VerificationClock.BattleSeconds(battle, 0.15, Vector2.Zero, true);
		Check(battle.Player.Position.DistanceTo(BattleConfig.PlayerSpawn + Vector2.One.Normalized() * 132) < 0.01, "无输入沿上次方向闪避");
		// 单独验证普通移动及闪避无法越过圆形边界，位置单位为像素。
		battle.Restart();
		battle.Player.Position = center + Vector2.Right * 390;
		VerificationClock.BattleSeconds(battle, 0.1, Vector2.Right, false);
		Check(battle.Player.Position.DistanceTo(center + Vector2.Right * 395) < 0.001, "普通移动圆边界");
		battle.Restart();
		battle.Player.Position = center + Vector2.One.Normalized() * 390;
		VerificationClock.BattleSeconds(battle, 0.15, Vector2.One, true);
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
		VerificationClock.BattleSeconds(battle, 11.0 / 60, Vector2.Zero, false);
		Check(battle.Bullets.ActiveCount == 0, "自动攻击首发等待");
		VerificationClock.BattleSeconds(battle, 1.0 / 60, Vector2.Zero, false);
		Check(battle.Bullets.ActiveCount == 1, "自动攻击首发");
		var shot = battle.Bullets.GetChild<Bullet>(0);
		Check(shot.Team == BulletTeam.Player && shot.Velocity.DistanceTo(Vector2.Up * 600) < 0.01, "自动瞄准");
		Check(!shot.IsPhysicsProcessing(), "托管子弹无重复更新");
		battle.Bullets.Clear();
		// 高速敌弹穿过玩家，实际连续碰撞必须扣血。
		var enemy = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with { Position = battle.Player.GlobalPosition - new Vector2(100, 0) })!;
		enemy.SetDirection(0);
		enemy.SetSpeed(12000);
		battle.StepFixed( Vector2.Zero, false);
		Check(battle.Player.Health.Hp == 2, "实际高速敌弹命中");
		Check(battle.Boss.Hp == 300, "敌弹不伤Boss");
		var passing = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with { Position = battle.Player.GlobalPosition })!;
		passing.SetSpeed(0);
		battle.StepFixed( Vector2.Zero, false);
		Check(battle.Player.Health.Hp == 2 && battle.Bullets.ActiveCount == 1, "无敌期间敌弹穿过");
		battle.Restart();
		var ended = 0;
		battle.BattleEnded += state => ended++;
		battle.Boss.TakeDamage(300);
		battle.StepFixed( Vector2.Zero, false);
		Check(battle.State == BattleState.Victory && ended == 1 && battle.Bullets.ActiveCount == 0, "胜利清理");
		var elapsed = battle.Elapsed;
		VerificationClock.BattleSeconds(battle, 1, Vector2.One, true);
		Check(battle.Elapsed == elapsed && ended == 1, "结束后冻结");
		// 多轮重开必须重新生成实例并恢复所有初始状态。
		for (int round = 0; round < 3; round++)
		{
			battle.Restart();
			Check(battle.Player.Health.Hp == 3 && battle.Boss.Hp == 300 && battle.Elapsed == 0, "重开生命时钟");
			Check(battle.Player.Position == BattleConfig.PlayerSpawn && battle.Player.Dodge.Cooldown == 0, "重开位置冷却");
			Check(battle.Bullets.ActiveCount == 0 && world.GetChildCount() == 4, "重开无残留节点");
		}
		battle.Player.Health.TakeDamage(2, false);
		battle.Timers.AdvanceByUnits(60000, _ => battle.Player.Timeline!.AdvanceUnits(60000));
        battle.Bullets.Clear();
		battle.Boss.TakeDamage(299);
		battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with { Position = battle.Player.GlobalPosition })!.SetSpeed(0);
		battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.PlayerSet) with { Position = battle.Boss.GlobalPosition })!.SetSpeed(0);
		battle.StepFixed( Vector2.Zero, false);
		Check(battle.Boss.Hp == 0 && battle.Player.Health.Hp == 0 && battle.State == BattleState.Victory, "玩家同段零血不阻止Boss击败胜利");
		battle.Restart();
		// 容量测试保持所有弹幕远离目标。
		for (int index = 0; index < BattleConfig.MaxBullets; index++) battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
		{
			Position = new Vector2(-10000, 0)
		});
		Check(battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with { }) is null, "容量上限");
		battle.Bullets.Clear();
		Check(battle.Bullets.ActiveCount == 0, "容量清理");
		world.Free();
	}
	/// <summary>验证阶段切换、退出与死亡不会重复。</summary>
	private void VerifyPhases()
	{
		var battle = CreateBattle(out var world);
		var boss = battle.Boss;
		var first = boss.CurrentPhase!;
		Check(ReferenceEquals(first.Emitters, first.Emitters), "阶段发射器复用只读视图");
		var bossTimeline = boss.Timeline;
		int crossPhaseActions = 0;
		bossTimeline!.At(30, () => crossPhaseActions++);
		var changes = 0;
		var deaths = 0;
		boss.PhaseChanged += phase => changes++;
		boss.Died += () => deaths++;
		battle.StepFixed(Vector2.Zero, false);
		Check(first.Timeline is not null && bossTimeline?.ElapsedUnits == VTimerProcessor.FixedStepUnits, "首阶段与Boss独立计时");
		Check(boss.TrySwitchAdjacentPhase(1) && first.Timeline is null
			&& boss.CurrentPhase is B01_Phase02 && ReferenceEquals(boss.Timeline, bossTimeline)
			&& changes == 1, "阶段切换保留Boss时间线");
		battle.StepFixed(Vector2.Zero, false);
		Check(crossPhaseActions == 1 && bossTimeline!.ElapsedUnits == 2 * VTimerProcessor.FixedStepUnits,
			"Boss时间线事件跨阶段在步末执行");
		boss.TakeDamage(300);
		boss.TakeDamage(300);
		boss.Stop();
		Check(boss.CurrentPhase is null && boss.Timeline is null && deaths == 1, "死亡退出一次");
		world.Free();
	}
	/// <summary>验证全局战斗服务的绑定、重开、胜利保留及旧实例隔离。</summary>
	private void VerifyGlobalServices()
	{
		var battle = CreateBattle(out var world);
		Check(ReferenceEquals(GlobalEvent.GetBoss(), battle.Boss)
			&& ReferenceEquals(GlobalEvent.GetPlayer(), battle.Player)
			&& ReferenceEquals(GlobalEvent.GetBulletManager(), battle.Bullets), "阶段初始化可查询全局实体");
		var oldTimers = battle.Timers;
		var oldBullets = battle.Bullets;
		battle.Restart();
		Check(!ReferenceEquals(oldTimers, battle.Timers) && !ReferenceEquals(oldBullets, battle.Bullets)
			&& oldTimers.TimelineActionCount == 0 && ReferenceEquals(GlobalEvent.GetBoss(), battle.Boss), "重开替换战斗服务");
		var victoryBoss = battle.Boss;
		var victoryPlayer = battle.Player;
		var victoryBullets = battle.Bullets;
		victoryBoss.TakeDamage(300);
		battle.StepFixed(Vector2.Zero, false);
		Check(battle.State == BattleState.Victory && ReferenceEquals(GlobalEvent.GetBoss(), victoryBoss)
			&& ReferenceEquals(GlobalEvent.GetPlayer(), victoryPlayer)
			&& ReferenceEquals(GlobalEvent.GetBulletManager(), victoryBullets), "胜利保留全局实体");
		try { GlobalEvent.CreateTimeline(victoryPlayer); Check(false, "胜利后仍可创建时间线"); }
		catch (InvalidOperationException) { _checks++; }
		battle.StopBattle();
		try { GlobalEvent.GetBoss(); Check(false, "停止后仍可读取全局Boss"); }
		catch (InvalidOperationException) { _checks++; }
		world.Free();

		var first = CreateBattle(out var firstWorld);
		var second = CreateBattle(out var secondWorld);
		first.StopBattle();
		Check(ReferenceEquals(GlobalEvent.GetBoss(), second.Boss)
			&& ReferenceEquals(GlobalEvent.GetBulletManager(), second.Bullets), "旧战斗停止不污染新绑定");
		firstWorld.Free();
		Check(ReferenceEquals(GlobalEvent.GetBoss(), second.Boss), "旧战斗延迟离场不解除新绑定");
		var secondTimers = second.Timers;
		secondWorld.Free();
		Check(secondTimers.TimelineActionCount == 0, "直接离场清理全部时间线动作");
		try { GlobalEvent.GetPlayer(); Check(false, "直接离场后仍可读取全局玩家"); }
		catch (InvalidOperationException) { _checks++; }
	}
    /// <summary>验证Q/E相邻阶段切换、边界、计时器交接与固定输入结果。</summary>
    private void VerifyPhaseSwitch()
    {
        GameInput.EnsureBindings();
        // 阶段切换动作分别验证物理键和逻辑键映射。
        foreach (var binding in new[] { ("battle_previous_phase", Key.Q), ("battle_next_phase", Key.E) })
        {
            using var physical = new InputEventKey { PhysicalKeycode = binding.Item2, Pressed = true };
            using var logical = new InputEventKey { Keycode = binding.Item2, Pressed = true };
            Check(InputMap.EventIsAction(physical, binding.Item1), "阶段切换物理按键映射");
            Check(InputMap.EventIsAction(logical, binding.Item1), "阶段切换逻辑按键映射");
        }

        // 建立隔离战斗，停止玩家攻击以稳定阶段计时器数量。
        var battle = CreateBattle(out var world);
        battle.Player.Attack.Stop();
        var boss = battle.Boss;
        var playerPosition = battle.Player.Position;
        var bossPosition = boss.Position = new Vector2(321, 234);
        var initialPhase = boss.CurrentPhase!;
        var initialActions = battle.Timers.TimelineActionCount;
		Check(initialPhase.Name == "环形弹幕 · 阶段01" && boss.Hp == 300 && initialActions == 4, "阶段切换初始状态");
        // 旧阶段弹幕用于验证切换时仍由管理器保留。
        var oldBullet = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = new Vector2(-1000, -1000),
            Speed = 0
        })!;

        battle.StepFixed(Vector2.Zero, false, false, true);
        Check(boss.CurrentPhase is B01_Phase02 && boss.Hp == 200
            && boss.Position.DistanceTo(new Vector2(640, 240)) < bossPosition.DistanceTo(new Vector2(640, 240))
            && battle.Player.Position == playerPosition, "E切入阶段02并在当前物理步开始移动");
        Check(GodotObject.IsInstanceValid(oldBullet) && battle.Bullets.ActiveCount == 1
            && battle.Timers.TimelineActionCount == 2, "切换保留旧弹并重建阶段动作");
        // 保存阶段对象，确认切入后的首个周期不会提前发射。
        var phaseTwo = boss.CurrentPhase!;
        var phaseTwoStart = boss.Position;
        VerificationClock.BossSeconds(battle, 0.5);
        Check(Mathf.IsEqualApprox(boss.Position.DistanceTo(phaseTwoStart), 100), "阶段02移动速度200");
        VerificationClock.BossSeconds(battle, 0.4);
        Check(phaseTwo.Emitters.Count == 2 && phaseTwo.Emitters[0].Bullets.Count == 0, "切入阶段后首周期仍等待");
        VerificationClock.BossSeconds(battle, 0.1);
        Check(phaseTwo.Emitters.Count == 2 && phaseTwo.Emitters[0].Bullets.Count == 72,
            "阶段02首周期只执行一次成功发射的批次");
        VerificationClock.BossSeconds(battle, 0.6);
        Check(boss.Position == new Vector2(640, 240) && !((B01_Phase02)phaseTwo).IsMoving,
            "阶段02到达固定目标后停止");

        // 同时按键的阶段、生命和位置快照。
        var beforeBoth = boss.CurrentPhase;
        var beforeBothHp = boss.Hp;
        var beforeBothPosition = boss.Position;
        battle.StepFixed(Vector2.Zero, false, true, true);
        Check(ReferenceEquals(boss.CurrentPhase, beforeBoth) && boss.Hp == beforeBothHp
            && boss.Position == beforeBothPosition, "同时按Q/E不切换或重置");
        battle.StepFixed(Vector2.Zero, false, false, true);
        Check(boss.CurrentPhase is B01_Phase03 && boss.Hp == 100 && boss.Position == beforeBothPosition, "E切入阶段03");
        // 末阶段计时器数量用于确认边界按键不重复注册。
        var phaseThreeActions = battle.Timers.TimelineActionCount;
        boss.TakeDamage(1);
        battle.StepFixed(Vector2.Zero, false, false, true);
        Check(boss.CurrentPhase is B01_Phase03 && boss.Hp == 99 && battle.Timers.TimelineActionCount == phaseThreeActions, "末阶段E不循环且不回血");
        battle.StepFixed(Vector2.Zero, false, true, false);
        Check(boss.CurrentPhase is B01_Phase02 && boss.Hp == 200, "Q返回阶段02");
        battle.StepFixed(Vector2.Zero, false, true, false);
        Check(boss.CurrentPhase is B01_Phase01 && boss.Hp == 300, "Q返回阶段01");
        boss.TakeDamage(1);
        battle.StepFixed(Vector2.Zero, false, true, false);
        Check(boss.CurrentPhase is B01_Phase01 && boss.Hp == 299, "首阶段Q不循环且不回血");

        battle.Restart();
        battle.Player.Attack.Stop();
        boss = battle.Boss;
        battle.Boss.TakeDamage(101);
        Check(boss.CurrentPhase is B01_Phase02 && boss.Hp == 199, "自然受伤进入阶段02不回血");
        battle.Boss.TakeDamage(99);
        Check(boss.CurrentPhase is B01_Phase03 && boss.Hp == 100, "自然受伤进入阶段03不回血");
        battle.Boss.TakeDamage(100);
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.State == BattleState.Victory && boss.CurrentPhase is null, "外部致死后固定步进入胜利");
        battle.StepFixed(Vector2.Zero, false, true, false);
        Check(battle.State == BattleState.Victory && boss.CurrentPhase is null, "胜利后Q不生效");
        world.Free();

        // 停止战斗后记录生命，确认输入不会恢复阶段。
        var stoppedBattle = CreateBattle(out var stoppedWorld);
        var stoppedHp = stoppedBattle.Boss.Hp;
        stoppedBattle.StopBattle();
        stoppedBattle.StepFixed(Vector2.Zero, false, false, true);
        Check(stoppedBattle.Boss.CurrentPhase is null && stoppedBattle.Boss.Hp == stoppedHp, "Stop后E不生效");
        stoppedWorld.Free();

        // 两个相同战斗实例执行相同切换序列，比较固定输入结果。
        var firstRun = CreateBattle(out var firstWorld);
        var secondRun = CreateBattle(out var secondWorld);
        firstRun.Player.Attack.Stop();
        secondRun.Player.Attack.Stop();
        var inputs = new[] { (Previous: false, Next: true), (Previous: false, Next: false), (Previous: true, Next: false) };
        foreach (var input in inputs)
        {
            firstRun.StepFixed(Vector2.Zero, false, input.Previous, input.Next);
            secondRun.StepFixed(Vector2.Zero, false, input.Previous, input.Next);
        }
        Check(firstRun.Boss.CurrentPhase!.Name == secondRun.Boss.CurrentPhase!.Name
            && firstRun.Boss.Hp == secondRun.Boss.Hp && firstRun.Boss.Position == secondRun.Boss.Position
            && firstRun.Timers.NowUnits == secondRun.Timers.NowUnits, "固定输入阶段切换可重现");
        firstWorld.Free();
        secondWorld.Free();

        // 直接调用切换接口，确认当前动画帧不被阶段重入重置。
        var animationBattle = CreateBattle(out var animationWorld);
        var animationSprite = animationBattle.Boss.GetNode<Sprite2D>("Sprite");
        animationSprite.Frame = 1;
        Check(animationBattle.Boss.TrySwitchAdjacentPhase(1) && animationSprite.Frame == 1, "直接切换不重置动画帧");
        animationWorld.Free();
    }
    /// <summary>验证阶段03逐圈时序、延迟追踪、退出和容量不足。</summary>
    private void VerifyBoss01Sequence()
    {
        var battle = CreateBattle(out var world);
        battle.Player.Attack.Stop();
        var boss = battle.Boss;
        boss.TakeDamage(200);
        Check(boss.CurrentPhase is B01_Phase03 && !boss.CurrentPhase.IsMoving,
            "阶段03切入时保持当前位置");
        Vector2 origin = boss.GlobalPosition;
        VerificationClock.BossSeconds(battle, 0.999);
        Check(battle.Bullets.ActiveCount == 0, "首次发射前没有阶段03子弹");
        VerificationClock.BossSeconds(battle, 0.001);
        var emitter = (B01P03_Emitter01)boss.CurrentPhase!.Emitters[0];
        Check(emitter.Bullets.Count == 12 && battle.Bullets.ActiveCount == 12,
            "第一圈在发射时刻生成12颗");
        // 后续圆心以首次发射时的位置为准，Boss位移不改变本轮斜线。
        boss.Position = new Vector2(800, 240);
        for (int ring = 2; ring <= 6; ring++)
        {
            VerificationClock.BossSeconds(battle, 0.199);
            Check(emitter.Bullets.Count == (ring - 1) * 12, "不足200毫秒不提前生成下一圈");
            VerificationClock.BossSeconds(battle, 0.001);
            Check(emitter.Bullets.Count == ring * 12 && battle.Bullets.ActiveCount == ring * 12,
                "每200毫秒恰好生成一圈");
        }
        Vector2 source = VMath.PolarMove(origin, Mathf.Pi * 5 / 6, 400);
        Vector2 end = VMath.PolarMove(origin, Mathf.Pi * 11 / 6, 400);
        end.Y += 300;
        double distance = VMath.GetDistanceBetween2Points(source, end);
        double lineAngle = VMath.GetAngleBetween2Points(source, end);
        for (int ring = 0; ring < 6; ring++)
        {
            Vector2 center = VMath.PolarMove(source, lineAngle, distance * ring / 5);
            for (int index = 0; index < 12; index++)
            {
                Bullet bullet = emitter.Bullets[ring * 12 + index];
                double angle = index * Math.Tau / 12;
                Check(bullet.GlobalPosition.DistanceTo(VMath.PolarMove(center, angle, 70)) < 0.001
                    && Math.Abs(bullet.AngleRadians - angle) < 0.000001 && bullet.Speed == 0,
                    "六个等距圆心及每圈12颗等角静止弹");
            }
        }
        VerificationClock.BossSeconds(battle, 0.999);
        Check(emitter.Bullets[0].Speed == 0, "第一圈未满两秒保持静止");
        battle.Player.Position = new Vector2(700, 650);
        VerificationClock.BossSeconds(battle, 0.001);
        Bullet firstBullet = emitter.Bullets[0];
        Check(firstBullet.Speed == 150 && emitter.Bullets[12].Speed == 0
            && Math.Abs(firstBullet.AngleRadians - VMath.GetAngleBetween2Points(
                firstBullet.GlobalPosition, battle.Player.GlobalPosition)) < 0.000001,
            "第一圈两秒后逐颗瞄准当时的玩家，下一圈仍静止");
        VerificationClock.BossSeconds(battle, 0.2);
        Check(emitter.Bullets[12].Speed == 150 && emitter.Bullets[24].Speed == 0,
            "第二圈按自身出生时间延迟转向");
        VerificationClock.BossSeconds(battle, 1.8);
        Check(boss.CurrentPhase is B01_Phase03 && boss.CurrentPhase.IsMoving
            && Math.Abs(boss.CurrentPhase.MoveTarget.DistanceTo(new Vector2(640, 250)) - 200) < 0.001,
            "阶段03仍在第五秒选择圆周移动目标");
        world.Free();

        var stoppedBattle = CreateBattle(out var stoppedWorld);
        stoppedBattle.Player.Attack.Stop();
        stoppedBattle.Boss.TakeDamage(200);
        VerificationClock.BossSeconds(stoppedBattle, 1.4);
        var stoppedEmitter = (B01P03_Emitter01)stoppedBattle.Boss.CurrentPhase!.Emitters[0];
        Check(stoppedEmitter.Bullets.Count == 36, "退出前已生成三圈");
        stoppedBattle.Boss.Stop();
        VerificationClock.BossSeconds(stoppedBattle, 2.5);
        Check(stoppedEmitter.Bullets.Count == 36 && stoppedEmitter.Bullets.All(bullet => bullet.Speed == 150),
            "退出取消未生成的圈，已出生子弹仍按各自计时转向");
        while (stoppedBattle.Bullets.ActiveCount < BattleConfig.MaxBullets - 4)
            stoppedBattle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
            {
                Position = new Vector2(-1000, -1000),
                Speed = 0
            });
        var limited = new B01P03_Emitter01();
        limited.Start(stoppedBattle.Boss, stoppedBattle.Bullets);
        VerificationClock.EmitterSeconds(stoppedBattle, 1, limited);
        int actionsBeforeEmptyRing = stoppedBattle.Timers.TimelineActionCount;
        VerificationClock.EmitterSeconds(stoppedBattle, 0.2, limited);
        Check(limited.Bullets.Count == 4 && stoppedBattle.Bullets.ActiveCount == BattleConfig.MaxBullets
            && stoppedBattle.Timers.TimelineActionCount == actionsBeforeEmptyRing - 1,
            "容量不足时只登记成功生成的弹，空圈不注册转向动作");
        limited.Stop();
        stoppedWorld.Free();
    }

    /// <summary>验证Boss_01三阶段血量阈值、已发批次及阶段计时器交接。</summary>
    private void VerifyBoss01Stages()
    {
        // 正式资源与无配置入口都必须使用300血和相同阶段组合。
        var data = GD.Load<BossData>("res://Data/Bosses/Boss_01.tres");
        Check(data.MaxHp == 300, "正式Boss01资源300血");
        var battle = CreateBattle(out var world);
        battle.Player.Attack.Stop();
        var boss = battle.Boss;
        Check(boss.MaxHp == 300 && boss.Hp == 300 && boss.CurrentPhase is B01_Phase01, "默认入口首阶段300血");
        VerificationClock.BossSeconds(battle, 1);
        var first = boss.CurrentPhase!;
        var surviving = first.Emitters[0];
        // 保存成功产生子弹的批次，阶段退出清理索引后仍能验证存活子弹。
        var batches = first.Emitters.ToList();
        Check(first.Emitters.Count == 2 && first.Emitters[0] is B01P01_Emitter01
            && first.Emitters[1] is B01P01_Emitter02, "阶段01独立双发射器");
        boss.TakeDamage(99);
        Check(boss.Hp == 201 && ReferenceEquals(first, boss.CurrentPhase), "201血保持阶段01");
        boss.TakeDamage(1);
        Check(boss.Hp == 200 && boss.CurrentPhase is B01_Phase02, "200血立即进入阶段02");
        Check(first.Emitters.Count == 0 && surviving.Bullets.Count == 24
            && surviving.Timeline is null, "旧阶段停止发射器但保留既有子弹");
        VerificationClock.BossSeconds(battle, 0.99);
        Check(boss.CurrentPhase!.Emitters.Count == 2 && battle.Bullets.ActiveCount == 40, "新阶段首次等待完整周期");
        VerificationClock.BossSeconds(battle, 0.01);
        var second = boss.CurrentPhase!;
        batches.AddRange(second.Emitters);
        Check(second.Emitters.Count == 2 && second.Emitters[0] is B01P02_Emitter01
            && second.Emitters[0].Bullets.Count == 72 && battle.Bullets.ActiveCount == 112,
            "阶段02首轮生成六圈各12颗子弹");
        boss.TakeDamage(99);
        Check(boss.Hp == 101 && ReferenceEquals(second, boss.CurrentPhase), "101血保持阶段02");
        boss.TakeDamage(1);
        Check(boss.CurrentPhase is B01_Phase03 && battle.Timers.TimelineActionCount > 0,
            "100血进入阶段03，已发子弹的行为计时器继续存活");
        VerificationClock.BossSeconds(battle, 1);
        var third = boss.CurrentPhase!;
        batches.AddRange(third.Emitters);
        Check(third.Emitters.Count == 2 && third.Emitters[0] is B01P03_Emitter01
            && third.Emitters[0].Bullets.Count == 12, "阶段03第一圈立即生成");
        Check(batches.Count == 6, "记录三个阶段各自绑定的两个发射器");
        boss.TakeDamage(99);
        Check(boss.Hp == 1 && ReferenceEquals(third, boss.CurrentPhase), "1血保持最后阶段");
        boss.TakeDamage(1);
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.State == BattleState.Victory && boss.CurrentPhase is null
            && battle.Timers.TimelineActionCount == 0, "0血胜利清理全部阶段");
        battle.Restart();
        Check(battle.Boss.Hp == 300 && battle.Boss.CurrentPhase is B01_Phase01
            && battle.Timers.TimelineActionCount == 4, "重开恢复三阶段初始配置");
        // 单次跨两条血线时有序切换，不能停留一物理步后再补切。
        var entered = new System.Collections.Generic.List<Type>();
        battle.Boss.PhaseChanged += phase => entered.Add(phase.GetType());
        battle.Boss.TakeDamage(250);
        Check(battle.Boss.Hp == 50 && battle.Boss.CurrentPhase is B01_Phase03
            && entered.SequenceEqual(new[] { typeof(B01_Phase02), typeof(B01_Phase03) }), "大伤害同刻跨过两个阶段");
        Check(battle.Timers.TimelineActionCount == 3, "跳阶段不残留中间阶段动作且空发射器无动作");
        // 正式工厂入口也注册三个独立阶段。
        var configured = BossFactory.Create(data);
        world.AddChild(configured);
        configured.StartPhases();
        configured.TakeDamage(100);
        Check(configured.CurrentPhase is B01_Phase02, "正式配置进入阶段02");
        configured.TakeDamage(100);
        Check(configured.CurrentPhase is B01_Phase03, "正式配置进入阶段03");
        configured.Free();
        battle.Restart();
        battle.Player.Attack.Stop();
        // 发射边界前放入阈值伤害弹，碰撞须先取消旧阶段同刻发射。
        VerificationClock.BossSeconds(battle, 0.99);
        battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.PlayerSet) with
        {
            Position = battle.Boss.Position,
            Speed = 0,
            Damage = 100
        });
        battle.Timers.AdvanceByUnits(600, seconds =>
        {
            battle.Player.Advance(seconds, Vector2.Zero);
            battle.Boss.Advance(seconds);
            battle.Bullets.Advance(seconds, battle.Player, battle.Boss);
        });
        Check(battle.Boss.CurrentPhase is B01_Phase02 && battle.Bullets.ActiveCount == 0,
            "血线碰撞先于同刻发射，旧阶段不多发一轮");
        VerificationClock.BossSeconds(battle, 1);
        Check(battle.Bullets.ActiveCount == 72 && battle.Boss.CurrentPhase!.Emitters.Count == 2,
            "交接后只有新阶段成功发射的弹幕");
        world.Free();
    }
    /// <summary>验证玩家零血及负血仍可操作、攻击、受伤和正常胜利。</summary>
    private void VerifyNegativePlayerHealth()
    {
        // 停止Boss主动攻击，使所有伤害来源都由测试明确控制。
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        Check(battle.Player.Health.TakeDamage(3, false) && battle.Player.Health.Hp == 0, "玩家恰好归零");
        var before = battle.Player.Position;
        battle.StepFixed(Vector2.Right, false);
        Check(battle.State == BattleState.Running && battle.Player.Position.X > before.X, "零血继续战斗和移动");
        battle.StepFixed(Vector2.Right, true);
        Check(battle.Player.Dodge.IsActive, "零血继续闪避");
        VerificationClock.BattleSeconds(battle, 10.0 / 60, Vector2.Zero, false);
        Check(battle.Bullets.ActiveBullets.Any(bullet => bullet.Team == BulletTeam.Player), "零血自动射击继续");
        Check(!battle.Player.Health.TakeDamage(1, false), "零血仍受正常无敌保护");
        VerificationClock.BattleSeconds(battle, 0.8, Vector2.Zero, false);
        battle.Bullets.Clear();
        battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = battle.Player.Position,
            Speed = 0
        });
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.Player.Health.Hp == -1 && battle.State == BattleState.Running, "实际敌弹命中后血量下降到负数");
        before = battle.Player.Position;
        battle.StepFixed(Vector2.Left, true);
        Check(battle.Player.Position.X < before.X && battle.Player.Dodge.IsActive, "负血仍能移动闪避");
        VerificationClock.BattleSeconds(battle, 1, Vector2.Zero, false);
        Check(battle.Player.Health.TakeDamage(2, false) && battle.Player.Health.Hp == -3, "负血继续受到后续伤害");
        battle.Boss.TakeDamage(300);
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.State == BattleState.Victory && battle.Player.Health.Hp == -3, "玩家负血仍能取得胜利");
        battle.Restart();
        Check(battle.Player.Health.Hp == 3 && battle.Player.Health.Invulnerability == 0, "重开恢复玩家初始血量及状态");
        world.Free();
    }
    /// <summary>验证周期发射、双列表注销、外部控制和容量限制。</summary>
    private void VerifyBatches()
    {
        // 隔离场景，所有批次远离双方，避免非目标碰撞。
        var battle = CreateBattle(out var world);
        var manager = battle.Bullets;
        manager.Position = new Vector2(30, 40);
        var origin = new Vector2(-10000, -10000);
        battle.Player.Attack.Stop();
        // 停止初始阶段，以下计时验证只观察单独启动的发射器。
        battle.Boss.Stop();
        battle.Boss.GlobalPosition = origin;
        var delayed = new B01P01_Emitter02();
        delayed.Start(battle.Boss, manager);
        VerificationClock.EmitterSeconds(battle, 1, delayed);
        Check(delayed.Bullets.Count == 16 && delayed.Bullets.All(bullet => bullet.Speed == 300),
            "阶段01发射器02初速300");
        delayed.Stop();
        VerificationClock.BossSeconds(battle, 1.999);
        Check(delayed.Bullets.All(bullet => bullet.Speed == 300), "发射器02两秒前保持初速");
        VerificationClock.BossSeconds(battle, 0.001);
        Check(delayed.Bullets.All(bullet => bullet.Speed == 300), "发射器02两秒后仍保持出生速度300");
        var first = new B01P01_Emitter01();
        var second = new B01P01_Emitter01();
        first.Start(battle.Boss, manager);
        second.Start(battle.Boss, manager);
        VerificationClock.EmitterSeconds(battle, 1, first, second);
        Check(first.Bullets.Count == 24 && second.Bullets.Count == 24 && manager.ActiveCount == 64, "独立发射批次");
        Check(manager.ActiveBullets.Contains(first.Bullets[0]), "双列表为同一对象");
        Check(first.Bullets[0].GlobalPosition == origin, "全局起点转为容器局部位置");
        second.Stop();
        VerificationClock.EmitterSeconds(battle, 1, first);
        Check(first.Bullets.Count == 48 && manager.ActiveCount == 88, "时间线使同一发射器重复生成");
        first.Stop();
        // 外部代码选中一个批次调整，另一批次保持不变。
        foreach (var bullet in first.Bullets)
        {
            bullet.SetDirection(Mathf.Pi / 2);
            bullet.SetSpeed(50);
        }
        manager.Advance(0.1, battle.Player, battle.Boss);
        Check(first.Bullets[0].GlobalPosition.DistanceTo(origin + Vector2.Down * 5) < 0.01, "外部批次转向变速");
        Check(second.Bullets[0].GlobalPosition.DistanceTo(origin + Vector2.Right * 18) < 0.01, "其他批次不受影响");
        Check(Mathf.IsEqualApprox(first.Bullets[0].GetNode<Sprite2D>("Sprite").Rotation, Mathf.Pi / 2), "转向同步贴图");
        // 负角、多圈和速度反推均保持弧度、速度向量及贴图一致。
        var directed = first.Bullets[0];
        directed.SetDirection(-Math.PI / 2);
        Check(Math.Abs(directed.AngleRadians - Math.PI * 1.5) < 1e-12
            && directed.Velocity.DistanceTo(Vector2.Up * 50) < 0.001, "负弧度标准化并转向");
        directed.SetDirection(Math.Tau * 3 + 0.5);
        Check(Math.Abs(directed.AngleRadians - 0.5) < 1e-12, "多圈方向标准化");
        directed.SetDirection(-1e-8);
        Check(Math.Abs(directed.AngleRadians - (Math.Tau - 1e-8)) < 1e-12
            && directed.GetNode<Sprite2D>("Sprite").Rotation == 0, "双精度方向保留精度并在贴图边界归零");
        directed.SetDirection(Math.PI);
        directed.SetSpeed(40);
        Check(Math.Abs(directed.AngleRadians - Math.PI) < 1e-12 && directed.Speed == 40
            && Mathf.IsEqualApprox(directed.GetNode<Sprite2D>("Sprite").Rotation, Mathf.Pi), "方向与贴图一致");
        manager.Advance(4, battle.Player, battle.Boss);
        Check(first.Bullets.Count == 0 && second.Bullets.Count == 0 && manager.ActiveCount == 0, "过期同步注销");
        // 命中与清场通过同一注销入口，容量不足时不能新增节点。
        manager.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = battle.Player.GlobalPosition,
            Speed = 0
        });
        manager.Advance(0, battle.Player, battle.Boss);
        Check(manager.ActiveCount == 0, "直接生成的单颗弹幕命中后注销");
        for (int index = 0; index < BattleConfig.MaxBullets - 1; index++) manager.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with { Position = origin });
        var partial = new B01P01_Emitter01();
        partial.Start(battle.Boss, manager);
        VerificationClock.EmitterSeconds(battle, 1, partial);
        Check(partial.Bullets.Count == 1 && manager.ActiveCount == BattleConfig.MaxBullets
            && manager.GetChildCount() == BattleConfig.MaxBullets, "满额只登记成功对象");
        partial.Stop();
        var blocked = new B01P01_Emitter01();
        blocked.Start(battle.Boss, manager);
        VerificationClock.EmitterSeconds(battle, 1, blocked);
        Check(blocked.Bullets.Count == 0 && manager.GetChildCount() == BattleConfig.MaxBullets, "满额不产生孤立节点");
        blocked.Stop();
        manager.Clear();
        Check(partial.Bullets.Count == 0 && manager.ActiveCount == 0, "清场同步注销");
        // 重新建立阶段，确认退出后子弹仍存活。
        battle.Restart();
        battle.Player.Attack.Stop();
        VerificationClock.BossSeconds(battle, 1);
        var phase = battle.Boss.CurrentPhase!;
        var surviving = phase.Emitters[0];
        battle.Boss.Stop();
        Check(phase.Emitters.Count == 0 && surviving.Bullets.Count == 24, "退出阶段保留已发弹幕");
        battle.Bullets.Advance(0.1, battle.Player, battle.Boss);
        Check(surviving.Bullets[0].Age > 0, "退出后管理器继续推进");
        battle.Restart();
        Check(surviving.Bullets.Count == 0, "重开注销旧批次");
        battle.Player.Attack.Stop();
        VerificationClock.BossSeconds(battle, 1);
        phase = battle.Boss.CurrentPhase!;
        battle.Bullets.Clear();
        battle.Boss.Advance(0);
        Check(phase.Emitters.Count == 2 && phase.Emitters[0].Bullets.Count == 0,
            "清场不解除阶段绑定的发射器");
        VerificationClock.BossSeconds(battle, 1);
        surviving = phase.Emitters[0];
        world.Free();
        Check(surviving.Bullets.Count == 0, "直接离场注销引用");
        VerifyQueue();
    }
    /// <summary>验证队列实际生成、增量、批量操作和注销。</summary>
    private void VerifyQueue()
    {
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        var origin = new Vector2(-10000, -10000);
        var precise = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = origin,
            AngleRadians = Math.PI / 7,
            Speed = 123.456789,
            LifetimeSeconds = 4.123456789,
            Radius = 6.123456789,
            VisualScale = 1.123456789
        })!;
        Check(Math.Abs(precise.AngleRadians - Math.PI / 7) < 1e-12
            && precise.Speed == 123.456789 && precise.LifetimeSeconds == 4.123456789
            && precise.Radius == 6.123456789, "弹幕配置和运行状态保留双精度");
        precise.SetDirection(Math.PI / 11);
        Check(Math.Abs(precise.AngleRadians - Math.PI / 11) < 1e-12,
            "转向接口接受双精度弧度");
        var first = BulletDefaultSet.Get(BulletType.ScaleSet) with { AngleRadians = -Math.PI / 4, Speed = 50 };
        var queueSet = new BulletQueueSet
        {
            Amount = 7,
            XAdd = 2,
            YAdd = -1,
            AngleAdd = Math.PI / 12,
            SpeedAdd = 5
        };
        var queue = new BulletQueue(origin, first, queueSet);
        Check(ReferenceEquals(queue.BulletList, queue.BulletList)
            && ReferenceEquals(battle.Bullets.ActiveBullets, battle.Bullets.ActiveBullets),
            "活动列表复用只读视图");
        queue.Emit(new B01P01_Emitter01(), battle.Bullets);
        Check(queue.BulletList.Count == 7 && queue.BulletList[0].GlobalPosition == origin,
            "队列首颗位于参考点");
        Check(queue.BulletList[6].GlobalPosition == origin + new Vector2(12, -6)
            && queue.BulletList[6].Speed == 80
            && Math.Abs(queue.BulletList[6].AngleRadians - Math.PI / 4) < 1e-12,
            "位置角度速度按索引递增并形成扇形");
        queue.SetSpeed(100);
        queue.SetDirection(Math.PI / 2);
        Check(queue.BulletList.All(bullet => bullet.Speed == 100 && bullet.AngleRadians == Math.PI / 2),
            "队列批量控制所有存活成员");
        queue.SetSpeed(-100);
        Check(queue.BulletList.All(bullet => bullet.Speed == -100 && bullet.Velocity.Y < 0
            && bullet.AngleRadians == Math.PI / 2), "队列负速度沿原角度反向移动");
        queue.SetSpeed(100);
        var ring = new BulletQueue(origin, first with { AngleRadians = 0 }, queueSet with
        {
            Amount = 24,
            XAdd = 0,
            YAdd = 0,
            AngleAdd = Math.Tau / 24,
            SpeedAdd = 0
        });
        ring.Emit(new B01P01_Emitter01(), battle.Bullets);
        Check(ring.BulletList.Count == 24 && ring.BulletList[0].AngleRadians == 0
            && Math.Abs(ring.BulletList[23].AngleRadians - Math.Tau * 23 / 24) < 1e-12,
            "环形排列不重复终点");
        var reverse = new BulletQueue(origin, first with { AngleRadians = Math.PI / 6 }, new BulletQueueSet
        {
            Amount = 3,
            AngleAdd = -Math.PI / 12
        });
        reverse.Emit(new B01P01_Emitter01(), battle.Bullets);
        Check(Math.Abs(reverse.BulletList[1].AngleRadians - Math.PI / 12) < 1e-12
            && reverse.BulletList[2].AngleRadians == 0, "负增量逆时针排列");
        Check(queueSet.Amount == 7 && queueSet.XAdd == 2, "with派生不改变原队列配置");
        var signed = new BulletQueue(origin, first with { AngleRadians = 0 }, new BulletQueueSet
        {
            Amount = 7,
            SpeedAdd = -20
        });
        signed.Emit(new B01P01_Emitter01(), battle.Bullets);
        Check(signed.BulletList.Count == 7 && signed.BulletList[3].Speed == -10
            && signed.BulletList[3].Velocity.X < 0 && signed.BulletList[6].Speed == -70,
            "队列速度跨零并保持原角度");
        try { _ = new BulletQueue(origin, first, new BulletQueueSet { Amount = 0 }); Check(false, "队列数量校验"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
        try { _ = new BulletQueue(origin, first, new BulletQueueSet { Amount = 2, AngleAdd = double.NaN }); Check(false, "角度增量校验"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
        try { _ = new BulletQueue(origin, first, new BulletQueueSet { Amount = 2, SpeedAdd = -double.MaxValue }); Check(false, "最终速度校验"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
        battle.Bullets.Clear();
        Check(queue.BulletList.Count == 0 && ring.BulletList.Count == 0 && reverse.BulletList.Count == 0 && signed.BulletList.Count == 0,
            "清场移除队列的全部成员引用");
        var reverseMotion = battle.Bullets.Spawn(first with
        {
            Position = origin,
            AngleRadians = 0,
            Speed = -40
        })!;
        battle.Bullets.Advance(0.5, battle.Player, battle.Boss);
        Check(reverseMotion.GlobalPosition.DistanceTo(origin + Vector2.Left * 20) < 0.001
            && reverseMotion.AngleRadians == 0 && reverseMotion.Speed == -40
            && Mathf.IsEqualApprox(reverseMotion.GetNode<Sprite2D>("Sprite").Rotation, Mathf.Pi),
            "负速度实际反向位移且贴图跟随实际方向");
        world.Free();
    }
    /// <summary>验证四种弹幕贴图的资源路径、非贴图参数一致性及十色图集生成。</summary>
    private void VerifySpriteSets()
    {
        // 保留既有枚举值，避免外部记录的参数集编号变化。
        Check((int)BulletType.ScaleSet == 0 && (int)BulletType.PlayerSet == 1, "原预设编号稳定");
        // 使用隔离战斗验证真实生成路径，不推进战斗时间。
        var battle = CreateBattle(out var world);
        var scale = BulletDefaultSet.Get(BulletType.ScaleSet);
        var entries = new[]
        {
            (BulletType.ScaleSet, "scale"), (BulletType.DotSet, "dot"),
            (BulletType.DropSet, "drop"), (BulletType.StarSet, "star")
        };
        // 逐种预设及逐色索引检查，确保末帧也能正常切片。
        foreach (var (type, name) in entries)
        {
            var settings = BulletDefaultSet.Get(type);
            Check(settings.TexturePath == $"res://Assets/Sprites/Sprite_{name}.png", "新图集路径");
            Check((settings with { TexturePath = scale.TexturePath }) == scale, "除贴图外全部参数与鳞弹一致");
            for (int index = 0; index < 10; index++)
            {
                var bullet = battle.Bullets.Spawn(settings with { ColorIndex = index })!;
                var sprite = bullet.GetNode<Sprite2D>("Sprite");
                Check(sprite.Visible && sprite.Texture.ResourcePath == settings.TexturePath
                    && sprite.Texture.GetSize() == new Vector2(160, 16)
                    && sprite.Hframes == 10 && sprite.Vframes == 1 && sprite.Frame == index
                    && sprite.GetRect().Size == new Vector2(16, 16), "十色图集逐格生成");
            }
        }
        // 玩家圆点预设保持原行为，与新增圆形贴图弹幕分开。
        var player = BulletDefaultSet.Get(BulletType.PlayerSet);
        Check(!player.UseSprite && player.TexturePath is null && player.Team == BulletTeam.Player
            && player.Speed == 600 && player.Radius == 3, "玩家预设保持不变");
        world.Free();
    }
    /// <summary>验证枚举预设、覆盖顺序、快照隔离及可配置的弹幕外观。</summary>
    private void VerifyDefaultSets()
    {
        VerifySpriteSets();
        // 明确检查原敌弹与玩家弹的数值，防止迁移改变现有玩法。
        var scale = BulletDefaultSet.Get(BulletType.ScaleSet) with { };
        var player = BulletDefaultSet.Get(BulletType.PlayerSet) with { };
        Check(scale.Speed == 180 && scale.LifetimeSeconds == 4 && scale.Radius == 6
            && scale.Team == BulletTeam.Enemy && scale.Damage == 1, "鳞弹预设数值");
        Check(scale.TexturePath == "res://Assets/Sprites/Sprite_scale.png" && scale.Hframes == 10 && scale.Vframes == 1
            && scale.ColorIndex == 0 && scale.VisualScale == 3 && scale.UseSprite && scale.CircleColor == Colors.Cyan, "鳞弹预设外观");
        Check(player.Speed == 600 && player.LifetimeSeconds == 2 && player.Radius == 3
            && player.Team == BulletTeam.Player && player.Damage == 1, "玩家弹预设数值");
        Check(player.TexturePath is null && player.Hframes == 1 && player.Vframes == 1 && player.ColorIndex == 0
            && player.VisualScale == 3 && !player.UseSprite && player.CircleColor == Colors.Cyan, "玩家弹预设外观");
        // 将所有预设字段改为自定义值，再检查批量设置是否完整覆盖。
        var behavior = new StraightBehavior();
        var data = BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = new Vector2(10, 20),
            AngleRadians = 1.25f,
            Behavior = behavior,
            TexturePath = "res://Assets/Units/Boss_01.png",
            Hframes = 4,
            Vframes = 2,
            ColorIndex = 7,
            Speed = 25,
            LifetimeSeconds = 10,
            Radius = 12,
            Team = BulletTeam.Enemy,
            Damage = 8,
            VisualScale = 2,
            UseSprite = true,
            CircleColor = Colors.Red
        };
        var switched = BulletDefaultSet.Get(BulletType.PlayerSet) with
        {
            Position = data.Position,
            AngleRadians = data.AngleRadians,
            Behavior = behavior
        };
        Check(switched.TexturePath == player.TexturePath && switched.Hframes == player.Hframes
            && switched.Vframes == player.Vframes && switched.ColorIndex == player.ColorIndex
            && switched.Speed == player.Speed && switched.LifetimeSeconds == player.LifetimeSeconds
            && switched.Radius == player.Radius
            && switched.Team == player.Team && switched.Damage == player.Damage
            && switched.VisualScale == player.VisualScale && switched.UseSprite == player.UseSprite
            && switched.CircleColor == player.CircleColor, "预设切换保留其余字段");
        Check(switched.Position == new Vector2(10, 20) && switched.AngleRadians == 1.25f
            && ReferenceEquals(switched.Behavior, behavior), "位置角度行为不被覆盖");
        var customized = switched with
        {
            Speed = 250,
            Team = BulletTeam.Enemy
        };
        Check(customized.Speed == 250 && customized.Team == BulletTeam.Enemy && player.Speed == 600
            && BulletDefaultSet.Get(BulletType.PlayerSet).Speed == 600, "逐项赋值及预设隔离");
        // 未知枚举不能取得参数集。
        try { _ = BulletDefaultSet.Get((BulletType)999); Check(false, "未知枚举未拦截"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
        // 在真实管理器中检查单发批次快照、生成后隔离和自定义二维图集。
        var battle = CreateBattle(out var world);
        var origin = new Vector2(-10000, -10000);
        var batchSettings = BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = origin,
            Speed = 123
        };
        batchSettings = batchSettings with { Speed = 456 };
        var shot = battle.Bullets.Spawn(batchSettings with { Speed = 123 })!;
        Check(shot.Speed == 123 && shot.Team == BulletTeam.Enemy && shot.LifetimeSeconds == 4, "单颗生成保存参数快照");
        batchSettings = batchSettings with { Speed = 999 };
        Check(shot.Speed == 123, "初始化数据不改变已生成子弹");
        var atlasData = BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = origin,
            TexturePath = "res://Assets/Units/Boss_01.png",
            Hframes = 4,
            Vframes = 2,
            ColorIndex = 7,
            VisualScale = 2
        };
        var atlasBullet = battle.Bullets.Spawn(atlasData)!;
        var sprite = atlasBullet.GetNode<Sprite2D>("Sprite");
        Check(sprite.Texture.ResourcePath == atlasData.TexturePath && sprite.Hframes == 4 && sprite.Vframes == 2
            && sprite.Frame == 7 && sprite.GetRect().Size == new Vector2(32, 64), "配置贴图与二维图集末帧");
        var dotData = BulletDefaultSet.Get(BulletType.PlayerSet) with
        {
            Position = origin,
            CircleColor = Colors.Magenta
        };
        var dot = battle.Bullets.Spawn(dotData)!;
        Check(!dot.GetNode<Sprite2D>("Sprite").Visible && dot.GetNode<Sprite2D>("Sprite").Texture is null
            && dot.CircleColor == Colors.Magenta, "圆点颜色与无贴图模式");
        // 无效布局、间隔和资源应在创建节点前失败。
        foreach (var invalid in new[]
        {
            atlasData with { ColorIndex = 8 }, atlasData with { Hframes = 0 }, atlasData with { Vframes = -1 },
            atlasData with { TexturePath = null }, atlasData with { TexturePath = "res://Assets/missing.png" },
            atlasData with { TexturePath = "res://Data/Bosses/Boss_01.tres" },
            atlasData with { CircleColor = new Color(float.NaN, 0, 0) },
            atlasData with { Position = new Vector2(float.NaN, 0) },
            atlasData with { AngleRadians = float.NaN },
            atlasData with { Behavior = null! }
        })
        {
            var beforeCount = battle.Bullets.ActiveCount;
            try { battle.Bullets.Spawn(invalid); Check(false, "无效参数未拦截"); }
            catch (ArgumentException) { _checks++; }
            Check(battle.Bullets.ActiveCount == beforeCount && battle.Bullets.GetChildCount() == beforeCount, "校验失败无节点残留");
        }
        // 玩家组件直接推进大步长，确认首发等待及余量仍来自玩家预设间隔。
        battle.Bullets.Clear();
        battle.Player.Attack.Stop();
        battle.Boss.Stop();
        battle.Player.Position = origin;
        var attack = new PlayerAttack();
        attack.Initialize(battle.Player, battle.Bullets);
        battle.Timers.AdvanceByUnits(11400, _ => battle.Player.Timeline!.AdvanceUnits(11400));
        Check(battle.Bullets.ActiveCount == 0, "玩家预设首发等待");
        battle.Timers.AdvanceByUnits(600, _ => battle.Player.Timeline!.AdvanceUnits(600));
        battle.Timers.AdvanceByUnits(30000, _ => battle.Player.Timeline!.AdvanceUnits(30000));
        Check(battle.Bullets.ActiveCount == 3, "玩家预设大步长补发");
        battle.Timers.AdvanceByUnits(6000, _ => battle.Player.Timeline!.AdvanceUnits(6000));
        Check(battle.Bullets.ActiveCount == 4, "玩家预设计时余量");
        world.Free();
    }

	/// <summary>确认正式场景同时支持物理键和辅助设备逻辑键。</summary>
	private void VerifyInput()
	{
		// 装配正式入口，检查实际注册的动作而非复制输入规则。
		var main = new Main();
		AddChild(main);
		main.GetNode<BattleManager>("BattleManager").SetPhysicsProcess(false);
		// 通过真实资源加载验证脚本迁移路径，并检查正式背景配置。
		Check(ResourceLoader.Exists("res://Boss/BossController.cs") && ResourceLoader.Exists("res://Bullet/Bullet.cs"), "模块脚本资源路径");
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
}
