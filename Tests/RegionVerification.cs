using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>区域接口、出界回收和选边/选弧反射的定向验证。</summary>
public partial class BattleVerification
{
    /// <summary>验证闭区域、角色约束、出界时钟、连续反射及数据入口。</summary>
    private void VerifyRegions()
    {
        VerifyRegionShapes();
        VerifyOutsideTimeout();
        VerifyRegionReflection();
        VerifyRegionData();
        VerifyRegionReferences();
        VerifyCustomOutsideRegions();
    }

    /// <summary>验证点、完整判定圆、相交圆和角色投影的不同语义。</summary>
    private void VerifyRegionShapes()
    {
        // 圆与矩形均包含边界，半径参数用于完整包含角色判定圆。
        IRegionShape circle = new CircleRegionShape(new Vector2(100, 100), 50);
        IRegionShape rectangle = new RectangleRegionShape(new Rect2(50, 50, 100, 100));
        foreach (var shape in new[] { circle, rectangle })
        {
            Check(shape.Contains(new Vector2(150, 100)) && !shape.Contains(new Vector2(150.01f, 100)), "闭区域边界包含且边外排除");
            Check(shape.Contains(new Vector2(145, 100), 5) && !shape.Contains(new Vector2(150, 100), 5), "角色判定圆完整位于区域内");
            Check(shape.IntersectsCircle(new Vector2(155, 100), 5) && !shape.IntersectsCircle(new Vector2(155.1f, 100), 5), "圆体相交与中心在内可区分");
            Check(shape.Clamp(new Vector2(300, 100), 5) == new Vector2(145, 100), "按判定半径约束位置");
            Check(shape.Clamp(new Vector2(100, 100), 50) == new Vector2(100, 100), "最大可容纳圆的中心保持不变");
            try { shape.Clamp(Vector2.Zero, 51); Check(false, "无法容纳的判定圆必须报错"); }
            catch (ArgumentOutOfRangeException) { Check(true, "无法容纳的判定圆被拒绝"); }
            foreach (var input in new[] { new Vector2(float.NaN, 0), new Vector2(float.PositiveInfinity, 0) })
            {
                try { shape.Contains(input); Check(false, "非有限查询必须报错"); }
                catch (ArgumentOutOfRangeException) { Check(true, "非有限查询被拒绝"); }
            }
        }
        Check(!circle.Contains(new Vector2(150, 150)) && rectangle.Contains(new Vector2(150, 150)), "两种形状按真实轮廓区分角点");
        Check(circle.TryGetExit(new Vector2(100, 100), new Vector2(200, 100), out var circleExit)
            && circleExit.Point == new Vector2(150, 100) && circleExit.Normal == Vector2.Right && circleExit.Fraction == 0.5,
            "圆形出界交点与外向法线");
        Check(rectangle.TryGetExit(new Vector2(100, 100), new Vector2(200, 200), out var corner)
            && corner.Point == new Vector2(150, 150) && corner.Edges == (RectangleEdges.Right | RectangleEdges.Bottom), "角点同时保存两条命中边");
        Check(!circle.TryGetExit(new Vector2(200, 100), new Vector2(100, 100), out _), "从区域外进入不会在入口反射");
        Check(!rectangle.TryGetExit(new Vector2(100, 100), new Vector2(150, 100), out _), "下一步恰在边界上时尚未出界");
        Check(VMath.ReflectVector(new Vector2(3, 4), Vector2.Right) == new Vector2(-3, 4), "法线反射保持切向分量");
        Check(VMath.ReflectVector(new Vector2(3, 4), new Vector2(0, 10)) == new Vector2(3, -4), "非单位法线正确归一化投影");
        Check(BattleConfig.GameRegion is CircleRegionShape arena && arena.Center == new Vector2(640, 400) && arena.Radius == 400,
            "现有游戏圆形区域由接口实例定义");
        var movement = new PlayerMovement();
        Check(movement.Clamp(new Vector2(2000, 400)) == new Vector2(1035, 400), "玩家约束沿用400减角色半径的范围");
        foreach (var action in new Action[]
        {
            () => new CircleRegionShape(Vector2.Zero, 0),
            () => new CircleRegionShape(Vector2.Zero, double.NaN),
            () => new RectangleRegionShape(new Rect2(0, 0, -1, 2)),
            () => VMath.ReflectVector(Vector2.One, Vector2.Zero)
        })
        {
            try { action(); Check(false, "非法几何参数必须报错"); }
            catch (ArgumentException) { Check(true, "非法几何参数被拒绝"); }
        }
    }

    /// <summary>验证按中心连续出界计时、回区重置、零阈值、独立寿命及统一释放。</summary>
    private void VerifyOutsideTimeout()
    {
        // 避开角色碰撞，并停止阶段发射；弹幕完整半径仍可能覆盖区域。
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        var settings = VBulletDefaultSet.Get(VBulletType.PlayerSet) with
        {
            Position = new Vector2(1040.1f, 400),
            Speed = 0,
            LifeTimeMs = 10000
        };
        var bullet = battle.Bullets.Spawn(settings)!;
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        for (int tick = 0; tick < 119; tick++) battle.StepFixed(Vector2.Zero, false);
        Check(bullet.IsAlive && bullet.OutsideTimeoutMs == 2000 && Math.Abs(bullet.OutsideAge - 119.0 / 60) < 1e-10,
            "中心连续出界不足2秒保留，默认阈值2000ms");
        battle.StepFixed(Vector2.Zero, false);
        Check(!bullet.IsAlive && battle.Bullets.ActiveCount == 0, "连续出界120个固定步后统一释放");
        bullet = battle.Bullets.Spawn(settings)!;
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        VerificationClock.BattleSeconds(battle, 1, Vector2.Zero, false);
        bullet.ApplyParameters(new ParameterActionAttribute { X = 1040 });
        battle.StepFixed(Vector2.Zero, false);
        Check(bullet.IsAlive && bullet.OutsideAge == 0, "中心回到圆周即清零，即使判定圆部分在外");
        bullet.ApplyParameters(new ParameterActionAttribute { X = 1041 });
        VerificationClock.BattleSeconds(battle, 1.5, Vector2.Zero, false);
        Check(bullet.IsAlive && bullet.OutsideAge == 1.5, "再次出界重新计时，不累计此前1秒");
        VerificationClock.BattleSeconds(battle, 0.5, Vector2.Zero, false);
        Check(!bullet.IsAlive, "第二段连续出界达到阈值后释放");

        bullet = battle.Bullets.Spawn(settings with { OutsideTimeoutMs = 0 })!;
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        battle.StepFixed(Vector2.Zero, false);
        Check(!bullet.IsAlive, "零阈值首次出界检查即回收");
        bullet = battle.Bullets.Spawn(settings with
        {
            Position = new Vector2(1000, 400),
            OutsideTimeoutMs = 0,
            LifeTimeMs = 100
        })!;
        battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        Check(bullet.IsAlive && bullet.OutsideAge == 0, "区域内零阈值不会误释放");
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        Check(!bullet.IsAlive, "正常总寿命仍然独立生效");
        world.Free();

        // Creator树内弹幕回收必须同时取消后代生成，但不额外删除既有子弹。
        battle = CreateBattle(out world);
        string json = """
            {"Core":{"Type":"VBullet","LifeTimeMs":10000,"OutsideTimeoutMs":50},
             "Display":{},"BaseAttributes":[{"Speed":0,"RefMoveQueue":[{"Type":"XYMove","X":1100,"Y":400}]}],
             "Timeline":[{"StartMs":0}],
             "Children":[{"Core":{"Type":"VNode"},"BaseAttributes":[{}],"Timeline":[{"StartMs":0}]}]}
            """;
        var emitter = StartSpawnFixture(battle, json);
        Check(emitter.Root.Members.Count == 1 && emitter.Nodes.Count == 1, "树内弹幕及纯节点后代建立");
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        Check(emitter.Root.Members.Count == 0 && emitter.Root.Batches.Count == 0 && emitter.Nodes.Count == 0,
            "出界回收沿用Creator批次和后代清理流程");
        world.Free();
    }

    /// <summary>验证镜面反射、加速后速率、角点选边、圆弧选择和折线碰撞。</summary>
    private void VerifyRegionReflection()
    {
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        // 反射区域独立于全场圆形区域，使用世界矩形。
        var rectangle = new RectangleRegionShape(new Rect2(300, 100, 200, 200));
        var all = new VReflectionRegion(rectangle);
        var basis = VBulletDefaultSet.Get(VBulletType.PlayerSet) with
        {
            Position = new Vector2(490, 150),
            Speed = 1200,
            LifeTimeMs = 10000,
            Reflectable = true,
            ReflectionRegion = all
        };
        var bullet = battle.Bullets.Spawn(basis)!;
        bullet.Advance(1.0 / 60);
        Check(bullet.WorldPosition.DistanceTo(new Vector2(490, 150)) < 0.001 && bullet.Velocity == new Vector2(-1200, 0)
            && Math.Abs(bullet.Angle - Math.PI) < 1e-10, "下一步出界时命中右边并反射本步剩余位移");
        Check(bullet.Speed == 1200 && bullet.LifeTimeMs == 10000 && bullet.Radius == basis.Radius, "反射不改其他出生参数");
        battle.Bullets.Release(bullet);

        bullet = battle.Bullets.Spawn(basis with
        {
            Speed = 100,
            ASpeed = 1000,
            AAngle = 0.7
        })!;
        bullet.Advance(0.1);
        Check(bullet.Velocity.DistanceTo(new Vector2(-200, 0)) < 0.001 && bullet.Speed == 100
            && bullet.ASpeed == 1000 && bullet.AAngle == VMath.StandardizationAngle(0.7), "反射保留已累计实际速率且不改速度和加速度配置");
        bullet.Advance(0.1);
        Check(bullet.Velocity.DistanceTo(new Vector2(-300, 0)) < 0.001, "同向加速度沿反射后角度继续累计");
        battle.Bullets.Release(bullet);

        bullet = battle.Bullets.Spawn(basis with
        {
            AngleRadians = Math.PI,
            Speed = -1200
        })!;
        bullet.Advance(1.0 / 60);
        Check(bullet.Velocity.DistanceTo(new Vector2(-1200, 0)) < 0.001 && bullet.Speed == -1200
            && (bullet.Angle < 0.000001 || Math.Tau - bullet.Angle < 0.000001), "负速度反射保留外部方向和实际方向的反向关系");
        battle.Bullets.Release(bullet);

        // 同时撞到两条边时分别反射，不能简单按45度法线交换两个速度分量。
        foreach (var edges in new[] { RectangleEdges.All, RectangleEdges.Right, RectangleEdges.None })
        {
            bullet = battle.Bullets.Spawn(basis with
            {
                Position = new Vector2(480, 290),
                Speed = Math.Sqrt(2400 * 2400 + 1200 * 1200),
                AngleRadians = Math.Atan2(1200, 2400),
                ReflectionRegion = new VReflectionRegion(rectangle, edges)
            })!;
            bullet.Advance(1.0 / 60);
            Vector2 expected = edges == RectangleEdges.All ? new Vector2(480, 290)
                : edges == RectangleEdges.Right ? new Vector2(480, 310) : new Vector2(520, 310);
            Check(bullet.WorldPosition.DistanceTo(expected) < 0.001, "角点只反射所选边，未选边允许穿出");
            battle.Bullets.Release(bullet);
        }

        bullet = battle.Bullets.Spawn(basis with
        {
            Position = new Vector2(305, 150),
            Speed = 1800,
            ReflectionRegion = new VReflectionRegion(new RectangleRegionShape(new Rect2(300, 100, 10, 100)))
        })!;
        bullet.Advance(1.0 / 60);
        Check(bullet.WorldPosition.DistanceTo(new Vector2(305, 150)) < 0.001 && bullet.Velocity.X == -1800
            && Math.Abs(bullet.Age - 1.0 / 60) < 1e-10, "一个逻辑步多次反射不重复加速或计龄");
        battle.Bullets.Release(bullet);

        // 真实路径先向右下到角点再向左下；其首尾连线不等于实际扫掠路径。
        bullet = battle.Bullets.Spawn(basis with
        {
            Speed = Math.Sqrt(2) * 1200,
            AngleRadians = Math.PI / 4
        })!;
        bullet.Advance(1.0 / 60);
        Check(bullet.SweptRegionHit(new Vector2(490, 150), new Vector2(500, 160), new Vector2(500, 160), 1),
            "反射接触点处仍检测连续碰撞");
        Check(!bullet.SweptRegionHit(new Vector2(490, 150), new Vector2(490, 160), new Vector2(490, 160), 1),
            "反射折线不会按首尾连线误判内部目标");
        battle.Bullets.Release(bullet);

        var circle = new CircleRegionShape(new Vector2(640, 400), 100);
        foreach (var arc in new[] { (0.0, Math.Tau, true), (Math.PI / 2, Math.PI, false),
            (Math.PI * 1.5, Math.PI / 2, true), (0.0, 0.0, true) })
        {
            bullet = battle.Bullets.Spawn(basis with
            {
                Position = new Vector2(730, 400),
                ReflectionRegion = new VReflectionRegion(circle, arc.Item1, arc.Item2)
            })!;
            bullet.Advance(1.0 / 60);
            Check(Math.Abs(bullet.WorldPosition.X - (arc.Item3 ? 730 : 750)) < 0.001
                && (bullet.Velocity.X < 0) == arc.Item3, "圆弧支持包含端点、跨零与未选弧穿出");
            battle.Bullets.Release(bullet);
        }
        bullet = battle.Bullets.Spawn(basis with
        {
            Position = new Vector2(700, 400),
            AngleRadians = Math.Atan2(1800, 2400),
            Speed = 3000,
            ReflectionRegion = new VReflectionRegion(circle)
        })!;
        bullet.Advance(1.0 / 60);
        Check(circle.Contains(bullet.WorldPosition) && Math.Abs(bullet.Velocity.Length() - 3000) < 0.001
            && bullet.Angle > Math.PI / 2, "斜入射圆周按交点法线反射并保留速率");
        battle.Bullets.Release(bullet);
        world.Free();
    }

    /// <summary>验证数据传到延迟出生实体、世界反射区域与父平移，以及父结束后的独立存活。</summary>
    private void VerifyRegionReferences()
    {
        // 两种参考模式共享世界反射区域，容器自身也带非零平移。
        foreach (string mode in new[] { "Follow", "Snapshot" })
        {
            var battle = CreateBattle(out var world);
            battle.Bullets.Position = new Vector2(17, 23);
            string json = $$$"""
                {"Core":{"Type":"VNode","LifeTimeMs":150},
                 "BaseAttributes":[{"RefMoveQueue":[{"Type":"XYMove","X":480,"Y":150}]}],
                 "Timeline":[{"StartMs":0}],"Children":[{
                  "Core":{"Type":"VBullet","LifeTimeMs":5000,"OutsideTimeoutMs":1700,
                   "CreatePositionMode":"{{{mode}}}","Reflectable":true,
                   "ReflectionRegion":{"Type":"Rectangle","X":300,"Y":100,"Width":200,"Height":200,"Edges":["Right"]}},
                  "Display":{},"BaseAttributes":[{"Speed":1200,"SpawnDelayMs":50}],
                  "Timeline":[{"StartMs":0}]}]}
                """;
            var emitter = StartSpawnFixture(battle, json);
            var child = emitter.Root.Children[0];
            Check(child.Members.Count == 0, "区域配置不会令延迟成员提前出生");
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            var bullet = (VBullet)child.Members.Single();
            Check(bullet.Reflectable && bullet.OutsideTimeoutMs == 1700
                && bullet.ReflectionRegion!.Edges == RectangleEdges.Right
                && bullet.WorldPosition == new Vector2(480, 150), "JSON区域与阈值传递至出生实体，位置使用世界坐标");
            emitter.Root.Members[0].ApplyParameters(new ParameterActionAttribute { X = 490 });
            battle.StepFixed(Vector2.Zero, false);
            Check(bullet.WorldPosition.DistanceTo(new Vector2(mode == "Follow" ? 490 : 500, 150)) < 0.001,
                "Follow继承父平移后在固定世界边界反射，Snapshot保持独立");
            emitter.Root.Members[0].ApplyParameters(new ParameterActionAttribute { X = 492 });
            battle.StepFixed(Vector2.Zero, false);
            Check(bullet.WorldPosition.DistanceTo(new Vector2(mode == "Follow" ? 472 : 480, 150)) < 0.001
                && bullet.Velocity.X == -1200, "反射后跟随偏移正确保存，边界起步向外可反射");
            VerificationClock.BattleSeconds(battle, 4.0 / 60, Vector2.Zero, false);
            Check(bullet.IsAlive && !bullet.IsFollowing && emitter.Root.Members.Count == 0,
                "父节点结束不清除已生反射弹幕");
            battle.Bullets.Clear();
            Check(child.Members.Count == 0 && battle.Bullets.ActiveCount == 0, "清场释放反射弹幕及Creator引用");
            world.Free();
        }

        // 激光位于游戏区域外超过默认2秒，仍只受自身阶段寿命控制。
        var laserBattle = CreateBattle(out var laserWorld);
        laserBattle.Boss.Stop();
        laserBattle.Player.Attack.Stop();
        var laser = laserBattle.Bullets.SpawnLaser(new VLaserAttribute
        {
            WarningMs = 0,
            ExpandMs = 0,
            ActiveMs = 3000,
            FadeMs = 0
        }, new Vector2(1500, 400))!;
        VerificationClock.BattleSeconds(laserBattle, 2.5, Vector2.Zero, false);
        Check(laser.IsAlive && laser.OutsideAge == 0, "激光不使用普通弹幕出界计时");
        laserWorld.Free();
    }
    /// <summary>验证自定义出界区域、回区清零、严格数据校验及正式Emitter的默认策略。</summary>
    private void VerifyCustomOutsideRegions()
    {
        // 两种自定义区域均位于游戏区域之外，确认默认游戏圆不会覆盖显式配置。
        foreach (string region in new[]
        {
            """{"Type":"Circle","X":1200,"Y":400,"Radius":"100+0*PI"}""",
            """{"Type":"Rectangle","X":1100,"Y":300,"Width":200,"Height":200}"""
        })
        {
            var battle = CreateBattle(out var world);
            string json = $$$"""
                {"Core":{"Type":"VBullet","LifeTimeMs":10000,"OutsideTimeoutMs":50,
                  "OutsideRegion":{{{region}}},"Reflectable":true,
                  "ReflectionRegion":{"Type":"Circle","X":640,"Y":400,"Radius":400}},
                 "Display":{},"BaseAttributes":[{"Speed":0,"RefMoveQueue":[{"Type":"XYMove","X":1200,"Y":400}]}],
                 "Timeline":[{"StartMs":0}]}
                """;
            var emitter = StartSpawnFixture(battle, json);
            var bullet = (VBullet)emitter.Root.Members.Single();
            VerificationClock.BattleSeconds(battle, 2.1, Vector2.Zero, false);
            Check(bullet.IsAlive && bullet.OutsideAge == 0 && !BattleConfig.GameRegion.Contains(bullet.WorldPosition),
                "显式出界区域独立于游戏区域和反射区域，游戏区外超过2秒可存活");
            bullet.ApplyParameters(new ParameterActionAttribute { X = 1301 });
            VerificationClock.BattleSeconds(battle, 2.0 / 60, Vector2.Zero, false);
            Check(bullet.IsAlive && bullet.OutsideAge > 0, "自定义区域之外按指定阈值累计时间");
            bullet.ApplyParameters(new ParameterActionAttribute { X = 1300 });
            battle.StepFixed(Vector2.Zero, false);
            Check(bullet.IsAlive && bullet.OutsideAge == 0, "回到自定义区域边界按中心清零");
            // 游戏圆内、自定义区域外同样必须释放，不能把两个区域取并集。
            bullet.ApplyParameters(new ParameterActionAttribute { X = 640 });
            VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            Check(!bullet.IsAlive && emitter.Root.Members.Count == 0, "位于游戏区内仍可因离开指定区域而回收");
            world.Free();
        }

        // 代码入口可直接使用接口实例，省略时明确取游戏区域。
        var directBattle = CreateBattle(out var directWorld);
        directBattle.Boss.Stop();
        directBattle.Player.Attack.Stop();
        var shape = new RectangleRegionShape(new Rect2(1100, 300, 200, 200));
        var custom = directBattle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.PlayerSet) with
        {
            Position = new Vector2(1200, 400),
            Speed = 0,
            LifeTimeMs = 5000,
            OutsideRegion = shape
        })!;
        Check(ReferenceEquals(custom.OutsideRegion, shape), "代码出生入口保留指定形状接口");
        var fallback = StartSpawnFixture(directBattle, """
            {"Core":{"Type":"VBullet","LifeTimeMs":5000},"Display":{},
             "BaseAttributes":[{"Speed":0,"RefMoveQueue":[{"Type":"XYMove","X":1200,"Y":400}]}],
             "Timeline":[{"StartMs":0}]}
            """).Root.Members.Single() as VBullet;
        Check(fallback!.OutsideTimeoutMs == 2000 && ReferenceEquals(fallback.OutsideRegion, BattleConfig.GameRegion),
            "省略两个字段的Emitter在运行时使用游戏区域及2秒阈值");
        VerificationClock.BattleSeconds(directBattle, 2, Vector2.Zero, false);
        Check(!fallback.IsAlive && custom.IsAlive, "同一管理器内不同出界区域独立生效");
        directWorld.Free();

        // 字段格式保持严格，不接受反射专属字段或模式混用。
        foreach (string invalid in new[]
        {
            "null", "[]", "{}",
            """{"Type":"Circle","X":0,"Y":0,"Radius":0}""",
            """{"Type":"Circle","X":0,"Y":0,"Radius":"1/0"}""",
            """{"Type":"Circle","X":0,"Y":0,"Radius":100,"Width":10}""",
            """{"Type":"Circle","X":0,"Y":0,"Radius":100,"AngleMin":0}""",
            """{"Type":"Circle","X":0,"Y":0,"Radius":100,"X":1}""",
            """{"Type":"Circle","X":null,"Y":0,"Radius":100}""",
            """{"Type":"Rectangle","X":0,"Y":0,"Width":100}""",
            """{"Type":"Rectangle","X":0,"Y":0,"Width":-1,"Height":100}""",
            """{"Type":"Rectangle","X":0,"Y":0,"Width":100,"Height":100,"Edges":[]}""",
            """{"Type":"Triangle","X":0,"Y":0}"""
        })
        {
            string json = $$$"""
                {"Core":{"Type":"VBullet","OutsideRegion":{{{invalid}}}},"Display":{},"BaseAttributes":[{}]}
                """;
            try { VNodeCreator.FromJson(json); Check(false, "非法出界区域必须加载失败"); }
            catch (JsonException) { Check(true, "非法出界区域在加载阶段拒绝"); }
        }

        // 加载全部正式及示例数据，逐层确认现存普通弹幕已经继承2秒默认值。
        int bullets = 0;
        foreach (string file in System.IO.Directory.GetFiles(ProjectSettings.GlobalizePath("res://Data/Emitters"), "*.json",
            System.IO.SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            var emitter = VBulletEmitter.Load(file);
            var pending = new System.Collections.Generic.Stack<VNodeCreator>();
            pending.Push(emitter.Root);
            while (pending.Count > 0)
            {
                var creator = pending.Pop();
                if (creator is VBulletCreator bulletCreator)
                {
                    Check(bulletCreator.Core.OutsideTimeoutMs == 2000 && bulletCreator.Core.OutsideRegion is null,
                        "现存Emitter普通弹幕默认使用游戏区域且连续出界2秒后消失：" + file);
                    bullets++;
                }
                foreach (var child in creator.Children) pending.Push(child);
            }
        }
        Check(bullets >= 7, "正式Emitter普通弹幕配置已遍历，防止空数据检查误通过");
    }
    /// <summary>验证JSON、代码入口默认值、字段隔离和重现性。</summary>
    private void VerifyRegionData()
    {
        const string fixture = """
            {"Core":{"Type":"VBullet","Reflectable":true,"OutsideTimeoutMs":1500,
              "ReflectionRegion":{"Type":"Circle","X":640,"Y":400,"Radius":400,"AngleMin":"3*PI/2","AngleMax":"PI/2"}},
             "Display":{},"BaseAttributes":[{"Speed":100}],"Timeline":[{"StartMs":0}]}
            """;
        var creator = (VBulletCreator)VNodeCreator.FromJson(fixture);
        Check(creator.Core.OutsideTimeoutMs == 1500 && creator.Core.Reflectable, "新字段通过子弹Core读取");
        foreach (var change in new Action<JsonNode>[]
        {
            data => data["Core"]!["OutsideTimeoutMs"] = -1,
            data => data["Core"]!["OutsideTimeoutMs"] = "2000",
            data => data["Core"]!["OutsideTimeoutMs"] = 0.5,
            data => data["Core"]!.AsObject().Remove("ReflectionRegion"),
            data => data["Core"]!["ReflectionRegion"] = null,
            data => data["Core"]!["ReflectionRegion"]!["Edges"] = new JsonArray("Top"),
            data => data["Core"]!["ReflectionRegion"]!["Radius"] = 0,
            data => data["Core"]!["ReflectionRegion"]!["X"] = null,
            data => data["Core"]!["ReflectionRegion"]!["Type"] = "Unknown",
            data => data["Core"]!["ReflectionRegion"]!["AngleMax"] = "1/0"
        })
        {
            var data = JsonNode.Parse(fixture)!;
            change(data);
            try { VNodeCreator.FromJson(data.ToJsonString()); Check(false, "无效区域数据必须拒绝"); }
            catch (JsonException) { Check(true, "无效区域参数在加载时拒绝"); }
        }
        foreach (string edges in new[] { """["Right","Bottom"]""", "[]" })
        {
            var data = JsonNode.Parse(fixture)!;
            data["Core"]!["ReflectionRegion"] = JsonNode.Parse($$"""
                {"Type":"Rectangle","X":200,"Y":100,"Width":600,"Height":500,"Edges":{{edges}}}
                """);
            Check(VNodeCreator.FromJson(data.ToJsonString()) is VBulletCreator, "矩形JSON支持选边及空边数组");
            data["Core"]!["ReflectionRegion"]!["Edges"] = JsonNode.Parse("""["Right","Right"]""");
            try { VNodeCreator.FromJson(data.ToJsonString()); Check(false, "重复反射边必须拒绝"); }
            catch (JsonException) { Check(true, "重复反射边被拒绝"); }
        }
        // 每次重建完整战斗，固定数据与种子，反射本身不消耗随机。
        var records = new System.Collections.Generic.List<(Vector2 Position, Vector2 Velocity, double Outside)>[2];
        for (int run = 0; run < 2; run++)
        {
            var battle = CreateBattle(out var world);
            battle.Boss.Stop();
            battle.Player.Attack.Stop();
            VMath.setRandomSeed(92);
            double next = VMath.getRandomDouble(0, 1);
            VMath.setRandomSeed(92);
            var bullet = battle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.PlayerSet) with
            {
                Position = new Vector2(400, 400),
                Speed = 1800,
                LifeTimeMs = 10000,
                Reflectable = true,
                ReflectionRegion = new VReflectionRegion(new CircleRegionShape(new Vector2(500, 400), 200))
            })!;
            battle.Timers.AdvanceByUnits(0, dispatchLocal: battle.Bullets.DispatchTimelines);
            records[run] = new();
            for (int tick = 0; tick < 180; tick++)
            {
                battle.StepFixed(Vector2.Zero, false);
                records[run].Add((bullet.WorldPosition, bullet.Velocity, bullet.OutsideAge));
            }
            Check(bullet.IsAlive && VMath.getRandomDouble(0, 1) == next, "反射不消耗战斗随机序列");
            world.Free();
        }
        Check(records[0].SequenceEqual(records[1]), "相同完整初始状态下区域和反射逐固定步重现");
    }
}
