using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

/// <summary>验证共用节点模型、树引用、数据时间规则和发射器生命周期。</summary>
public partial class BattleVerification
{
    /// <summary>将节点和子弹数组包装为版本2发射器。</summary>
    /// <param name="nodes">VNodeQueue数组文本。</param>
    /// <param name="bullets">BulletQueue数组文本，默认空。</param>
    /// <param name="stop">停止策略，默认保留子弹。</param>
    /// <param name="reference">根参考对象JSON值，默认世界原点。</param>
    /// <returns>可加载的完整Emitter JSON。</returns>
    private static string EmitterJson(string nodes, string bullets = "[]", string stop = "KeepBullets", string reference = "null")
        => $$"""
        { "Core": { "Id": "test", "Version": 2, "RefObject": {{reference}}, "Team": "Enemy", "Damage": 3, "StopMode": "{{stop}}" },
          "VNodes": {{nodes}}, "BulletQueues": {{bullets}} }
        """;

    /// <summary>验证两种实际对象共用运动、原子设置及坐标参考。</summary>
    private void VerifyVNodeModel()
    {
        // 使用隔离战斗，只推进指定Emitter及其节点。
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        battle.Boss.GlobalPosition = new Vector2(100, 100);
        battle.Bullets.Position = new Vector2(25, 40);
        const string nodes = """
        [
          { "Core": { "Id": "Parent" }, "BaseAttributes": { "Speed": 0 },
            "PositionAttributes": { "RefObject": "Emitter", "RefMoveQueue": [ { "Type": "XYMove", "X": { "Value": 10 }, "Y": {} } ] },
            "Timeline": [ { "StartMs": 0 } ] },
          { "Core": { "Id": "Child" }, "BaseAttributes": {},
            "PositionAttributes": { "RefObject": "Parent", "RefMoveQueue": [ { "Type": "XYMove", "X": {}, "Y": { "Value": 20 } } ] },
            "Timeline": [ { "StartMs": 0 } ] },
          { "Core": { "Id": "Snapshot" }, "BaseAttributes": {},
            "PositionAttributes": { "RefObject": "Parent", "Mode": "Snapshot" }, "Timeline": [ { "StartMs": 0 } ] }
        ]
        """;
        const string bullets = """
        [ { "Core": { "Id": "Ball", "LifeTimeS": 10 }, "Display": {}, "BaseAttributes": { "Speed": 0 },
            "PositionAttributes": { "RefObject": "Child", "Mode": "Follow" }, "Timeline": [ { "StartMs": 0 } ] } ]
        """;
        var emitter = BulletEmitter.FromJson(EmitterJson(nodes, bullets, reference: "\"Boss\""));
        emitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0);
        VNode parent = emitter.Nodes[0];
        VNode child = emitter.Nodes.Single(node => node.Queue!.Core.Id == "Child");
        VNode snapshot = emitter.Nodes.Single(node => node.Queue!.Core.Id == "Snapshot");
        Bullet bullet = emitter.Bullets.Single();
        Check(emitter.Nodes.Count == 3 && battle.Bullets.ActiveCount == 1, "VNode不计入子弹容量");
        Check(parent.GetChildCount() == 0 && child.GetChildCount() == 0 && !parent.IsPhysicsProcessing(), "纯节点无显示子节点且不自行物理更新");
        Check(bullet.Damage == 3 && bullet.Team == BulletTeam.Enemy, "子弹继承Emitter共通参数");
        Check(child.WorldPosition == new Vector2(110, 120) && bullet.GlobalPosition == child.WorldPosition, "树局部偏移及不同容器全局坐标一致");
        Check(!ReferenceEquals(child.GetParent(), parent) && !ReferenceEquals(bullet.GetParent(), child), "逻辑树与场景父子关系独立");
        battle.Boss.GlobalPosition = new Vector2(200, 200);
        Check(child.WorldPosition == new Vector2(210, 220) && snapshot.WorldPosition == new Vector2(110, 100), "读取时Follow立即更新，Snapshot保持出生快照");
        parent.ApplyParameters(new ParameterActionAttribute { X = 30, Y = 40 });
        Check(child.GlobalPosition == new Vector2(230, 260) && bullet.GlobalPosition == child.GlobalPosition, "父参数动作立即同步多层跟随者");
        emitter.Stop();
        Check(emitter.Nodes.Count == 0 && battle.Bullets.ActiveCount == 1 && !bullet.IsFollowing && bullet.IsAlive,
            "停止结束节点树并将保留的Follow子弹脱离参考");
        battle.Boss.Position += Vector2.One * 100;
        Check(bullet.WorldPosition == new Vector2(230, 260), "脱离后不再随Boss移动");
        battle.Bullets.Clear();

        // 相同运动参数，两种对象逐步得到相同实际速度和全局位置。
        var motionEmitter = BulletEmitter.FromJson(EmitterJson("""
        [{ "Core": { "Id": "Mover", "LifeTimeS": 10, "AAngleIsSameAsAngle": false },
           "BaseAttributes": { "Speed": 20, "ASpeed": 30, "AAngle": "PI/2" },
           "PositionAttributes": { "RefObject": "Emitter", "Mode": "Snapshot" }, "Timeline": [{ "StartMs": 0 }] }]
        """));
        motionEmitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0);
        VNode mover = motionEmitter.Nodes[0];
        Bullet equivalent = battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with
        {
            Position = Vector2.Zero,
            Speed = 20,
            ASpeed = 30,
            AAngle = Math.PI / 2,
            AAngleIsSameAsAngle = false,
            LifetimeSeconds = 10
        })!;
        for (int step = 0; step < 120; step++)
        {
            mover.Advance(1.0 / 60);
            equivalent.Advance(1.0 / 60);
            Check(mover.Velocity == equivalent.Velocity && mover.WorldPosition.DistanceTo(equivalent.WorldPosition) < 0.0001,
                "VNode与Bullet使用同一运动积分");
        }
        Vector2 oldVelocity = mover.Velocity;
        mover.ApplyParameters(new ParameterActionAttribute { ASpeed = -30, AAngle = 0 });
        Check(mover.Velocity == oldVelocity, "仅改加速度不重置速度");
        mover.ApplyParameters(new ParameterActionAttribute { Angle = Math.PI, Speed = 50, ASpeed = 10 });
        Check(mover.Velocity.DistanceTo(Vector2.Left * 50) < 0.001, "多字段动作一次性重建速度");
        double oldSpeed = mover.Speed;
        try { mover.ApplyParameters(new ParameterActionAttribute { Speed = 999, LifeTimeS = -1 }); Check(false, "无效原子动作须拒绝"); }
        catch (JsonException) { Check(mover.Speed == oldSpeed, "无效动作不留下部分修改"); }
        motionEmitter.Stop();
        world.Free();
    }

    /// <summary>验证时间数组、父索引不同周期和重叠批次绑定。</summary>
    private void VerifyVNodeSchedules()
    {
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        const string nodes = """
        [{ "Core": { "Id": "Roots", "Amount": 2, "LifeTimeS": 2 }, "BaseAttributes": {},
           "PositionAttributes": { "RefObject": "Emitter", "Mode": "Snapshot", "RefMoveQueue": [ { "Type": "XYMove", "X": { "ValueAdd": 100 }, "Y": {} } ] },
           "Timeline": [{ "AtMs": [0, 100] }] }]
        """;
        const string bullets = """
        [{ "Core": { "Id": "Balls", "LifeTimeS": 10 }, "Display": {}, "BaseAttributes": { "Speed": 0 },
           "PositionAttributes": { "RefObject": "Roots" },
           "Timeline": [{ "StartMs": 0, "StartMsAdd": 50, "IntervalMs": 100, "IntervalMsAdd": 100, "EndMs": 200, "EndMsAdd": 50 }] }]
        """;
        var emitter = BulletEmitter.FromJson(EmitterJson(nodes, bullets, reference: "\"Boss\""));
        battle.Boss.Position = Vector2.Zero;
        emitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0);
        Check(emitter.Nodes.Count == 2 && emitter.Bullets.Count == 1, "首批父节点零龄生成子弹");
        VerificationClock.EmitterSeconds(battle, 0.05, emitter);
        Check(emitter.Bullets.Count == 2 && emitter.Bullets[1].WorldPosition.X == 100, "父索引决定不同首次延迟");
        battle.Boss.Position = new Vector2(1000, 0);
        VerificationClock.EmitterSeconds(battle, 0.05, emitter);
        Check(emitter.Nodes.Count == 4 && emitter.Bullets.Count == 4, "新批次与旧批次独立重叠");
        VerificationClock.EmitterSeconds(battle, 0.25, emitter);
        Check(emitter.Bullets.Count == 10, "不同周期及包含结束端点产生预期十次发射");
        Check(emitter.Bullets.Count(item => item.WorldPosition.X == 0) == 3
            && emitter.Bullets.Count(item => item.WorldPosition.X == 100) == 2
            && emitter.Bullets.Count(item => item.WorldPosition.X == 1000) == 3
            && emitter.Bullets.Count(item => item.WorldPosition.X == 1100) == 2, "子任务始终绑定具体父实例");
        emitter.Stop();
        battle.Bullets.Clear();

        // 同刻参数修改按声明顺序，多个字段读取同一旧状态。
        var actions = BulletEmitter.FromJson(EmitterJson("""
        [{ "Core": { "Id": "Point" }, "BaseAttributes": {}, "PositionAttributes": {}, "Timeline": [{ "StartMs": 0 }],
           "MemberTimeline": [
             { "AtMs": [100, 100], "Set": { "Speed": 10 } },
             { "StartMs": 100, "Set": { "Speed": 20 } }
           ] }]
        """));
        actions.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0);
        VerificationClock.EmitterSeconds(battle, 0.1, actions);
        Check(actions.Nodes[0].Speed == 20, "重复固定时刻不去重，最后登记动作确定最终值");
        actions.Stop();
        world.Free();
    }

    /// <summary>验证到期回收、保留/清除策略以及满额时节点继续运行。</summary>
    private void VerifyVNodeLifecycle()
    {
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        const string nodes = """
        [{ "Core": { "Id": "Parent", "LifeTimeS": 0.1 }, "BaseAttributes": {}, "PositionAttributes": {}, "Timeline": [{ "StartMs": 0 }] },
         { "Core": { "Id": "Child" }, "BaseAttributes": {}, "PositionAttributes": { "RefObject": "Parent", "Mode": "Snapshot" }, "Timeline": [{ "StartMs": 0 }] }]
        """;
        const string bullets = """
        [{ "Core": { "Id": "Balls", "LifeTimeS": 10 }, "Display": {}, "BaseAttributes": { "Speed": 0 },
           "PositionAttributes": { "RefObject": "Child", "Mode": "Follow" }, "Timeline": [{ "StartMs": 0, "IntervalMs": 200 }],
           "MemberTimeline": [{ "StartMs": 200, "Set": { "AngleSource": "AimPlayer", "Speed": 150 } }] }]
        """;
        var emitter = BulletEmitter.FromJson(EmitterJson(nodes, bullets));
        emitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0);
        Bullet survivor = emitter.Bullets.Single();
        VerificationClock.EmitterSeconds(battle, 0.1, emitter);
        Check(emitter.Nodes.Count == 0 && emitter.Bullets.Count == 1 && !survivor.IsFollowing && survivor.ParentVNode is null,
            "父到期取消Snapshot后代和未来生成，已发子弹脱离引用继续运行");
        emitter.Stop();
        battle.Player.Position = new Vector2(300, 400);
        VerificationClock.BossSeconds(battle, 0.1);
        Check(survivor.Speed == 150 && Math.Abs(survivor.Angle - Math.Atan2(400, 300)) < 0.00001,
            "Emitter停止后已发子弹仍按自身年龄瞄准当前玩家");
        var clearing = BulletEmitter.FromJson(EmitterJson(nodes, bullets, "ClearBullets"));
        clearing.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0);
        Bullet removed = clearing.Bullets.Single();
        VerificationClock.EmitterSeconds(battle, 0.1, clearing);
        Check(clearing.Nodes.Count == 0 && removed.IsAlive, "ClearBullets只在Emitter停止时生效，节点到期不清弹");
        clearing.Stop();
        Check(clearing.Bullets.Count == 0 && !removed.IsAlive && removed.Timeline is null
            && battle.Bullets.ActiveBullets.Contains(survivor), "只清除所属Emitter子弹并取消其时间线");
        battle.Bullets.Clear();

        // 子弹满额不阻止节点生成、随机、运动和到期。
        for (int index = 0; index < BattleConfig.MaxBullets; index++)
            battle.Bullets.Spawn(BulletDefaultSet.Get(BulletType.ScaleSet) with { Position = new Vector2(-10000, -10000) });
        var full = BulletEmitter.FromJson(EmitterJson("""
        [{ "Core": { "Id": "Point", "LifeTimeS": 0.1 }, "BaseAttributes": { "Speed": 60 },
           "AddAttributesRandDiff": { "Angle": 1 }, "PositionAttributes": {}, "Timeline": [{ "StartMs": 0 }] }]
        """, """
        [{ "Core": { "Id": "Blocked" }, "Display": {}, "BaseAttributes": {}, "AddAttributesRandDiff": { "Speed": 100 },
           "PositionAttributes": { "RefObject": "Point" }, "Timeline": [{ "StartMs": 0 }] }]
        """));
        VMath.setRandomSeed(41);
        double expectedAngle = VMath.getRandomDiff(1, RandomDiffMode.Center);
        double expectedNext = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(41);
        full.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0);
        Check(full.Nodes.Count == 1 && full.Bullets.Count == 0 && VMath.getRandomDouble(0, 1) == expectedNext,
            "满额仍抽节点随机，但子弹随机不消耗");
        VNode point = full.Nodes[0];
        VerificationClock.EmitterSeconds(battle, 1.0 / 60, full);
        Check(point.WorldPosition.DistanceTo(VMath.PolarMove(Vector2.Zero, expectedAngle, 1)) < 0.001,
            "满额节点仍按固定步运动");
        point.ApplyParameters(new ParameterActionAttribute { LifeTimeS = 0.001 });
        Check(point.IsAlive, "寿命修改不在回调中立即释放");
        VerificationClock.EmitterSeconds(battle, 1.0 / 60, full);
        Check(full.Nodes.Count == 0, "缩短寿命在下一次固定步检查中释放");
        full.Stop();
        world.Free();
    }

    /// <summary>验证图引用、专用属性隔离、版本及时间边界。</summary>
    private void VerifyVNodeValidation()
    {
        const string minimal = """
        [{ "Core": { "Id": "A" }, "BaseAttributes": {}, "PositionAttributes": {}, "Timeline": [{ "StartMs": 0 }] }]
        """;
        string[] invalid =
        {
            EmitterJson(minimal).Replace("\"Version\": 2", "\"Version\": 1"),
            EmitterJson(minimal).Replace("\"RefObject\": null", "\"RefObject\": \"Other\""),
            EmitterJson(minimal.Replace("\"Id\": \"A\"", "\"Id\": \"A\", \"Radius\": 6")),
            EmitterJson(minimal.Replace("\"PositionAttributes\": {}", "\"PositionAttributes\": { \"RefObject\": \"A\" }")),
            EmitterJson(minimal.Replace("\"PositionAttributes\": {}", "\"PositionAttributes\": { \"RefObject\": \"Missing\" }")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 0, \"IntervalMs\": 100")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"AtMs\": [0, 100]")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": -1")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": \"PI\"")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 0, \"IntervalMs\": 0")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 2, \"IntervalMs\": 1, \"EndMs\": 1")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 0, \"AtMs\": [0]")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 9223372036854775807")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 0, \"Set\": { \"Speed\": 1 }")),
            EmitterJson(minimal, """
            [{ "Core": { "Id": "A" }, "Display": {}, "BaseAttributes": {}, "PositionAttributes": {} }]
            """),
            EmitterJson(minimal, """
            [{ "Core": { "Id": "B", "Damage": 2 }, "Display": {}, "BaseAttributes": {}, "PositionAttributes": {} }]
            """)
        };
        foreach (string json in invalid)
        {
            try { BulletEmitter.FromJson(json, "invalid-emitter.json"); Check(false, "非法Emitter必须在加载时拒绝"); }
            catch (JsonException error) { Check(error.Message.Contains("invalid-emitter.json"), "错误包含来源文件"); }
        }
        // 父索引端点验证，不能只检查第零个成员。
        string indexed = EmitterJson(minimal.Replace("\"Id\": \"A\"", "\"Id\": \"A\", \"Amount\": 3"), """
        [{ "Core": { "Id": "B" }, "Display": {}, "BaseAttributes": {}, "PositionAttributes": { "RefObject": "A" },
           "Timeline": [{ "StartMs": 0, "IntervalMs": 100, "IntervalMsAdd": -60 }] }]
        """);
        try { BulletEmitter.FromJson(indexed); Check(false, "末索引负周期必须拒绝"); }
        catch (JsonException) { _checks++; }
    }

    /// <summary>对四个迁移发射器逐固定步重放并比较节点、子弹与后续随机值。</summary>
    private void VerifyVNodeReplay()
    {
        foreach (string name in new[] { "B01P01_Emitter01", "B01P01_Emitter02", "B01P02_Emitter01", "B01P03_Emitter01" })
        {
            // 每次使用全新Emitter与同一输入、种子，捕获结构和实际运动。
            string[] Capture()
            {
                var battle = CreateBattle(out var world);
                battle.Boss.Stop();
                battle.Player.Attack.Stop();
                VMath.setRandomSeed(2026);
                var emitter = BulletEmitter.Load("res://Data/Emitters/" + name + ".json");
                emitter.Start(battle.Boss, battle.Bullets);
                var frames = new List<string>();
                for (int step = 0; step < 240; step++)
                {
                    battle.Boss.GlobalPosition = new Vector2(300 + step, 100);
                    battle.Player.GlobalPosition = new Vector2(600, 500 + step % 20);
                    battle.Timers.AdvanceByUnits(VTimerProcessor.FixedStepUnits, delta =>
                    {
                        emitter.AdvanceUnits(VTimerProcessor.FixedStepUnits);
                        battle.Bullets.Advance(delta, battle.Player, battle.Boss);
                    });
                    frames.Add(string.Join(";", emitter.Nodes.Concat<VNode>(emitter.Bullets)
                        .Select(node => $"{node.WorldPosition}|{node.Velocity}|{node.Angle:R}|{node.Age:R}|{node.BirthIndex}")));
                }
                frames.Add(VMath.getRandomDouble(0, 1).ToString("R"));
                emitter.Stop();
                world.Free();
                return frames.ToArray();
            }
            string[] first = Capture();
            string[] second = Capture();
            Check(first.SequenceEqual(second), name + "固定输入、节点生命周期和随机序列逐步重现");
        }
    }
}
