using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>AI、矩形查询、随机隔离与选择界面的定向验证，不运行全项目回归。</summary>
public partial class AIVerification : Node
{
    // 已通过的定向断言数量。
    private int _checks;
    /// <summary>等待节点入树后执行同步及界面验证。</summary>
    public override void _Ready() => Callable.From(Run).CallDeferred();

    /// <summary>运行本功能测试，失败时以非零退出码返回。</summary>
    private async void Run()
    {
        try
        {
            if (OS.GetCmdlineUserArgs().Contains("--capture-ai"))
            {
                await VerifyUI();
                GD.Print($"PASS: {_checks} AI rendered UI assertions");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--targeted-ai-behavior"))
            {
                VerifyStrategy();
                VerifyRestPosition();
                VerifyReplayAndIsolation();
                GD.Print($"PASS: {_checks} targeted AI behavior assertions");
                GetTree().Quit();
                return;
            }
            VerifyRandomStreams();
            VerifyGeometry();
            VerifyQueries();
            VerifyStrategy();
            VerifyRestPosition();
            VerifyDamageAndLifecycle();
            VerifyReplayAndIsolation();
            VerifyDecisionCost();
            await VerifyUI();
            GD.Print($"PASS: {_checks} targeted AI assertions");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    /// <summary>记录行为断言，失败时提供定位信息。</summary>
    /// <param name="condition">应成立的条件。</param>
    /// <param name="message">行为说明。</param>
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    /// <summary>确认非法输入不会被静默接受。</summary>
    /// <param name="action">预期抛出参数异常的操作。</param>
    /// <param name="message">行为说明。</param>
    private void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { Check(true, message); return; }
        throw new Exception(message);
    }

    /// <summary>创建由测试手动推进的隔离战斗。</summary>
    /// <param name="world">返回拥有本战斗的节点，调用者释放。</param>
    /// <param name="config">可选AI配置，默认启用基础策略。</param>
    /// <param name="quiet">默认停止Boss和玩家攻击，以隔离手工弹幕。</param>
    /// <returns>禁止自动物理更新的战斗管理器。</returns>
    private BattleManager Create(out Node2D world, AICharConfig? config = null, bool quiet = true)
    {
        world = new Node2D();
        AddChild(world);
        // 每次创建完整初态，包括玩家保护和所有时间线。
        var battle = new BattleManager();
        world.AddChild(battle);
        battle.Initialize(world, aiConfig: config ?? new AICharConfig { Enabled = true });
        battle.SetPhysicsProcess(false);
        if (quiet) { battle.Boss.Stop(); battle.Player.Attack.Stop(); }
        return battle;
    }

    /// <summary>生成手工普通弹，不使用随机数。</summary>
    /// <param name="battle">所属战斗。</param>
    /// <param name="position">世界逻辑像素位置。</param>
    /// <param name="radius">碰撞半径像素，默认3。</param>
    /// <param name="team">阵营，默认敌方。</param>
    /// <param name="speed">向右速度，像素每秒，默认静止。</param>
    /// <param name="damage">伤害点数，默认1。</param>
    /// <returns>已登记的普通弹。</returns>
    private static VBullet Spawn(BattleManager battle, Vector2 position, double radius = 3,
        VBulletTeam team = VBulletTeam.Enemy, double speed = 0, int damage = 1)
        => battle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.PlayerSet) with
        {
            Position = position,
            Radius = radius,
            Team = team,
            Speed = speed,
            Damage = damage,
            LifeTimeMs = 10000
        })!;

    /// <summary>验证固定样本、重建、区间映射及默认流隔离。</summary>
    private void VerifyRandomStreams()
    {
        // 独立流必须匹配现有SplitMix64标准输出。
        var stream = VMath.CreateRandomStream(0);
        // 固定算法的前三个已知整数样本。
        int[] expected = { 0x6220A839, unchecked((int)0xEE789E6A), unchecked((int)0x86C45D18) };
        // 逐个对照固定序列，防止算法或映射发生变化。
        foreach (int sample in expected) Check(stream.GetRandomInt(int.MinValue, int.MaxValue) == sample, "独立流固定算法样本");
        VMath.setRandomSeed(-123);
        stream = VMath.CreateRandomStream(-123);
        // 当前对照轮次，每轮交错整数、小数与偏移抽样。
        for (int index = 0; index < 128; index++)
        {
            Check(stream.GetRandomInt(-300, 800) == VMath.getRandomInt(-300, 800), "整数映射兼容");
            Check(stream.GetRandomDouble(-5, 10) == VMath.getRandomDouble(-5, 10), "小数映射兼容");
            Check(stream.GetRandomDiff(12, RandomDiffMode.Center) == VMath.getRandomDiff(12, RandomDiffMode.Center), "偏移映射兼容");
        }
        VMath.setRandomSeed(77);
        // 默认流下一次应返回的样本。
        int next = VMath.getRandomInt(int.MinValue, int.MaxValue);
        VMath.setRandomSeed(77);
        // 创建、交错使用和重建其他流均不能消耗原流。
        var first = VMath.CreateRandomStream(19);
        var second = VMath.CreateRandomStream(19);
        // 当前重复验证步，覆盖随机平局及持续移动。
        for (int index = 0; index < 100; index++)
        {
            Check(first.GetRandomInt(0, 1000) == second.GetRandomInt(0, 1000), "相同独立种子可重现");
            VMath.CreateRandomStream(index).GetRandomDouble(0, 1);
        }
        Check(VMath.getRandomInt(int.MinValue, int.MaxValue) == next && VMath.randomSeed == 77, "独立流不改变默认流");
        first = VMath.CreateRandomStream(9);
        second = VMath.CreateRandomStream(9);
        first.GetRandomInt(2, 2);
        first.GetRandomDouble(3, 3);
        first.GetRandomDiff(0);
        Reject(() => first.GetRandomInt(2, 1), "拒绝逆序整数区间");
        Reject(() => first.GetRandomDouble(double.NaN, 1), "拒绝非有限随机范围");
        Reject(() => first.GetRandomDiff(-1), "拒绝负宽度");
        Check(first.GetRandomInt(0, 1000) == second.GetRandomInt(0, 1000), "零宽度与错误调用不消耗独立流");
    }

    /// <summary>验证公共几何的闭边界、圆角、零长度及非法输入。</summary>
    private void VerifyGeometry()
    {
        // 使用小整数坐标保证接触边界断言精确。
        var rectangle = new Rect2(0, 0, 10, 10);
        Check(VMath.ClosestPointOnSegment(new Vector2(3, 4), Vector2.Zero, new Vector2(10, 0)) == new Vector2(3, 0), "线段最近点");
        Check(VMath.ClosestPointOnSegment(Vector2.One, Vector2.Zero, Vector2.Zero) == Vector2.Zero, "零长度线段");
        Check(VMath.CircleIntersectsRect(new Vector2(15, 5), 5, rectangle), "圆与矩形边界接触");
        Check(!VMath.CircleIntersectsRect(new Vector2(15, 15), 5, rectangle), "圆角不使用矩形粗筛结果");
        Check(VMath.CapsuleIntersectsRect(new Vector2(-20, 5), new Vector2(30, 5), 0, rectangle), "中心线穿越矩形");
        Check(VMath.CapsuleIntersectsRect(new Vector2(-20, -3), new Vector2(30, -3), 3, rectangle), "带宽线段擦边");
        Check(!VMath.CapsuleIntersectsRect(new Vector2(13, 14), new Vector2(20, 14), 4.9, rectangle), "胶囊端帽精确排除角点");
        Check(VMath.CapsuleIntersectsRect(new Vector2(13, 14), new Vector2(20, 14), 5, rectangle), "胶囊圆端帽接触角点");
        Check(VMath.CapsuleIntersectsRect(Vector2.Zero, Vector2.Zero, 1, rectangle), "零长度胶囊");
        Reject(() => VMath.CircleIntersectsRect(Vector2.Zero, -1, rectangle), "拒绝负半径");
        Reject(() => VMath.CapsuleIntersectsRect(Vector2.Zero, new Vector2(float.NaN, 0), 1, rectangle), "拒绝非有限端点");
        Reject(() => VMath.CircleIntersectsRect(Vector2.Zero, 1, new Rect2(0, 0, -1, 2)), "拒绝负矩形尺寸");
    }

    /// <summary>验证矩形查询的阵营、稳定顺序、大子弹及激光阶段和路径窗口。</summary>
    private void VerifyQueries()
    {
        // 完整隔离战斗及其场景节点，测试结束统一释放。
        var battle = Create(out var world);
        // 原点附近与玩家分离，便于独立检验矩形的边界。
        var rectangle = new Rect2(100, 100, 20, 20);
        var large = Spawn(battle, new Vector2(130, 110), 10);
        var outside = Spawn(battle, new Vector2(140, 140));
        var friendly = Spawn(battle, new Vector2(110, 110), team: VBulletTeam.Player);
        var warning = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Length = 300, Width = 20, HitWidth = 12,
            WarningMs = 50, ExpandMs = 50, ActiveMs = 100, FadeMs = 100
        }, new Vector2(0, 94))!;
        battle.StepFixed(Vector2.Zero, false);
        // 跨多次查询复用的结果列表。
        var results = new List<VBullet>();
        GlobalEvent.CollectBulletsInRect(rectangle, results);
        Check(results.SequenceEqual(new VBullet[] { large, friendly, warning }), "按覆盖区域和登记顺序查询，包含远端预警激光");
        GlobalEvent.CollectBulletsInRect(rectangle, results, VBulletTeam.Enemy);
        Check(results.SequenceEqual(new VBullet[] { large, warning }), "阵营过滤且复用结果不累加");
        Check(!results.Contains(outside), "范围外普通弹排除");
        GlobalEvent.CollectBulletsInRect(new Rect2(125, 105, 20, 20), results, VBulletTeam.Enemy);
        Check(results.SequenceEqual(new[] { large }), "同尺寸平移感知框使用形状相交接口");
        GlobalEvent.CollectBulletsInRect(new Rect2(100, 100, 40, 40), results, VBulletTeam.Enemy);
        Check(results.SequenceEqual(new VBullet[] { large, outside, warning }), "改变矩形尺寸后查询缓存正确更新");
        GlobalEvent.CollectBulletsInRect(new Rect2(120, 110, 0, 0), results, VBulletTeam.Enemy);
        Check(results.SequenceEqual(new[] { large }), "退化矩形保留闭边界查询语义");
        Check(warning.VisualWidth == 2 && warning.IntersectsRect(rectangle), "预警按正式宽度查询");
        // 补齐到消退边界的固定步。
        for (int index = 0; index < 11; index++) battle.StepFixed(Vector2.Zero, false);
        Check(warning.Stage == VLaserStage.Fade && !warning.IntersectsRect(rectangle), "消退不再参与查询");
        battle.Bullets.Clear();
        Check(!warning.TryGetNearestHazard(Vector2.Zero, out _, out _), "清场后旧激光无危险几何");
        // 移动光束只查询当前窗口，既不查询未来路径也不查询已经离开的尾部。
        var path = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Mode = "Path", Length = 30, TravelSpeed = 120, EndMs = 5000, Width = 10, HitWidth = 6
        }, Vector2.Zero, path: new[] { Vector2.Zero, new Vector2(120, 0), new Vector2(120, 120) })!;
        // 推进半秒，使移动光束形成明确的头尾窗口。
        for (int index = 0; index < 30; index++) battle.StepFixed(Vector2.Zero, false);
        Check(path.IntersectsRect(new Rect2(35, -1, 10, 2)), "移动光束当前中段入选");
        Check(!path.IntersectsRect(new Rect2(5, -1, 10, 2)), "移动光束尾部经过后不再入选");
        Check(!path.IntersectsRect(new Rect2(110, 50, 20, 20)), "移动光束未来路径不入选");
        Check(path.TryGetNearestHazard(new Vector2(40, 10), out var nearest, out var tangent)
            && nearest.IsEqualApprox(new Vector2(40, 0)) && tangent == Vector2.Right, "当前窗口最近点及切向量");
        world.Free();
        // 无战斗时沿用GlobalEvent明确报错的约定。
        try { GlobalEvent.CollectBulletsInRect(rectangle, results); throw new Exception("无战斗查询未拒绝"); }
        catch (InvalidOperationException) { Check(true, "无有效战斗不查询旧容器"); }
    }

    /// <summary>验证感知与威胁分离、小幅避让、路径安全、边界和预警逃离。</summary>
    private void VerifyStrategy()
    {
        // 安静战斗与出生位置；此位置已在驻留矩形内。
        var battle = Create(out var world);
        var ai = battle.AI!;
        var initial = ai.Position;
        // 大量安全静止弹即使分布不均也不会驱动角色持续游走。
        Vector2[] occupied = { Vector2.Right, new(1, 1), Vector2.Down, new(-1, 1), Vector2.Left, new(-1, -1), new(1, -1) };
        foreach (var direction in occupied) Spawn(battle, initial + direction.Normalized() * 60);
        for (int index = 0; index < 90; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.Position == initial && ai.LastIntent == default, "附近安全静止弹不触发移动");
        battle.Bullets.Clear();
        Spawn(battle, initial + new Vector2(-30, -30), speed: 300);
        Spawn(battle, initial + new Vector2(20, 0), speed: 300);
        for (int index = 0; index < 30; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.Position == initial, "擦身经过及远离弹幕不触发移动");

        // 已处于3像素余量内但实际安全且不再接近，不应被安全余量强制驱赶。
        battle.Bullets.Clear();
        Spawn(battle, initial + new Vector2(9, 0), speed: 300);
        Spawn(battle, initial - new Vector2(0, 9));
        for (int index = 0; index < 30; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.Position == initial && ai.HitCount == 0, "余量内的安全静止弹和远离弹仍保持停留");

        // 一颗来弹在120毫秒内会击中原地，侧移后及时停止。
        battle.Bullets.Clear();
        Spawn(battle, initial - new Vector2(35, 0), speed: 300);
        battle.StepFixed(Vector2.Zero, false);
        Check(Math.Abs(ai.LastIntent.Movement.Y) == 1 && ai.LastIntent.Movement.X == 0, "即将命中时沿来弹侧面避让");
        for (int index = 0; index < 40; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.HitCount == 0 && ai.Position.DistanceTo(initial) >= 8 && ai.Position.DistanceTo(initial) <= 20
            && ai.LastIntent.Movement == Vector2.Zero, "单弹只小幅移动，安全后立即停留");

        // 两侧不只比较终点：上方安全静止弹会使向上侧移变危险。
        battle.Bullets.Clear();
        ai.Position = initial;
        Spawn(battle, initial - new Vector2(35, 0), speed: 300);
        Spawn(battle, initial - new Vector2(0, 20));
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.LastIntent.Movement == Vector2.Down, "避让不会主动撞上另一颗静止弹");

        // 上侧预警距中心20像素，尚未触发19像素激光避让，但可以封住普通弹侧移。
        battle.Bullets.Clear();
        ai.Position = initial;
        Spawn(battle, initial - new Vector2(35, 0), speed: 300);
        battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Length = 200, Width = 20, HitWidth = 12,
            WarningMs = 500, ExpandMs = 100, ActiveMs = 1000, FadeMs = 100
        }, initial - new Vector2(100, 20));
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.LastIntent.Movement == Vector2.Down, "避让普通弹时检查其他预警光束");

        // 位于上边界时只能选向场内的候选。
        battle.Bullets.Clear();
        ai.Position = BattleConfig.ArenaCenter + new Vector2(0, -395);
        Spawn(battle, ai.Position - new Vector2(35, 0), speed: 300);
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.LastIntent.Movement == Vector2.Down && BattleConfig.GameRegion.Contains(ai.Position, BattleConfig.PlayerRadius),
            "场地形状接口排除越界侧移");

        // 已经重叠的静止弹仍尝试退出，不会因零速度除零或失去方向。
        battle.Bullets.Clear();
        ai.Position = initial;
        Spawn(battle, initial);
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.LastIntent.Movement.LengthSquared() == 1, "零速同心弹可以选择退出方向");
        battle.Bullets.Clear();
        ai.Position = initial;
        // 安全一帧复位上一段普通弹方向记忆。
        battle.StepFixed(Vector2.Zero, false);
        // 水平激光中心线通过AI，预警期间即应沿法线离开。
        long hits = ai.HitCount;
        var laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Length = 200, Width = 20, HitWidth = 12,
            WarningMs = 500, ExpandMs = 100, ActiveMs = 1000, FadeMs = 100
        }, initial - new Vector2(100, 0))!;
        battle.StepFixed(Vector2.Zero, false);
        Check(Math.Abs(ai.LastIntent.Movement.Y) == 1 && ai.LastIntent.Movement.X == 0 && ai.HitCount == hits,
            "中心线预警法线逃离");
        for (int index = 0; index < 10; index++) battle.StepFixed(Vector2.Zero, false);
        Check(Math.Abs(ai.Position.Y - initial.Y) >= 23 && ai.LastIntent.Movement == Vector2.Zero, "离开阈值增加12像素后停留");
        for (int index = 0; index < 40; index++) battle.StepFixed(Vector2.Zero, false);
        Check(laser.Stage == VLaserStage.Active && ai.HitCount == hits, "预警结束前完成基础逃离");
        battle.Bullets.Clear();
        ai.Position = initial;
        battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Length = 200, Width = 20, HitWidth = 12,
            WarningMs = 0, ExpandMs = 0, ActiveMs = 1000, FadeMs = 0
        }, initial - new Vector2(100, 0));
        battle.StepFixed(Vector2.Zero, false);
        Check(Math.Abs(ai.LastIntent.Movement.Y) == 1, "无预警激光按当前几何避让");
        world.Free();
    }

    /// <summary>验证宽矩形驻留、一秒安静窗口、普通弹和预警阻止不安全回位。</summary>
    private void VerifyRestPosition()
    {
        // 无弹战斗，出生位置在中下方宽容区域内。
        var battle = Create(out var world);
        var ai = battle.AI!;
        var spawn = ai.Position;
        for (int index = 0; index < 120; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.Position == spawn, "驻留区域内长期停留，不主动贴近中心");
        ai.Position = new Vector2(790, 690);
        for (int index = 0; index < 90; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.Position == new Vector2(790, 690), "中下方矩形角部也可以原地驻留");

        // 重开清除安静计数，前59步不会开始回位。
        battle.Restart();
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        ai = battle.AI!;
        ai.Position = new Vector2(640, 300);
        Spawn(battle, new Vector2(690, 300));
        for (int index = 0; index < 59; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.Position == new Vector2(640, 300), "短暂空档不触发回位");
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.LastIntent.Movement == Vector2.Down * 0.5f && ai.Position == new Vector2(640, 302),
            "附近有安全弹也能累计一秒安静并半速回位");
        // 上方来弹实际形成威胁时，打断向下回位，改为水平侧移。
        var incoming = Spawn(battle, ai.Position - new Vector2(0, 25), speed: 300);
        incoming.SetDirection(Math.PI * 0.5);
        battle.StepFixed(Vector2.Zero, false);
        Check(Math.Abs(ai.LastIntent.Movement.X) == 1 && Math.Abs(ai.LastIntent.Movement.Y) < 0.00001f, "实际威胁立即打断回位");
        battle.Bullets.Clear();
        ai.Position = new Vector2(640, 300);
        for (int index = 0; index < 59; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.Position == new Vector2(640, 300), "威胁结束后重新等待一秒");
        for (int index = 0; index < 150; index++)
        {
            battle.StepFixed(Vector2.Zero, false);
            Check(BattleConfig.GameRegion.Contains(ai.Position, BattleConfig.PlayerRadius), "回位始终遵守活动区域");
        }
        Check(ai.Position.IsEqualApprox(new Vector2(640, 520)) && ai.LastIntent.Movement == Vector2.Zero,
            "到达驻留矩形最近边缘即停止");
        world.Free();

        // 回位前方安全静止弹不会触发躲避，也不会被回位主动撞上。
        battle = Create(out world);
        ai = battle.AI!;
        ai.Position = new Vector2(640, 300);
        Spawn(battle, new Vector2(640, 320));
        for (int index = 0; index < 120; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.Position == new Vector2(640, 300) && ai.HitCount == 0, "静止弹挡住回位时宁可停留");
        battle.Bullets.Clear();
        // 激光尚未形成原地威胁，但穿过它回位仍须被禁止。
        var laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Length = 200, Width = 20, HitWidth = 12,
            WarningMs = 2000, ExpandMs = 100, ActiveMs = 1000, FadeMs = 100
        }, new Vector2(540, 320))!;
        for (int index = 0; index < 70; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.Position == new Vector2(640, 300) && ai.HitCount == 0 && laser.Stage == VLaserStage.Warning,
            "回位不穿过预警光束");
        battle.Bullets.Clear();
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.LastIntent.Movement == Vector2.Down * 0.5f, "障碍消失后恢复缓慢回位");
        world.Free();

        // 安全停留不会抽取随机数，方向平局仍只使用独立流。
        var strategy = new BasicAIStrategy();
        var random = VMath.CreateRandomStream(42);
        var expected = VMath.CreateRandomStream(42);
        var state = new AICharState
        {
            Position = spawn, MoveSpeed = BattleConfig.MoveSpeed, Radius = BattleConfig.PlayerRadius, StepSeconds = 1.0 / 60
        };
        for (int index = 0; index < 120; index++) strategy.Decide(in state, Array.Empty<VBullet>(), random);
        Check(random.GetRandomInt(0, int.MaxValue) == expected.GetRandomInt(0, int.MaxValue), "安全停留不消耗独立随机流");
    }
    /// <summary>验证普通弹与激光独立受击、负血、策略替换、重开及结束清理。</summary>
    private void VerifyDamageAndLifecycle()
    {
        // 测试策略固定停留，以稳定验证碰撞和无敌时间。
        var config = new AICharConfig { Enabled = true, StrategyFactory = () => new FixedStrategy(default) };
        var battle = Create(out var world, config);
        // 当前AI引用；重开后重新获取，避免测试旧角色。
        var ai = battle.AI!;
        Check(ai.Health.Hp == BattleConfig.PlayerHp && ai.GetNode<Label>("AILabel").Text == "AI", "角色初始生命及AI标识");
        // 持续重叠的高伤害弹，用于区分受击次数和伤害数值。
        var bullet = Spawn(battle, ai.Position, damage: 3);
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.HitCount == 1 && ai.Health.Hp == 0 && bullet.IsAlive, "AI有效受击计一次且不消耗敌弹");
        Check(battle.Player.Health.Hp == 3, "AI受击不伤害玩家");
        // 首次受击后仍处于一秒保护内的固定步。
        for (int index = 0; index < 59; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.HitCount == 1, "保护期间不逐帧计数");
        // 跨过保护结束派发边界，允许下一次有效伤害。
        for (int index = 0; index < 2; index++) battle.StepFixed(Vector2.Zero, false);
        Check(ai.HitCount == 2 && ai.Health.Hp == -3 && ai.Timeline is not null, "无敌结束后再次受伤且负血继续");
        battle.Restart();
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        Check(!ReferenceEquals(ai, battle.AI) && ai.IsQueuedForDeletion() && battle.AI!.HitCount == 0, "重开重建AI并清零");
        ai = battle.AI!;
        ai.Position = battle.Player.Position;
        bullet = Spawn(battle, ai.Position);
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.HitCount == 1 && battle.Player.Health.Hp == 2 && !bullet.IsAlive, "同一步双方命中各自生效，原玩家释放规则不变");
        battle.Restart();
        battle.Player.Attack.Stop();
        ai = battle.AI!;
        // 激光伤害不会释放光束，路径沿用既有连续碰撞。
        var laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Length = 100, WarningMs = 0, ExpandMs = 0, ActiveMs = 2000, FadeMs = 0
        }, ai.Position - new Vector2(50, 0))!;
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.HitCount == 1 && laser.IsAlive, "激光独立受击且不释放");
        battle.StepFixed(Vector2.Zero, false, nextPhasePressed: true);
        Check(ai.HitCount == 1 && battle.Boss.PhaseIndex == 1, "切阶段保留受击次数");
        // 阶段拥有独立血池，逐阶段结束，不让超额伤害穿透。
        for (int phase = battle.Boss.PhaseIndex; phase < battle.Boss.PhaseCount; phase++)
            battle.Boss.TakeDamage(battle.Boss.PhaseHp);
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.State == BattleState.Victory && battle.Bullets.ActiveCount == 0 && battle.Timers.TimelineActionCount == 0, "胜利清理AI时间线和弹幕");
        // 胜利冻结时的位置，后续输入不得改变它。
        var stopped = ai.Position;
        battle.StepFixed(Vector2.One, false);
        Check(ai.Position == stopped, "胜利冻结AI");
        world.Free();
        // 高速穿越使用相对轨迹，不能只在步末判定重叠。
        battle = Create(out world, config);
        ai = battle.AI!;
        bullet = Spawn(battle, ai.Position - new Vector2(50, 0), speed: 6000);
        battle.StepFixed(Vector2.Zero, false);
        Check(ai.HitCount == 1 && bullet.IsAlive, "AI高速弹连续碰撞不漏判");
        world.Free();
        // 可替换策略可以请求攻击和闪避，基础策略没有开启这些能力。
        battle = Create(out world, new AICharConfig
        {
            Enabled = true,
            StrategyFactory = () => new FixedStrategy(new AIIntent { Movement = Vector2.Up, AttackEnabled = true, DodgePressed = true })
        });
        // 推进200毫秒，覆盖攻击接口的首发时刻。
        for (int index = 0; index < 12; index++) battle.StepFixed(Vector2.Zero, false);
        Check(battle.AI!.Dodge.Cooldown > 0 && battle.Bullets.ActiveBullets.Any(item => item.Team == VBulletTeam.Player), "策略接口保留闪避和攻击能力");
        battle.StopBattle();
        Check(battle.Timers.TimelineActionCount == 0 && battle.Bullets.ActiveCount == 0 && battle.AI.LastIntent == default, "离场停止AI并清理");
        world.Free();
    }

    /// <summary>验证相同初态重现与启用AI前后原战局一致。</summary>
    private void VerifyReplayAndIsolation()
    {
        // 原战局不包含AI字段；完整AI轨迹另外比较。
        var without = Capture(false, false);
        var first = Capture(true, false);
        var second = Capture(true, true);
        Check(without.World == first.World, "启用AI不改变原玩家、Boss、弹幕和默认随机结果");
        Check(first.World == second.World && first.AI == second.AI, "相同AI种子与输入重现，显示刷新不干扰随机");
        // 在同一管理器重开，确认策略状态和独立随机状态一起归零。
        // 完整隔离战斗及其场景节点，测试结束统一释放。
        var battle = Create(out var world);
        var initial = CaptureManual(battle);
        battle.Restart();
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        Check(initial == CaptureManual(battle), "同一战斗重开重现AI轨迹与受击计数");
        world.Free();
    }

    /// <summary>采集固定输入下的原战局与AI轨迹。</summary>
    /// <param name="enabled">是否启用AI。</param>
    /// <param name="redraw">是否额外刷新AI显示。</param>
    /// <returns>原战局和AI逐步记录。</returns>
    private (string World, string AI) Capture(bool enabled, bool redraw)
    {
        // 保留真实Boss和玩家攻击的完整战斗。
        var battle = Create(out var world, new AICharConfig { Enabled = enabled }, quiet: false);
        // 分别记录原战局和AI状态，避免隔离验证混入新增角色字段。
        var original = new StringBuilder();
        var aiTrace = new StringBuilder();
        // 固定七秒输入样本，每步都比较原战局和AI轨迹。
        for (int index = 0; index < 420; index++)
        {
            // 固定玩家输入，同时覆盖手动阶段切换和不同弹幕树。
            var direction = index % 120 < 60 ? Vector2.Left : Vector2.Right;
            battle.StepFixed(direction, index % 90 == 0, nextPhasePressed: index is 180 or 300);
            original.Append(Exact(battle.Player.Position)).Append(':').Append(battle.Player.Health.Hp)
                .Append(':').Append(Exact(battle.Boss.Position)).Append(':').Append(battle.Boss.Hp).Append('|');
            // 当前活动弹按登记顺序记录，检测数量及运动状态变化。
            foreach (var bullet in battle.Bullets.ActiveBullets)
                original.Append(Exact(bullet.WorldPosition)).Append(':').Append(Exact(bullet.Velocity)).Append(':')
                    .Append(bullet.Age.ToString("R")).Append(':').Append(bullet.Team).Append(';');
            original.AppendLine();
            if (battle.AI is not null)
            {
                aiTrace.Append(Exact(battle.AI.Position)).Append(':').Append(battle.AI.HitCount).Append(':').Append(Exact(battle.AI.LastIntent.Movement)).AppendLine();
                if (redraw) { battle.AI.FinishStep(); battle.AI.FinishStep(); }
            }
        }
        original.Append(VMath.getRandomInt(int.MinValue, int.MaxValue));
        world.Free();
        return (original.ToString(), aiTrace.ToString());
    }

    /// <summary>使用单颗正向来弹制造两侧随机平局，记录重开前后轨迹。</summary>
    /// <param name="battle">已重置完整状态的安静战斗。</param>
    /// <returns>逐步AI轨迹与命中计数。</returns>
    private static string CaptureManual(BattleManager battle)
    {
        Spawn(battle, battle.AI!.Position - new Vector2(35, 0), speed: 300);

        // 保存逐步精确位置及受击次数。
        var result = new StringBuilder();
        // 当前重复验证步，覆盖随机平局及持续移动。
        for (int index = 0; index < 100; index++)
        {
            battle.StepFixed(Vector2.Zero, false);
            result.Append(Exact(battle.AI.Position)).Append(':').Append(battle.AI.HitCount).AppendLine();
        }
        return result.ToString();
    }

    /// <summary>保留单精度往返表示，避免重现测试掩盖低位差异。</summary>
    /// <param name="value">世界位置或速度向量。</param>
    /// <returns>两个分量的精确文本。</returns>
    private static string Exact(Vector2 value) => $"{value.X:R},{value.Y:R}";

    /// <summary>测量满额普通弹下的查询和策略开销，确认热路径不分配托管集合。</summary>
    private void VerifyDecisionCost()
    {
        // 满额静止弹全落在感知范围，避免用空场景低估决策成本。
        // 完整隔离战斗及其场景节点，测试结束统一释放。
        var battle = Create(out var world);
        // 按固定网格生成至2048容量上限。
        for (int index = 0; index < BattleConfig.MaxBullets; index++)
            Spawn(battle, battle.AI!.Position + new Vector2(index % 80 - 40, index / 80 - 13));
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        // 预热运行库和Godot绑定后测量1000次完整感知与决策。
        for (int index = 0; index < 64; index++) battle.AI!.Decide(1.0 / 60);
        // 记录当前线程分配量与高精度起始时间。
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        // 不推进战斗，重复评估相同满额感知状态。
        for (int index = 0; index < 1000; index++) battle.AI!.Decide(1.0 / 60);
        // 换算总耗时和托管分配增量，时间仅报告不设机器相关断言。
        double milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "2048颗附近普通弹的感知与决策热路径无托管分配");
        GD.Print($"AI cost: 2048 ordinary bullets, {milliseconds / 1000:0.0000} ms/decision, {allocated} managed bytes / 1000 decisions");
        world.Free();
    }

    /// <summary>等待延迟场景切换及布局完成。</summary>
    /// <returns>两个渲染帧后的任务。</returns>
    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>验证真实鼠标复选框、空格确认、HUD与返回记忆。</summary>
    /// <returns>界面验证完成的任务。</returns>
    private async Task VerifyUI()
    {
        // 完整游戏管理器负责真实场景切换与选择记忆。
        var game = new GameManager();
        AddChild(game);
        await Settle();
        // 当前选择界面，确认关闭状态和后续鼠标命中位置。
        var select = (BossSelectStage)game.CurrentStage!;
        Check(!select.UI.AIOption.ButtonPressed && !game.AIEnabled, "AI选项默认关闭");
        // 经视口发送鼠标事件，不能只修改控件属性代替交互验证。
        var position = select.UI.AIOption.GetGlobalRect().GetCenter();
        using var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position };
        using var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = position, GlobalPosition = position };
        using var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position, GlobalPosition = position };
        GetViewport().PushInput(motion, true);
        GetViewport().PushInput(press, true);
        GetViewport().PushInput(release, true);
        Check(game.AIEnabled && select.UI.AIOption.ButtonPressed && !game.IsTransitioning, "鼠标只切换AI选项");
        await CaptureUI("ai-select-preview");
        // 点击复选框后的键盘确认事件，用于检查焦点未被截获。
        using var confirm = new InputEventKey { Keycode = Key.Space, Pressed = true };
        GetViewport().PushInput(confirm, true);
        Check(game.IsTransitioning, "点击复选框后空格仍能进入关卡");
        await Settle();
        // 新战场及其管理器，冻结物理更新后检查HUD与重开。
        var stage = (BattleStage)game.CurrentStage!;
        var battle = stage.View.Battle;
        battle.SetPhysicsProcess(false);
        Check(battle.AI is not null && battle.AI.GetNode<Label>("AILabel").Text == "AI", "场景配置创建AI及头顶文字");
        stage.View._Process(0);
        Check(stage.View.GetChildren().OfType<CanvasLayer>().Single().GetChildren().OfType<Label>()
            .Any(label => label.Text.Contains("AI 被击中次数：")), "左侧HUD显示AI命中计数");
        await CaptureUI("ai-battle-preview");
        battle.Restart();
        battle.SetPhysicsProcess(false);
        Check(battle.AI is not null && battle.AI.HitCount == 0, "UI创建的战斗重开保留AI选项");
        game.RequestStage(GameManager.SelectStageId);
        await Settle();
        select = (BossSelectStage)game.CurrentStage!;
        Check(select.UI.AIOption.ButtonPressed && game.AIEnabled, "返回选择页保留AI选项");
        select.UI.AIOption.ButtonPressed = false;
        select.UI.Confirm();
        await Settle();
        battle = ((BattleStage)game.CurrentStage!).View.Battle;
        battle.SetPhysicsProcess(false);
        Check(battle.AI is null, "取消勾选后不创建AI");
        game.Free();
    }

    /// <summary>仅在渲染验证模式保存实际视口，普通无头测试不读取渲染纹理。</summary>
    /// <param name="name">输出文件名，不含扩展名，保存在忽略的.godot目录。</param>
    /// <returns>当前帧绘制并保存后的任务。</returns>
    private async Task CaptureUI(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture-ai")) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        // 图像只作为本地视觉验证产物，不加入项目资源。
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath($"res://.godot/{name}.png")) == Error.Ok, "保存实际界面渲染");
    }

    /// <summary>测试用固定输入策略，用于隔离角色能力与策略算法。</summary>
    private sealed class FixedStrategy : IAIStrategy
    {
        // 每步返回的固定操作。
        private readonly AIIntent _intent;
        /// <summary>保存测试操作。</summary>
        /// <param name="intent">固定移动、闪避与攻击意图。</param>
        public FixedStrategy(AIIntent intent) => _intent = intent;
        /// <summary>返回预设输入，不读取弹幕或随机数。</summary>
        /// <param name="state">当前角色状态。</param>
        /// <param name="nearby">当前附近弹幕。</param>
        /// <param name="random">未使用的独立随机流。</param>
        /// <returns>固定操作。</returns>
        public AIIntent Decide(in AICharState state, IReadOnlyList<VBullet> nearby, VRandomStream random) => _intent;
    }
}
