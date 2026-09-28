using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

/// <summary>验证共用节点模型、树引用、数据时间规则和发射器生命周期。</summary>
public partial class BattleVerification
{
    /// <summary>将单根Creator包装为当前发射器格式。</summary>
    /// <param name="nodes">单个根Creator对象。</param>
    /// <param name="stop">停止策略。</param>
    /// <param name="reference">Boss或null的JSON值。</param>
    /// <returns>完整Emitter JSON。</returns>
    private static string EmitterJson(string nodes, string stop = "KeepBullets", string reference = "null")
        => $$"""
        { "Core": { "RefObject": {{reference}}, "Team": "Enemy", "Damage": 3, "StopMode": "{{stop}}" }, "VNodes": {{nodes}} }
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
        {"Core": {"Name": "Parent", "Type": "VNode"}, "BaseAttributes": [{"Speed": 0, "RefMoveQueue": [{"Type": "XYMove", "X": 10, "Y": 0}]}], "Timeline": [{"StartMs": 0}], "Children": [{"Core": {"Name": "Child", "Type": "VNode"}, "BaseAttributes": [{"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 20}]}], "Timeline": [{"StartMs": 0}], "Children": [{"Core": {"LifeTimeMs": 10000, "Name": "Ball", "Type": "VBullet", "CreatePositionMode": "Follow"}, "Display": {}, "BaseAttributes": [{"Speed": 0}], "Timeline": [{"StartMs": 0}], "AddAttributes": {}}], "AddAttributes": {"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 0}]}, "RandDiffAttributes": {"Batch": {"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 0}]}, "Member": {"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 0}]}}}, {"Core": {"Name": "Snapshot", "Type": "VNode", "CreatePositionMode": "Snapshot"}, "BaseAttributes": [{}], "Timeline": [{"StartMs": 0}], "AddAttributes": {}}], "AddAttributes": {"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 0}]}, "RandDiffAttributes": {"Batch": {"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 0}]}, "Member": {"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 0}]}}}
        """;
        var emitter = VBulletEmitter.FromJson(EmitterJson(nodes, reference: "\"Boss\""));
        emitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        VNode parent = emitter.Nodes[0];
        VNode child = emitter.Nodes.Single(node => node.Creator!.Core.Name == "Child");
        VNode snapshot = emitter.Nodes.Single(node => node.Creator!.Core.Name == "Snapshot");
        VBullet bullet = emitter.Bullets.Single();
        Check(emitter.Nodes.Count == 3 && battle.Bullets.ActiveCount == 1, "VNode不计入子弹容量");
        Check(parent.GetChildCount() == 0 && child.GetChildCount() == 0 && !parent.IsPhysicsProcessing(), "纯节点无显示子节点且不自行物理更新");
        Check(bullet.Damage == 3 && bullet.Team == VBulletTeam.Enemy, "子弹继承Emitter共通参数");
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
        var motionEmitter = VBulletEmitter.FromJson(EmitterJson("""
        {"Core": {"LifeTimeMs": 10000, "AAngleIsSameAsAngle": false, "Name": "Mover", "Type": "VNode", "CreatePositionMode": "Snapshot"}, "BaseAttributes": [{"Speed": 20, "ASpeed": 30, "AAngle": "PI/2"}], "Timeline": [{"StartMs": 0}], "AddAttributes": {}}
        """));
        motionEmitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        VNode mover = motionEmitter.Nodes[0];
        VBullet equivalent = battle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.ScaleSet) with
        {
            Position = Vector2.Zero,
            Speed = 20,
            ASpeed = 30,
            AAngle = Math.PI / 2,
            AAngleIsSameAsAngle = false,
            LifeTimeMs = 10000
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
        try { mover.ApplyParameters(new ParameterActionAttribute { Speed = 999, LifeTimeMs = -1 }); Check(false, "无效原子动作须拒绝"); }
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
        {"Core": {"Amount": 2, "LifeTimeMs": 2000, "Name": "Roots", "Type": "VNode", "CreatePositionMode": "Snapshot"}, "BaseAttributes": [{"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 0}]}], "Timeline": [{"AtMs": [0, 100]}], "Children": [{"Core": {"LifeTimeMs": 10000, "Name": "Balls", "Type": "VBullet"}, "Display": {}, "BaseAttributes": [{"Speed": 0}], "Timeline": [{"StartMs": 0, "StartMsAdd": 50, "IntervalMs": 100, "IntervalMsAdd": 100, "EndMs": 200, "EndMsAdd": 50}], "AddAttributes": {}}], "AddAttributes": {"RefMoveQueue": [{"Type": "XYMove", "X": 100, "Y": 0}]}, "RandDiffAttributes": {"Batch": {"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 0}]}, "Member": {"RefMoveQueue": [{"Type": "XYMove", "X": 0, "Y": 0}]}}}
        """;
        var emitter = VBulletEmitter.FromJson(EmitterJson(nodes, reference: "\"Boss\""));
        battle.Boss.Position = Vector2.Zero;
        emitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
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
        var actions = VBulletEmitter.FromJson(EmitterJson("""
        {"Core": {"Name": "Point", "Type": "VNode"}, "BaseAttributes": [{}], "Timeline": [{"StartMs": 0}], "MemberTimeline": [{"AtMs": [100, 100], "Set": {"Speed": 10}}, {"StartMs": 100, "Set": {"Speed": 20}}], "AddAttributes": {}}
        """));
        actions.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
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
        {"Core": {"LifeTimeMs": 100, "Name": "Parent", "Type": "VNode"}, "BaseAttributes": [{}], "Timeline": [{"StartMs": 0}], "Children": [{"Core": {"Name": "Child", "Type": "VNode", "CreatePositionMode": "Snapshot"}, "BaseAttributes": [{}], "Timeline": [{"StartMs": 0}], "Children": [{"Core": {"LifeTimeMs": 10000, "Name": "Balls", "Type": "VBullet", "CreatePositionMode": "Follow"}, "Display": {}, "BaseAttributes": [{"Speed": 0}], "Timeline": [{"StartMs": 0, "IntervalMs": 200}], "MemberTimeline": [{"StartMs": 200, "Set": {"AngleSource": "AimPlayer", "Speed": 150}}], "AddAttributes": {}}], "AddAttributes": {}}], "AddAttributes": {}}
        """;
        var emitter = VBulletEmitter.FromJson(EmitterJson(nodes));
        emitter.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        VBullet survivor = emitter.Bullets.Single();
        VerificationClock.EmitterSeconds(battle, 0.1, emitter);
        Check(emitter.Nodes.Count == 0 && emitter.Bullets.Count == 1 && !survivor.IsFollowing && survivor.ParentVNode is null,
            "父到期取消Snapshot后代和未来生成，已发子弹脱离引用继续运行");
        emitter.Stop();
        battle.Player.Position = new Vector2(300, 400);
        VerificationClock.BossSeconds(battle, 0.1);
        Check(survivor.Speed == 150 && Math.Abs(survivor.Angle - Math.Atan2(400, 300)) < 0.00001,
            "Emitter停止后已发子弹仍按自身年龄瞄准当前玩家");
        var clearing = VBulletEmitter.FromJson(EmitterJson(nodes, "ClearBullets"));
        clearing.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        VBullet removed = clearing.Bullets.Single();
        VerificationClock.EmitterSeconds(battle, 0.1, clearing);
        Check(clearing.Nodes.Count == 0 && removed.IsAlive, "ClearBullets只在Emitter停止时生效，节点到期不清弹");
        clearing.Stop();
        Check(clearing.Bullets.Count == 0 && !removed.IsAlive && removed.Timeline is null
            && battle.Bullets.ActiveBullets.Contains(survivor), "只清除所属Emitter子弹并取消其时间线");
        battle.Bullets.Clear();

        // 子弹满额不阻止节点生成、随机、运动和到期。
        for (int index = 0; index < BattleConfig.MaxBullets; index++)
            battle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.ScaleSet) with { Position = new Vector2(-10000, -10000) });
        var full = VBulletEmitter.FromJson(EmitterJson("""
        {"Core": {"LifeTimeMs": 100, "Name": "Point", "Type": "VNode"}, "BaseAttributes": [{"Speed": 60}], "Timeline": [{"StartMs": 0}], "Children": [{"Core": {"Name": "Blocked", "Type": "VBullet"}, "Display": {}, "BaseAttributes": [{}], "Timeline": [{"StartMs": 0}], "AddAttributes": {}, "RandDiffAttributes": {"Batch": {}, "Member": {"Speed": 100}}}], "AddAttributes": {}, "RandDiffAttributes": {"Batch": {}, "Member": {"Angle": 1}}}
        """));
        VMath.setRandomSeed(41);
        double expectedAngle = VMath.getRandomDiff(1, RandomDiffMode.Center);
        double expectedNext = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(41);
        full.Start(battle.Boss, battle.Bullets);
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        Check(full.Nodes.Count == 1 && full.Bullets.Count == 0 && VMath.getRandomDouble(0, 1) == expectedNext,
            "满额仍抽节点随机，但子弹随机不消耗");
        VNode point = full.Nodes[0];
        VerificationClock.EmitterSeconds(battle, 1.0 / 60, full);
        Check(point.WorldPosition.DistanceTo(VMath.PolarMove(Vector2.Zero, expectedAngle, 1)) < 0.001,
            "满额节点仍按固定步运动");
        point.ApplyParameters(new ParameterActionAttribute { LifeTimeMs = 1 });
        Check(point.IsAlive, "寿命修改不在回调中立即释放");
        VerificationClock.EmitterSeconds(battle, 1.0 / 60, full);
        Check(full.Nodes.Count == 0, "缩短寿命在下一次固定步检查中释放");
        full.Stop();
        world.Free();
    }

    /// <summary>验证路径身份、可选名称、旧字段拒绝及时间边界。</summary>
    private void VerifyVNodeValidation()
    {
        // 当前最小定义不含Version或Id，名称也可省略。
        const string minimal = """
        {"Core": {"Type": "VNode"}, "BaseAttributes": [{}], "Timeline": [{"StartMs": 0}], "AddAttributes": {}}
        """;
        string[] invalid =
        {
            EmitterJson(minimal.Replace("\"Type\": \"VNode\"", "\"Type\": \"VNode\", \"Version\": 2")),
            EmitterJson(minimal.Replace("\"Type\": \"VNode\"", "\"Type\": \"VNode\", \"Id\": \"A\"")),
            EmitterJson(minimal.Replace("\"VNode\"", "\"Bullet\"")),
            EmitterJson(minimal.Replace("\"Type\": \"VNode\"", "\"Type\": \"VNode\", \"Radius\": 6")),
            EmitterJson(minimal.Replace("\"Core\":", "\"PositionAttributes\": {}, \"Core\":")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 0, \"IntervalMs\": 100")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"AtMs\": [0, 100]")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": -1")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": \"PI\"")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 0, \"IntervalMs\": 0")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 2, \"IntervalMs\": 1, \"EndMs\": 1")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 0, \"AtMs\": [0]")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 9223372036854775807")),
            EmitterJson(minimal.Replace("\"StartMs\": 0", "\"StartMs\": 0, \"Set\": { \"Speed\": 1 }")),
            EmitterJson("[" + minimal + "]"),
            EmitterJson(minimal).Replace("\"VNodes\":", "\"BulletQueues\": [], \"VNodes\":")
        };
        foreach (string json in invalid)
        {
            try { VBulletEmitter.FromJson(json, "invalid-tree"); Check(false, "无效树必须拒绝"); }
            catch (JsonException error) { Check(error.Message.Contains("invalid-tree"), "树错误包含来源"); }
        }
        VerifyCreatorIdentityAndDispatch();
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
                var emitter = VBulletEmitter.Load("res://Data/Emitters/" + name + ".json");
                emitter.Start(battle.Boss, battle.Bullets);
                var frames = new List<string>();
                for (int step = 0; step < 240; step++)
                {
                    battle.Boss.GlobalPosition = new Vector2(300 + step, 100);
                    battle.Player.GlobalPosition = new Vector2(600, 500 + step % 20);
                    battle.Timers.AdvanceByUnits(VTimerProcessor.FixedStepUnits, delta =>
                    {
                        battle.Bullets.Advance(delta, battle.Player, battle.Boss);
                    }, battle.Bullets.DispatchTimelines);
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
