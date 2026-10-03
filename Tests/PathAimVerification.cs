using Godot;
using System;
using System.Linq;
using System.Text.Json;

/// <summary>玩家目标直线段的严格读取、出生快照及移动激光集成验证。</summary>
public partial class BattleVerification
{
    /// <summary>验证瞄准段的世界终点、连接、采样、独立快照及非法字段。</summary>
    private void VerifyAimPlayerPaths()
    {
        // 加载无需战斗或玩家实例，只有实际采样才读取玩家。
        var path = (VPathCreator)VNodeCreator.FromJson(PathFixture(
            """{"Type":"AimPlayer","X":"10+PI-PI","Y":-20}""", 3));
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        battle.Player.Position = new Vector2(640, 600);
        var source = new Vector2(300, 200);
        VMath.setRandomSeed(903);
        double expectedRandom = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(903);
        var points = path.SampleGeometry(source).Select(point => source + point).ToArray();
        Check(points.SequenceEqual(new[] { source, new Vector2(475, 390), new Vector2(650, 580) }),
            "瞄准直线相对玩家偏移，保留端点并等距采样");
        Check(VMath.getRandomDouble(0, 1) == expectedRandom, "瞄准路径采样不消耗随机");
        battle.Player.Position = new Vector2(700, 650);
        var next = path.SampleGeometry(source).Select(point => source + point).ToArray();
        Check(next[^1] == new Vector2(710, 630) && points[^1] == new Vector2(650, 580),
            "新路径读取新的玩家位置，旧缓存保持不变");
        // 混合路径承接上一段终点，瞄准结束后仍可继续普通直线。
        path = (VPathCreator)VNodeCreator.FromJson(PathFixture(
            PathLine + """,{"Type":"AimPlayer"},{"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":50}]}""", 4));
        points = path.SampleGeometry(source).Select(point => source + point).ToArray();
        Check(points.SequenceEqual(new[] { source, new Vector2(400, 200), new Vector2(700, 650), new Vector2(750, 650) }),
            "瞄准段与普通段按原声明顺序连接，省略偏移为零");
        foreach (string segment in new[]
        {
            """{"Type":"Unknown"}""", """{"Type":null}""",
            """{"Type":"AimPlayer","PathMode":"XY"}""",
            """{"Type":"AimPlayer","StartMoveQueue":[]}""",
            """{"Type":"AimPlayer","EndMoveQueue":[]}""",
            """{"Type":"AimPlayer","X":null}""", """{"Type":"AimPlayer","Y":"t"}""",
            """{"Type":"AimPlayer","X":"1/0"}""", """{"Type":"AimPlayer","X":1e39}""",
            """{"Type":"AimPlayer","Samples":10}""", """{"Type":"AimPlayer","Dist":100}""",
            """{"Type":"AimPlayer","X":0,"X":1}"""
        }) RejectPath(PathFixture(segment));
        // 和已有直线相同，零长度段不能形成路径，且保留段号诊断。
        path = (VPathCreator)VNodeCreator.FromJson(PathFixture("""{"Type":"AimPlayer"}"""));
        try { path.SampleGeometry(battle.Player.GlobalPosition); Check(false, "零长度瞄准路径须拒绝"); }
        catch (JsonException error) { Check(error.Message.Contains("PathQueue[0]"), "瞄准零长度诊断包含段索引"); }
        world.Free();
    }

    /// <summary>验证每颗延迟出生才冻结玩家坐标、重叠批次不覆盖、光束移动及清场。</summary>
    private void VerifyAimPlayerLasers()
    {
        // 相同Creator在0/200ms触发两批，每批第二颗延迟100ms，路径每颗独立。
        const string fixture = """
            {"Core":{"Type":"VLaser","Amount":2},"BaseAttributes":[{"RefMoveQueue":[{"Type":"XYMove","X":300,"Y":200}]}],
             "AddAttributes":{"SpawnDelayMs":100},"Laser":{"Mode":"Path","Length":180,"TravelSpeed":360,"EndMs":4000,"PathPointCount":2},
             "PathQueue":[{"Type":"AimPlayer","X":10,"Y":-20}],"Timeline":[{"AtMs":[0,200]}]}
            """;
        var battle = CreateBattle(out var world);
        battle.Player.Position = new Vector2(640, 600);
        var emitter = StartSpawnFixture(battle, fixture, "ClearBullets");
        var first = (VLaser)emitter.Bullets.Single();
        var origin = new Vector2(300, 200);
        var firstTarget = new Vector2(650, 580);
        Check(first.PointAt(0) == origin && first.PointAt(first.PathLength) == firstTarget
            && first.Stage == VLaserStage.Active && first.HeadDistance == 0, "移动激光从实际出生点到玩家偏移点，无定点预警阶段");
        battle.Player.Position = new Vector2(700, 650);
        VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
        var second = emitter.Bullets.OfType<VLaser>().Last();
        Check(emitter.Bullets.Count == 2 && second.PointAt(second.PathLength) == new Vector2(710, 630),
            "延迟激光使用实际出生时玩家位置，不使用批次触发时目标");
        Check(first.PointAt(first.PathLength) == firstTarget
            && first.WorldPosition.DistanceTo(origin + (firstTarget - origin).Normalized() * 36) < 0.001,
            "旧激光沿冻结直线每100ms移动36像素，不追踪玩家");
        battle.Player.Position = new Vector2(480, 500);
        VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
        var third = emitter.Bullets.OfType<VLaser>().Last();
        Check(emitter.Root.Batches.Count == 2 && third.PointAt(third.PathLength) == new Vector2(490, 480)
            && second.PointAt(second.PathLength) == new Vector2(710, 630), "重叠批次分别冻结路径");
        emitter.Stop();
        VerificationClock.BattleSeconds(battle, 0.2, Vector2.Zero, false);
        Check(emitter.Bullets.Count == 0 && emitter.Root.Batches.Count == 0, "清场释放瞄准路径并取消剩余延迟出生");
        world.Free();

        // 用短直线单独核对头尾移动：头尾越过目标后保持原方向，直到EndMs释放。
        battle = CreateBattle(out world);
        battle.Player.Position = new Vector2(600, 400);
        emitter = StartSpawnFixture(battle, """
            {"Core":{"Type":"VLaser"},"BaseAttributes":[{"RefMoveQueue":[{"Type":"XYMove","X":240,"Y":400}]}],
             "Laser":{"Mode":"Path","Length":180,"TravelSpeed":360,"EndMs":4000,"PathPointCount":2},
             "PathQueue":[{"Type":"AimPlayer"}],"Timeline":[{"StartMs":0}]}
            """);
        first = (VLaser)emitter.Bullets.Single();
        battle.Player.Position = new Vector2(640, 750);
        VerificationClock.BattleSeconds(battle, 1, Vector2.Zero, false);
        Check(first.IsAlive && first.PointAt(first.PathLength) == new Vector2(600, 400)
            && first.HeadDistance == 360 && first.TailDistance == 180, "头部到达目标快照后尾部仍在路径上");
        VerificationClock.BattleSeconds(battle, 0.5, Vector2.Zero, false);
        Check(first.IsAlive && first.WorldPosition == new Vector2(780, 400),
            "瞄准激光越过目标后继续沿冻结方向前进");
        VerificationClock.BattleSeconds(battle, 2.5, Vector2.Zero, false);
        Check(!first.IsAlive && emitter.Bullets.Count == 0, "瞄准移动激光持续至4000ms而非路径末端");
        world.Free();
    }
}