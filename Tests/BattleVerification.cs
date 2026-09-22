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
	/// <summary>执行回归集，失败时以非零状态退出。</summary>
	private void Run()
	{
		try
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
		VerificationClock.BossSeconds(boss, 0.99);
		Check(container.GetChildCount() == 0, "第一秒前不能发射");
		VerificationClock.BossSeconds(boss, 0.01);
		Check(container.GetChildCount() == 48, "第一秒双发射器各24发");
		// 每颗弹的序号对应π/12弧度间距。
		for (int index = 0; index < 48; index++)
		{
			var bullet = container.GetChild<Bullet>(index);
			var sprite = bullet.GetNode<Sprite2D>("Sprite");
			Check(Mathf.IsEqualApprox(bullet.AngleRadians, index % 24 * Mathf.Tau / 24), "环形角度");
			Check(bullet.Speed == 180 && bullet.LifetimeSeconds == 4, "原弹速寿命");
			Check(sprite.Scale == Vector2.One * 3 && sprite.Frame == 0 && sprite.Centered, "原弹幕外观");
			Check(sprite.TextureFilter == CanvasItem.TextureFilterEnum.Nearest, "最近邻");
		}
		VerificationClock.BossSeconds(boss, 2.5);
		Check(container.GetChildCount() == 144, "大步长补发");
		VerificationClock.BossSeconds(boss, 0.5);
		Check(container.GetChildCount() == 192, "发射余量");
		// 所有实际子弹由管理器推进，外部初始化入口不再自行更新。
        var sampleBatch = new SingleBulletEmitter(new BulletSpawnData
        {
            AngleRadians = Mathf.Pi / 2, Speed = 100, LifetimeSeconds = 0.25f, VisualScale = 2
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
		// 在场节点供独立组件绑定，逻辑时间由处理器推进。
        var componentOwner = new Node2D();
        AddChild(componentOwner);
        var componentTimers = new VTimerProcessor();
        var dodge = new PlayerDodge();
        dodge.Initialize(componentOwner, componentTimers);
		Check(dodge.TryStart(Vector2.Zero) && dodge.Direction == Vector2.Up, "默认向上闪避");
		Check(!dodge.TryStart(Vector2.Right), "闪避冷却阻止重复");
		componentTimers.AdvanceByUnits(9000);
		Check(!dodge.IsActive && Math.Abs(dodge.Cooldown - 0.85) < 1e-6, "闪避与冷却独立");
		componentTimers.AdvanceByUnits(51000);
		Check(dodge.TryStart(Vector2.Right), "冷却恢复");
				// 60Hz下不允许浮点余量额外延长闪避、冷却和无敌。
		componentTimers.Clear(true);
        var fixedDodge = new PlayerDodge();
        fixedDodge.Initialize(componentOwner, componentTimers);
		fixedDodge.TryStart(Vector2.Up);
		for (int tick = 0; tick < 9; tick++) componentTimers.AdvanceByUnits(1000);
		Check(!fixedDodge.IsActive, "9个固定步结束闪避");
		for (int tick = 9; tick < 60; tick++) componentTimers.AdvanceByUnits(1000);
		Check(fixedDodge.Cooldown == 0, "60个固定步恢复冷却");
		var fixedHealth = new PlayerHealth();
        fixedHealth.Initialize(componentOwner, componentTimers);
		fixedHealth.TakeDamage(1, false);
		for (int tick = 0; tick < 60; tick++) componentTimers.AdvanceByUnits(1000);
		Check(fixedHealth.TakeDamage(1, false), "60个固定步结束受击无敌");
		// 独立生命组件与事件计数。
		componentTimers.Clear(true);
        var health = new PlayerHealth();
        health.Initialize(componentOwner, componentTimers);
		var changes = 0;
		health.HealthChanged += hp => changes++;
		Check(!health.TakeDamage(1, true), "闪避无敌");
		Check(health.TakeDamage(1, false) && !health.TakeDamage(1, false), "受击防重复");
		componentTimers.AdvanceByUnits(60000);
		Check(health.TakeDamage(99, false) && health.Hp == -97, "生命允许降为负数");
		Check(!health.TakeDamage(1, false) && changes == 2, "负血保留受击保护与通知");
        componentOwner.Free();
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
		var enemy = battle.Bullets.Spawn(new BulletSpawnData { Position = battle.Player.GlobalPosition - new Vector2(100, 0) })!;
		enemy.Velocity = Vector2.Right * 12000;
		battle.StepFixed( Vector2.Zero, false);
		Check(battle.Player.Health.Hp == 2, "实际高速敌弹命中");
		Check(battle.Boss.Hp == 300, "敌弹不伤Boss");
		var passing = battle.Bullets.Spawn(new BulletSpawnData { Position = battle.Player.GlobalPosition })!;
		passing.Velocity = Vector2.Zero;
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
		battle.Timers.AdvanceByUnits(60000);
        battle.Bullets.Clear();
		battle.Boss.TakeDamage(299);
		battle.Bullets.Spawn(new BulletSpawnData { Position = battle.Player.GlobalPosition })!.Velocity = Vector2.Zero;
		battle.Bullets.Spawn(new BulletSpawnData(BulletType.PlayerSet) { Position = battle.Boss.GlobalPosition })!.Velocity = Vector2.Zero;
		battle.StepFixed( Vector2.Zero, false);
		Check(battle.Boss.Hp == 0 && battle.Player.Health.Hp == 0 && battle.State == BattleState.Victory, "玩家同段零血不阻止Boss击败胜利");
		battle.Restart();
		// 容量测试保持所有弹幕远离目标。
		for (int index = 0; index < BattleConfig.MaxBullets; index++) battle.Bullets.Spawn(new BulletSpawnData { Position = new Vector2(-10000, 0) });
		Check(battle.Bullets.Spawn(new BulletSpawnData()) is null, "容量上限");
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
		boss.TakeDamage(300);
		boss.TakeDamage(300);
		boss.Stop();
		Check(second.Exits == 1 && deaths == 1, "死亡退出一次");
		world.Free();
	}
    /// <summary>验证Boss_01三阶段血量阈值、六种发射器及阶段计时器交接。</summary>
    private void VerifyBoss01Stages()
    {
        // 正式资源与无配置入口都必须使用300血和相同阶段组合。
        var data = GD.Load<BossData>("res://Data/Bosses/Boss_01.tres");
        Check(data.MaxHp == 300, "正式Boss01资源300血");
        var battle = CreateBattle(out var world);
        battle.Player.Attack.Stop();
        var boss = battle.Boss;
        Check(boss.MaxHp == 300 && boss.Hp == 300 && boss.CurrentPhase is B01_Phase01, "默认入口首阶段300血");
        VerificationClock.BossSeconds(boss, 1);
        var first = boss.CurrentPhase!;
        var surviving = first.Emitters[0];
        // 保存六个已发批次，阶段退出清理索引后仍能验证存活子弹。
        var batches = first.Emitters.ToList();
        Check(first.Emitters.Count == 2 && first.Emitters[0] is B01P01_Emitter01
            && first.Emitters[1] is B01P01_Emitter02, "阶段01独立双发射器");
        boss.TakeDamage(99);
        Check(boss.Hp == 201 && ReferenceEquals(first, boss.CurrentPhase), "201血保持阶段01");
        boss.TakeDamage(1);
        Check(boss.Hp == 200 && boss.CurrentPhase is B01_Phase02, "200血立即进入阶段02");
        Check(first.Emitters.Count == 0 && surviving.Bullets.Count == 24
            && battle.Timers.ActiveCount == 2, "旧阶段取消计时器但保留既有子弹");
        VerificationClock.BossSeconds(boss, 0.99);
        Check(boss.CurrentPhase!.Emitters.Count == 0 && battle.Bullets.ActiveCount == 48, "新阶段首次等待完整周期");
        VerificationClock.BossSeconds(boss, 0.01);
        var second = boss.CurrentPhase!;
        batches.AddRange(second.Emitters);
        Check(second.Emitters.Count == 2 && second.Emitters[0] is B01P02_Emitter01
            && second.Emitters[1] is B01P02_Emitter02 && battle.Bullets.ActiveCount == 96, "阶段02仅发两个独立批次");
        boss.TakeDamage(99);
        Check(boss.Hp == 101 && ReferenceEquals(second, boss.CurrentPhase), "101血保持阶段02");
        boss.TakeDamage(1);
        Check(boss.CurrentPhase is B01_Phase03 && battle.Timers.ActiveCount == 2, "100血进入阶段03且无重复计时器");
        VerificationClock.BossSeconds(boss, 1);
        var third = boss.CurrentPhase!;
        batches.AddRange(third.Emitters);
        Check(third.Emitters.Count == 2 && third.Emitters[0] is B01P03_Emitter01
            && third.Emitters[1] is B01P03_Emitter02, "阶段03独立双发射器");
        // 六个具体发射器暂时都保留24颗、速度180及等角弧度设计。
        Check(batches.Count == 6, "三阶段共六个独立发射器批次");
        foreach (var emitter in batches)
        {
            Check(emitter.Bullets.Count == 24, "每个发射器均生成24颗弹幕");
            for (int index = 0; index < emitter.Bullets.Count; index++)
                Check(emitter.Bullets[index].Speed == 180
                    && Mathf.IsEqualApprox(emitter.Bullets[index].AngleRadians, index * Mathf.Tau / 24), "阶段弹幕参数保持一致");
        }
        boss.TakeDamage(99);
        Check(boss.Hp == 1 && ReferenceEquals(third, boss.CurrentPhase), "1血保持最后阶段");
        boss.TakeDamage(1);
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.State == BattleState.Victory && boss.CurrentPhase is null && battle.Timers.ActiveCount == 0, "0血胜利清理全部阶段");
        battle.Restart();
        Check(battle.Boss.Hp == 300 && battle.Boss.CurrentPhase is B01_Phase01 && battle.Timers.ActiveCount == 3, "重开恢复三阶段初始配置");
        // 单次跨两条血线时有序切换，不能停留一物理步后再补切。
        var entered = new System.Collections.Generic.List<Type>();
        battle.Boss.PhaseChanged += phase => entered.Add(phase.GetType());
        battle.Boss.TakeDamage(250);
        Check(battle.Boss.Hp == 50 && battle.Boss.CurrentPhase is B01_Phase03
            && entered.SequenceEqual(new[] { typeof(B01_Phase02), typeof(B01_Phase03) }), "大伤害同刻跨过两个阶段");
        Check(battle.Timers.ActiveCount == 3, "跳阶段不残留中间阶段计时器");
        // 正式工厂入口也注册三个独立阶段。
        var configured = BossFactory.Create(data, battle.Bullets);
        world.AddChild(configured);
        configured.TakeDamage(100);
        Check(configured.CurrentPhase is B01_Phase02, "正式配置进入阶段02");
        configured.TakeDamage(100);
        Check(configured.CurrentPhase is B01_Phase03, "正式配置进入阶段03");
        configured.Free();
        battle.Restart();
        battle.Player.Attack.Stop();
        // 发射边界前放入阈值伤害弹，碰撞须先取消旧阶段同刻发射。
        VerificationClock.BossSeconds(battle.Boss, 0.99);
        battle.Bullets.Spawn(new BulletSpawnData(BulletType.PlayerSet)
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
        VerificationClock.BossSeconds(battle.Boss, 1);
        Check(battle.Bullets.ActiveCount == 48 && battle.Boss.CurrentPhase!.Emitters.Count == 2,
            "交接后只有新阶段两批弹幕");
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
        battle.Bullets.Spawn(new BulletSpawnData { Position = battle.Player.Position, Speed = 0 });
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
    /// <summary>验证单次批次、双列表注销、外部控制和可选角度排列。</summary>
    private void VerifyBatches()
    {
        // 隔离场景，所有批次远离双方，避免非目标碰撞。
        var battle = CreateBattle(out var world);
        var manager = battle.Bullets;
        manager.Position = new Vector2(30, 40);
        var origin = new Vector2(-10000, -10000);
        battle.Player.Attack.Stop();
        var first = new B01P01_Emitter01();
        var second = new B01P01_Emitter01();
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
            bullet.SetDirection(Mathf.Pi / 2);
            bullet.SetSpeed(50);
        }
        manager.Advance(0.1, battle.Player, battle.Boss);
        Check(first.Bullets[0].GlobalPosition.DistanceTo(origin + Vector2.Down * 5) < 0.01, "外部批次转向变速");
        Check(second.Bullets[0].GlobalPosition.DistanceTo(origin + Vector2.Right * 18) < 0.01, "其他批次不受影响");
        Check(Mathf.IsEqualApprox(first.Bullets[0].GetNode<Sprite2D>("Sprite").Rotation, Mathf.Pi / 2), "转向同步贴图");
        // 负角、多圈和速度反推均保持弧度、速度向量及贴图一致。
        var directed = first.Bullets[0];
        directed.SetDirection(-Mathf.Pi / 2);
        Check(Mathf.IsEqualApprox(directed.AngleRadians, Mathf.Pi * 1.5f)
            && directed.Velocity.DistanceTo(Vector2.Up * 50) < 0.001, "负弧度标准化并转向");
        directed.SetDirection(Mathf.Tau * 3 + 0.5f);
        Check(Mathf.IsEqualApprox(directed.AngleRadians, 0.5f), "多圈方向标准化");
        directed.SetDirection(-1e-8f);
        Check(directed.AngleRadians == 0, "单精度上界舍入归零");
        directed.Velocity = Vector2.Left * 40;
        Check(Mathf.IsEqualApprox(directed.AngleRadians, Mathf.Pi) && directed.Speed == 40
            && Mathf.IsEqualApprox(directed.GetNode<Sprite2D>("Sprite").Rotation, Mathf.Pi), "速度反推弧度与贴图一致");
        manager.Advance(4, battle.Player, battle.Boss);
        Check(first.Bullets.Count == 0 && second.Bullets.Count == 0 && manager.ActiveCount == 0, "过期同步注销");
        // 命中与清场通过同一注销入口，容量不足时不能新增节点。
        var hit = new SingleBulletEmitter(new BulletSpawnData { Speed = 0 });
        hit.Emit(manager, battle.Player.GlobalPosition);
        manager.Advance(0, battle.Player, battle.Boss);
        Check(hit.Bullets.Count == 0 && manager.ActiveCount == 0, "命中同步注销");
        for (int index = 0; index < BattleConfig.MaxBullets - 1; index++) manager.Spawn(new BulletSpawnData { Position = origin });
        var partial = new B01P01_Emitter01();
        partial.Emit(manager, origin);
        Check(partial.Bullets.Count == 1 && manager.ActiveCount == BattleConfig.MaxBullets
            && manager.GetChildCount() == BattleConfig.MaxBullets, "满额只登记成功对象");
        var blocked = new B01P01_Emitter01();
        blocked.Emit(manager, origin);
        Check(blocked.Bullets.Count == 0 && manager.GetChildCount() == BattleConfig.MaxBullets, "满额不产生孤立节点");
        manager.Clear();
        Check(partial.Bullets.Count == 0 && manager.ActiveCount == 0, "清场同步注销");
        // 阶段退出后子弹仍存活，空批次在下一次推进时移除。
        VerificationClock.BossSeconds(battle.Boss, 1);
        var phase = battle.Boss.CurrentPhase!;
        var surviving = phase.Emitters[0];
        battle.Boss.Stop();
        Check(phase.Emitters.Count == 0 && surviving.Bullets.Count == 24, "退出阶段保留已发弹幕");
        manager.Advance(0.1, battle.Player, battle.Boss);
        Check(surviving.Bullets[0].Age > 0, "退出后管理器继续推进");
        battle.Restart();
        Check(surviving.Bullets.Count == 0, "重开注销旧批次");
        battle.Player.Attack.Stop();
        VerificationClock.BossSeconds(battle.Boss, 1);
        phase = battle.Boss.CurrentPhase!;
        battle.Bullets.Clear();
        battle.Boss.Advance(0);
        Check(phase.Emitters.Count == 0, "阶段移除空批次");
        VerificationClock.BossSeconds(battle.Boss, 1);
        surviving = phase.Emitters[0];
        world.Free();
        Check(surviving.Bullets.Count == 0, "直接离场注销引用");
        // 可选排列辅助工具只生成方向，不创建任何节点。
        var angles = new System.Collections.Generic.List<float>();
        new AngularPattern().Emit(angles.Add, 0);
        Check(angles.Count == 24 && angles[0] == 0 && Mathf.IsEqualApprox(angles[23], Mathf.Tau * 23 / 24), "圆环无重复终点");
        angles.Clear();
        new AngularPattern(7, Mathf.Pi / 12).Emit(angles.Add, -Mathf.Pi / 4);
        Check(angles.Count == 7 && Mathf.IsEqualApprox(angles[0], Mathf.Pi * 7 / 4) && Mathf.IsEqualApprox(angles[6], Mathf.Pi / 4), "π/2弧度扇形");
        angles.Clear();
        new AngularPattern(3, Mathf.Pi / 12, false).Emit(angles.Add, Mathf.Pi / 6);
        Check(Mathf.IsEqualApprox(angles[0], Mathf.Pi / 6) && Mathf.IsEqualApprox(angles[1], Mathf.Pi / 12) && angles[2] == 0, "逆时针排列");
        angles.Clear();
        new AngularPattern(1, Mathf.Pi / 12).Emit(angles.Add, 2);
        Check(angles.Count == 1 && angles[0] == 2, "单发初始方向");
        angles.Clear();
        new AngularPattern(3, 0).Emit(angles.Add, 0.5f);
        Check(angles.Count == 3 && angles.TrueForAll(angle => angle == 0.5f), "零间隔同方向");
        try { _ = new AngularPattern(0); Check(false, "数量校验"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
        try { _ = new AngularPattern(1, float.NaN); Check(false, "间隔校验"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
    }
    /// <summary>验证枚举预设、覆盖顺序、快照隔离及可配置的弹幕外观。</summary>
    private void VerifyDefaultSets()
    {
        // 明确检查原敌弹与玩家弹的数值，防止迁移改变现有玩法。
        var scale = new BulletSpawnData();
        var player = new BulletSpawnData(BulletType.PlayerSet);
        Check(scale.Speed == 180 && scale.LifetimeSeconds == 4 && scale.Radius == 6 && scale.IntervalSeconds == 1
            && scale.Team == BulletTeam.Enemy && scale.Damage == 1, "鳞弹预设数值");
        Check(scale.TexturePath == "res://Assets/Sprite_02.png" && scale.Hframes == 10 && scale.Vframes == 1
            && scale.ColorIndex == 0 && scale.VisualScale == 3 && scale.UseSprite && scale.CircleColor == Colors.Cyan, "鳞弹预设外观");
        Check(player.Speed == 600 && player.LifetimeSeconds == 2 && player.Radius == 3 && player.IntervalSeconds == 0.2
            && player.Team == BulletTeam.Player && player.Damage == 1, "玩家弹预设数值");
        Check(player.TexturePath is null && player.Hframes == 1 && player.Vframes == 1 && player.ColorIndex == 0
            && player.VisualScale == 3 && !player.UseSprite && player.CircleColor == Colors.Cyan, "玩家弹预设外观");
        // 将所有预设字段改为自定义值，再检查批量设置是否完整覆盖。
        var behavior = new StraightBehavior();
        var data = new BulletSpawnData
        {
            Position = new Vector2(10, 20), AngleRadians = 1.25f, Behavior = behavior,
            TexturePath = "res://Assets/Units/Boss_01.png", Hframes = 4, Vframes = 2, ColorIndex = 7,
            Speed = 25, LifetimeSeconds = 10, Radius = 12, IntervalSeconds = 5, Team = BulletTeam.Enemy,
            Damage = 8, VisualScale = 2, UseSprite = true, CircleColor = Colors.Red
        };
        data.setPattern(BulletType.PlayerSet);
        Check(data == (player with
        {
            Position = data.Position,
            AngleRadians = data.AngleRadians,
            Behavior = behavior
        }), "覆盖全部预设字段");
        Check(data.Position == new Vector2(10, 20) && data.AngleRadians == 1.25f && ReferenceEquals(data.Behavior, behavior), "位置角度行为不被覆盖");
        data.Speed = 250;
        data.Team = BulletTeam.Enemy;
        Check(data.Speed == 250 && data.Team == BulletTeam.Enemy && player.Speed == 600
            && BulletDefaultSet.Get(BulletType.PlayerSet).Speed == 600, "逐项赋值及预设隔离");
        // 未知枚举不能让原对象出现部分修改。
        var beforeInvalid = data with { };
        try { data.setPattern((BulletType)999); Check(false, "未知枚举未拦截"); }
        catch (ArgumentOutOfRangeException) { _checks++; }
        Check(data == beforeInvalid, "未知枚举不改变原参数");
        // 在真实管理器中检查单发批次快照、生成后隔离和自定义二维图集。
        var battle = CreateBattle(out var world);
        var origin = new Vector2(-10000, -10000);
        data.setPattern(BulletType.ScaleSet);
        data.Speed = 123;
        var batch = new SingleBulletEmitter(data);
        data.setPattern(BulletType.PlayerSet);
        batch.Emit(battle.Bullets, origin);
        var shot = batch.Bullets[0];
        Check(shot.Speed == 123 && shot.Team == BulletTeam.Enemy && shot.LifetimeSeconds == 4, "批次保留创建时快照");
        data.Speed = 999;
        Check(shot.Speed == 123, "初始化数据不改变已生成子弹");
        var atlasData = new BulletSpawnData
        {
            Position = origin, TexturePath = "res://Assets/Units/Boss_01.png",
            Hframes = 4, Vframes = 2, ColorIndex = 7, VisualScale = 2
        };
        var atlasBullet = battle.Bullets.Spawn(atlasData)!;
        var sprite = atlasBullet.GetNode<Sprite2D>("Sprite");
        Check(sprite.Texture.ResourcePath == atlasData.TexturePath && sprite.Hframes == 4 && sprite.Vframes == 2
            && sprite.Frame == 7 && sprite.GetRect().Size == new Vector2(32, 64), "配置贴图与二维图集末帧");
        var dotData = new BulletSpawnData(BulletType.PlayerSet) { Position = origin, CircleColor = Colors.Magenta };
        var dot = battle.Bullets.Spawn(dotData)!;
        Check(!dot.GetNode<Sprite2D>("Sprite").Visible && dot.GetNode<Sprite2D>("Sprite").Texture is null
            && dot.CircleColor == Colors.Magenta, "圆点颜色与无贴图模式");
        // 无效布局、间隔和资源应在创建节点前失败。
        foreach (var invalid in new[]
        {
            atlasData with { ColorIndex = 8 }, atlasData with { Hframes = 0 }, atlasData with { Vframes = -1 },
            atlasData with { IntervalSeconds = 0 }, atlasData with { IntervalSeconds = double.NaN },
            atlasData with { TexturePath = null }, atlasData with { TexturePath = "res://Assets/missing.png" },
            atlasData with { TexturePath = "res://Data/Bosses/Boss_01.tres" },
            atlasData with { CircleColor = new Color(float.NaN, 0, 0) }
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
        attack.Initialize(battle.Player, battle.Bullets, battle.Boss);
        battle.Timers.AdvanceByUnits(11400);
        Check(battle.Bullets.ActiveCount == 0, "玩家预设首发等待");
        battle.Timers.AdvanceByUnits(600);
        battle.Timers.AdvanceByUnits(30000);
        Check(battle.Bullets.ActiveCount == 3, "玩家预设大步长补发");
        battle.Timers.AdvanceByUnits(6000);
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
