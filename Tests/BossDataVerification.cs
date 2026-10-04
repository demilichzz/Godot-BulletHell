using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Boss数据化、独立血池、时间条件、移动与完整初态重现的定向验证。</summary>
public partial class BossDataVerification : Node
{
    // 通过断言数及当前独立战场，测试结束时释放。
    private int _checks;
    private Node2D? _world;
    /// <summary>等待场景就绪后运行定向验证。</summary>
    public override void _Ready() => Callable.From(Run).CallDeferred();
    /// <summary>记录断言或立即报告失败。</summary>
    /// <param name="condition">应当成立的条件。</param>
    /// <param name="message">具体行为说明。</param>
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    /// <summary>创建完整初态的隔离战斗并关闭引擎自动步进。</summary>
    /// <param name="data">Boss配置；null测试正式默认入口。</param>
    /// <returns>仅由测试推进的战斗。</returns>
    private BattleManager Start(BossData? data = null)
    {
        if (_world is not null) _world.Free();
        _world = new Node2D();
        AddChild(_world);
        var battle = new BattleManager();
        _world.AddChild(battle);
        battle.Initialize(_world, data);
        battle.SetPhysicsProcess(false);
        battle.Player.Attack.Stop();
        return battle;
    }
    /// <summary>推进给定数量的60Hz逻辑步。</summary>
    /// <param name="battle">测试战斗。</param>
    /// <param name="frames">非负固定步数。</param>
    private static void Step(BattleManager battle, int frames)
    {
        // 固定输入，不让玩家射击改变Boss血池。
        for (int frame = 0; frame < frames; frame++) battle.StepFixed(Vector2.Zero, false);
    }
    /// <summary>生成无发射器的独立双阶段配置，保留真实显示资源。</summary>
    /// <returns>可编辑JSON根对象。</returns>
    private static JsonObject Fixture()
    {
        // 10与30点血池便于独立验证伤害、超时扣血和总血量。
        var root = JsonNode.Parse(JsonData.ReadFile("res://Data/Bosses/B01.json"))!.DeepClone().AsObject();
        root["Core"]!["MaxHp"] = 40;
        root["Phases"] = JsonNode.Parse("""
        [{"Name":"甲","Hp":10,"DurationMs":100,"EndCondition":"Time","Emitters":[],
          "Movement":{"Type":"Center","Speed":60,"Target":{"X":640,"Y":300}}},
         {"Name":"乙","Hp":30,"DurationMs":100,"EndCondition":"Time","Emitters":[],
          "Movement":{"Type":"Center"}}]
        """);
        return root;
    }
    /// <summary>检查非法JSON被加载器拒绝。</summary>
    /// <param name="root">独立错误配置。</param>
    /// <param name="message">应当拒绝的原因。</param>
    private void Reject(JsonObject root, string message)
    {
        try { BossData.FromJson(root.ToJsonString()); }
        catch (Exception error) when (error is JsonException or ArgumentException or System.IO.IOException)
        { Check(true, message); return; }
        throw new Exception("未拒绝：" + message);
    }
    /// <summary>运行明确范围的Boss验证；不调用整个项目回归集。</summary>
    private void Run()
    {
        try
        {
            VerifyDefinitions();
            VerifyHealthAndTime();
            VerifyMovement();
            VerifyReplay();
            if (_world is not null) { _world.Free(); _world = null; }
            GD.Print($"PASS: {_checks} targeted Boss JSON assertions");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            if (_world is not null) { _world.Free(); _world = null; }
            GetTree().Quit(1);
        }
    }
    /// <summary>校验23个正式Boss及所有阶段可运行，严格读取不会消耗随机。</summary>
    private void VerifyDefinitions()
    {
        // 固定随机序列的下一值应不受加载全部配置影响。
        VMath.setRandomSeed(72);
        double expected = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(72);
        var catalog = BossCatalog.Load();
        catalog.Validate();
        Check(catalog.Entries.Count == 23 && VMath.getRandomDouble(0, 1) == expected, "23个Boss加载不消耗业务随机");
        foreach (var data in catalog.Entries)
        {
            Check(data.MaxHp == data.Phases.Sum(phase => phase.Hp) && data.PhaseCount == (data.Id == "Boss_02" ? 1 : 3), "正式血池总和与阶段队列");
            var battle = Start(data);
            for (int index = 0; index < data.PhaseCount; index++)
            {
                Check(battle.Boss.CurrentPhase is BossPhase && battle.Boss.PhaseIndex == index, "正式入口使用数据化阶段");
                Step(battle, 65);
                Check(battle.Boss.CurrentPhase!.Emitters.Count == data.Phases[index].Emitters.Count, "全部Emitter引用按阶段绑定");
                if (index + 1 < data.PhaseCount) Check(battle.Boss.TrySwitchAdjacentPhase(1), "阶段按队列可切换");
            }
        }
        // 未知、重复、空、零时限及错误引用逐项拒绝。
        var invalid = Fixture(); invalid["Version"] = 1; Reject(invalid, "未知Version");
        invalid = Fixture(); invalid["Core"]!["MaxHp"] = 41; Reject(invalid, "总血量不等于阶段和");
        invalid = Fixture(); invalid["Phases"] = new JsonArray(); Reject(invalid, "空阶段队列");
        invalid = Fixture(); invalid["Core"]!["SpawnPosition"]!.AsObject().Remove("Y"); Reject(invalid, "缺少出生纵坐标");
        invalid = Fixture(); invalid["Phases"]![0]!["Movement"]!["Target"]!.AsObject().Remove("X"); Reject(invalid, "缺少移动横坐标");
        invalid = Fixture(); invalid["Phases"]![0]!["DurationMs"] = null; Reject(invalid, "时间条件缺少时限");
        invalid = Fixture(); invalid["Phases"]![0]!["DurationMs"] = 0; Reject(invalid, "零时限");
        invalid = Fixture(); invalid["Phases"]![0]!["DurationMs"] = 0.5; Reject(invalid, "非整数毫秒");
        invalid = Fixture(); invalid["Phases"]![0]!["Emitters"] = new JsonArray("res://Data/Emitters/missing.json"); Reject(invalid, "缺失Emitter");
        invalid = Fixture(); invalid["Phases"]![0]!["Movement"]!["IntervalMs"] = 1; Reject(invalid, "模式无关字段");
        try { BossData.FromJson(Fixture().ToJsonString().Replace("\"Hp\":10", "\"Hp\":10,\"Hp\":10")); throw new Exception("重复字段未拒绝"); }
        catch (JsonException) { Check(true, "重复字段"); }
    }
    /// <summary>覆盖独立血池、三种条件、末阶段胜利、重开和手动切换。</summary>
    private void VerifyHealthAndTime()
    {
        var battle = Start();
        Check(battle.Boss.Hp == 300 && battle.Boss.PhaseHp == 100, "默认入口独立100血池");
        battle.Boss.TakeDamage(int.MaxValue);
        Check(battle.Boss.Hp == 200 && battle.Boss.PhaseHp == 100 && battle.Boss.PhaseIndex == 1, "超额伤害不带入下一血池");
        battle.Boss.TakeDamage(10);
        Check(battle.Boss.Hp == 190 && battle.Boss.PhaseHp == 90, "当前池与总血量同步");
        Check(battle.Boss.TrySwitchAdjacentPhase(-1) && battle.Boss.Hp == 300 && battle.Boss.PhaseHp == 100, "手动切换恢复对应池");
        battle = Start(BossData.FromJson(Fixture().ToJsonString()));
        battle.Boss.TakeDamage(50);
        Step(battle, 5);
        Check(battle.Boss.PhaseHp == 0 && battle.Boss.PhaseIndex == 0 && battle.Boss.Position.Y == 255, "仅时间零血仍移动");
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.Boss.Hp == 30 && battle.Boss.PhaseHp == 30 && battle.Boss.PhaseIndex == 1, "100ms边界切换且新阶段零龄");
        battle.Boss.TakeDamage(99);
        Step(battle, 5);
        Check(battle.Boss.Hp == 0 && !battle.Boss.IsDefeated && battle.State == BattleState.Running, "末阶段仅时间零血不提前胜利");
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.State == BattleState.Victory && battle.Boss.IsDefeated, "末阶段时间边界胜利");
        battle.Restart(); battle.Player.Attack.Stop();
        Step(battle, 6);
        Check(battle.Boss.Hp == 30 && battle.Boss.PhaseHp == 30, "超时扣除未受伤血池");
        Step(battle, 6);
        Check(battle.Boss.Hp == 0 && battle.State == BattleState.Victory && battle.Timers.TimelineActionCount == 0, "末阶段超时清空血量与时间线");
        // 两条独立用例分别由血量和时间结束HealthOrTime阶段。
        foreach (bool damage in new[] { false, true })
        {
            var root = Fixture();
            root["Phases"]![0]!["EndCondition"] = "HealthOrTime";
            root["Phases"]![1]!["EndCondition"] = "Health";
            battle = Start(BossData.FromJson(root.ToJsonString()));
            if (damage) battle.Boss.TakeDamage(11); else Step(battle, 6);
            Check(battle.Boss.PhaseIndex == 1 && battle.Boss.PhaseHp == 30, "任一条件切换且血池独立");
            Step(battle, 12);
            Check(battle.Boss.PhaseIndex == 1 && !battle.Boss.IsDefeated, "仅血量忽略时限");
            battle.Boss.TakeDamage(30);
            battle.StepFixed(Vector2.Zero, false);
            Check(battle.State == BattleState.Victory, "末阶段血量胜利");
        }
    }
    /// <summary>覆盖中心停止、随机范围、固定目标以及VPath直线和曲线。</summary>
    private void VerifyMovement()
    {
        // 单阶段固定中心。
        var root = Fixture();
        root["Phases"]![0]!["EndCondition"] = "Health";
        var battle = Start(BossData.FromJson(root.ToJsonString()));
        Step(battle, 120);
        Check(battle.Boss.Position == new Vector2(640, 300) && !battle.Boss.CurrentPhase!.IsMoving, "到中心后停止");
        root["Phases"]![0]!["Movement"] = JsonNode.Parse("""
        {"Type":"RandomRect","Speed":100,"Min":{"X":500,"Y":200},"Max":{"X":700,"Y":300},"StartMs":100,"IntervalMs":100}
        """);
        battle = Start(BossData.FromJson(root.ToJsonString()));
        Step(battle, 60);
        Check(new Rect2(500, 200, 201, 101).HasPoint(battle.Boss.CurrentPhase!.MoveTarget), "随机目标在配置矩形内");
        battle = Start(BossCatalog.Load().Get("Boss_01"));
        Step(battle, 300);
        Check(Math.Abs(battle.Boss.CurrentPhase!.MoveTarget.DistanceTo(new Vector2(640, 250)) - 200) < 0.001, "B01原圆周随机行为");
        battle = Start(BossCatalog.Load().Get("Boss_03"));
        Step(battle, 360);
        Check(battle.Boss.Position == new Vector2(500, 230), "B03波次间固定目标换位");
        // VPath直线与贝塞尔各自复用原格式，速度为每秒60像素。
        foreach (string queue in new[]
        {
            """[{"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":60}]}]""",
            """[{"PathMode":"Function","AxisMode":"Relative","X":"L*t","Y":"20*sin(PI*t)","EndMoveQueue":[{"Type":"XYMove","X":60}]}]""",
            """[{"PathMode":"Bezier","EndMoveQueue":[{"Type":"XYMove","X":60}],"ControlPoints":[{"X":30,"Y":30}]}]"""
        })
        {
            root["Phases"]![0]!["Movement"] = JsonNode.Parse($$"""{"Type":"Path","Speed":60,"PathQueue":{{queue}}}""");
            battle = Start(BossData.FromJson(root.ToJsonString()));
            Step(battle, 30);
            Check(battle.Boss.Position.X > 640 && battle.Boss.Position.X < 700, "VPath中途按速度移动");
            Step(battle, 150);
            Check(battle.Boss.Position.DistanceTo(new Vector2(700, 250)) < 0.001 && !battle.Boss.CurrentPhase!.IsMoving, "VPath终点停止");
        }
        // Boss路径在入场时冻结目标；之后玩家移动不能重定向已经采样的路段。
        root["Phases"]![0]!["Movement"] = JsonNode.Parse("""{"Type":"Path","Speed":60,"PointCount":2,"PathQueue":[{"Type":"AimPlayer"}]}""");
        battle = Start(BossData.FromJson(root.ToJsonString()));
        battle.Player.Position = new Vector2(780, 550);
        Step(battle, 30);
        Check(battle.Boss.Position.DistanceTo(new Vector2(640, 280)) < 0.001
            && battle.Boss.CurrentPhase!.MoveTarget == BattleConfig.PlayerSpawn, "Boss瞄准路径使用阶段入场时的玩家快照");
        Check(battle.Boss.TrySwitchAdjacentPhase(1) && battle.Boss.TrySwitchAdjacentPhase(-1), "回到路径阶段重新入场");
        Check(battle.Boss.CurrentPhase!.MoveTarget == new Vector2(780, 550), "再次进入Boss路径阶段读取新的玩家位置");
    }
    /// <summary>重开后以同一完整初态和固定输入比较运动、阶段与弹幕快照。</summary>
    private void VerifyReplay()
    {
        var battle = Start();
        Step(battle, 420);
        Vector2 position = battle.Boss.Position;
        var bullets = battle.Bullets.ActiveBullets.Select(bullet => (bullet.WorldPosition, bullet.Angle, bullet.Speed)).ToArray();
        battle.Restart(); battle.Player.Attack.Stop();
        Step(battle, 420);
        Check(position == battle.Boss.Position
            && bullets.SequenceEqual(battle.Bullets.ActiveBullets.Select(bullet => (bullet.WorldPosition, bullet.Angle, bullet.Speed))), "重开完整初态重现Boss移动和弹幕");
    }
}
