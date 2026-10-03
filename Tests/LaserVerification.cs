using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>激光阶段、固定路径、连续碰撞、生命周期与重现性的定向验证。</summary>
public partial class BattleVerification
{
    // 一次出生的激光Creator样例；运行路径全部使用现有PathQueue格式。
    private const string FixedLaserFixture = """
        {"Core":{"Type":"VLaser","Width":20},"BaseAttributes":[{}],
         "Laser":{"Mode":"Fixed","Length":200,"HitWidth":12,
                  "WarningMs":50,"ExpandMs":50,"ActiveMs":100,"FadeMs":50},
         "Timeline":[{"StartMs":0}]}
        """;
    private const string PathLaserFixture = """
        {"Core":{"Type":"VLaser","Width":20},"BaseAttributes":[{}],
         "Laser":{"Mode":"Path","Length":40,"HitWidth":12,
                  "TravelSpeed":120,"EndMs":5000,"PathPointCount":3},
         "PathQueue":[{"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","X":100}]},
                      {"PathMode":"XY","EndMoveQueue":[{"Type":"XYMove","Y":100}]}],
         "Timeline":[{"StartMs":0}]}
        """;

    /// <summary>执行激光定向验证，不运行完整回归。</summary>
    private void VerifyLasers()
    {
        VerifyLaserStages();
        VerifyLaserPaths();
        VerifyAimPlayerLasers();
        VerifyLaserEndExtension();
        VerifyFixedLaserPath();
        VerifyLaserContours();
        VerifyLaserCoreEndpoints();
        VerifyLaserCollisions();
        VerifyLaserLifecycle();
        VerifyLaserValidation();
        VerifyBulletBlendModes();
        Check(CaptureLasers(false).SequenceEqual(CaptureLasers(true)), "激光相同完整初态、种子和输入逐步重现，显示刷新不改变随机");
    }

    /// <summary>验证四阶段精确边界、零时长跳过、显示连续与命中保留。</summary>
    private void VerifyLaserStages()
    {
        // 位于玩家前方的水平激光，在进入Active前不应伤害玩家。
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        var laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Length = 200,
            Width = 20,
            HitWidth = 12,
            WarningMs = 50,
            ExpandMs = 50,
            ActiveMs = 100,
            FadeMs = 50
        }, new Vector2(540, 600))!;
        Check(laser.Stage == VLaserStage.Warning && laser.VisualWidth == 2, "出生显示预警细线");
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        Check(laser.Stage == VLaserStage.Expand && laser.VisualWidth == 2 && battle.Player.Health.Hp == 3, "50ms展开边界连续且无伤害");
        battle.StepFixed(Vector2.Zero, false);
        Check(laser.VisualWidth > 2 && laser.VisualWidth < 20 && battle.Player.Health.Hp == 3, "展开中宽度渐变且无伤害");
        VerificationClock.BattleSeconds(battle, 2.0 / 60, Vector2.Zero, false);
        Check(laser.Stage == VLaserStage.Active && laser.VisualWidth == 20 && battle.Player.Health.Hp == 2, "100ms生效并命中");
        Check(laser.IsAlive && battle.Bullets.ActiveCount == 1, "命中保留整条激光");
        VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
        Check(laser.Stage == VLaserStage.Fade && laser.VisualWidth == 20, "200ms关闭判定，消退初始宽度连续");
        battle.StepFixed(Vector2.Zero, false);
        Check(laser.VisualWidth > 0 && laser.VisualWidth < 20 && battle.Player.Health.Hp == 2, "消退逐渐收束");
        VerificationClock.BattleSeconds(battle, 2.0 / 60, Vector2.Zero, false);
        Check(!laser.IsAlive && battle.Bullets.ActiveCount == 0, "250ms结束释放");
        world.Free();

        battle = CreateBattle(out world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            WarningMs = 0,
            ExpandMs = 0,
            ActiveMs = 2500,
            FadeMs = 0
        }, new Vector2(540, 600))!;
        Check(laser.Stage == VLaserStage.Active, "零时长预警和展开直接跳过");
        VerificationClock.BattleSeconds(battle, 1, Vector2.Zero, false);
        Check(battle.Player.Health.Hp == 2 && laser.IsAlive, "持续接触沿用受击保护，不逐帧扣血");
        VerificationClock.BattleSeconds(battle, 2.0 / 60, Vector2.Zero, false);
        Check(battle.Player.Health.Hp == 1 && laser.IsAlive, "受击保护结束后允许再次受伤，光束不销毁");
        world.Free();
    }

    /// <summary>验证按弧长移动、末段持续外延、到期释放以及禁止外部修改。</summary>
    private void VerifyLaserPaths()
    {
        var battle = CreateBattle(out var world);
        var emitter = StartSpawnFixture(battle, PathLaserFixture);
        var laser = (VLaser)emitter.Bullets.Single();
        Check(laser.PathLength == 200 && laser.HeadDistance == 0 && laser.TailDistance == 0, "路径出生零龄，路径总长200");
        VerificationClock.BattleSeconds(battle, 0.5, Vector2.Zero, false);
        Check(laser.GlobalPosition.DistanceTo(new Vector2(60, 0)) < 0.001 && laser.TailDistance == 20, "头尾按弧长推进");
        VerificationClock.BattleSeconds(battle, 0.5, Vector2.Zero, false);
        Check(laser.GlobalPosition.DistanceTo(new Vector2(100, 20)) < 0.001, "跨直角不切弦");
        // 无论通过父类接口、Creator批量接口还是原生Node2D姿态写入，均不能更改固定路径。
        VNode baseNode = laser;
        foreach (Action action in new Action[]
        {
            () => baseNode.SetDirection(1), () => baseNode.SetSpeed(30),
            () => baseNode.ApplyParameters(new ParameterActionAttribute { X = 500 }),
            () => baseNode.ApplyParameters(new ParameterActionAttribute { LifeTimeMs = 1 }),
            () => emitter.Root.ApplyParameters(new ParameterActionAttribute { Y = 500 })
        })
        {
            try { action(); Check(false, "激光不能接受外部参数修改"); }
            catch (InvalidOperationException) { Check(true, "参数修改被拒绝"); }
        }
        Vector2 saved = laser.GlobalPosition;
        laser.Position += new Vector2(100, 100);
        laser.Rotation = 2;
        laser.Scale = new Vector2(2, 2);
        Check(laser.GlobalPosition.DistanceTo(saved) < 0.001 && laser.GlobalRotation == 0 && laser.GlobalScale == Vector2.One,
            "原生姿态修改立即恢复固定路径");
        VerificationClock.BattleSeconds(battle, 5.0 / 6, Vector2.Zero, false);
        Check(laser.IsAlive && laser.GlobalPosition.DistanceTo(new Vector2(100, 120)) < 0.001 && laser.TailDistance == 180,
            "头部越过终点后尾部继续前进");
        VerificationClock.BattleSeconds(battle, 1.0 / 6, Vector2.Zero, false);
        Check(laser.IsAlive && laser.GlobalPosition.DistanceTo(new Vector2(100, 140)) < 0.001,
            "尾部越过原终点后继续沿末段向下移动");
        Check(laser.Intersects(laser.Timeline!.ElapsedUnits, new Vector2(100, 130), new Vector2(100, 130), 0)
            && !laser.Intersects(laser.Timeline.ElapsedUnits, new Vector2(130, 100), new Vector2(130, 100), 0),
            "延长线碰撞沿最后一段方向，不能沿首段或端点切弦");
        Check(laser.IntersectsRect(new Rect2(99, 125, 2, 2))
            && laser.TryGetNearestHazard(new Vector2(105, 130), out var nearest, out var tangent)
            && nearest.DistanceTo(new Vector2(100, 130)) < 0.001 && tangent == Vector2.Down,
            "矩形查询和AI最近危险点包含末段延长线");
        Check(laser.GetNode<Line2D>("LaserCore").Visible && laser.VisualWidth == 20,
            "越过终点后光束持续显示原宽度");
        VerificationClock.BattleSeconds(battle, 179.0 / 60, Vector2.Zero, false);
        Check(laser.IsAlive && !laser.HasFinished, "EndMs前一个固定步仍存活");
        battle.StepFixed(Vector2.Zero, false);
        Check(!laser.IsAlive && emitter.Bullets.Count == 0 && emitter.Root.Batches.Count == 0,
            "严格在5000ms释放移动激光及批次");
        world.Free();

        battle = CreateBattle(out world);
        emitter = StartSpawnFixture(battle, PathLaserFixture.Replace("\"EndMs\":5000", "\"EndMs\":250"));
        laser = (VLaser)emitter.Bullets.Single();
        VerificationClock.BattleSeconds(battle, 14.0 / 60, Vector2.Zero, false);
        Check(laser.IsAlive && laser.HeadDistance < 40, "短总时长不要求走完整条路径");
        battle.StepFixed(Vector2.Zero, false);
        Check(!laser.IsAlive && Math.Abs(laser.HeadDistance - 30) < 0.001, "250ms立即提前结束");
        world.Free();

        // 路径数组复制与非零世界参考独立；显示和实体坐标不得重复加原点。
        battle = CreateBattle(out world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        var points = new[] { new Vector2(100, 100), new Vector2(200, 100) };
        laser = battle.Bullets.SpawnLaser(new VLaserAttribute { Mode = "Path", EndMs = 1000, TravelSpeed = 60 },
            new Vector2(100, 100), path: points)!;
        points[1] = new Vector2(900, 900);
        VerificationClock.BattleSeconds(battle, 0.5, Vector2.Zero, false);
        Check(laser.GlobalPosition.DistanceTo(new Vector2(130, 100)) < 0.001 && laser.PathLength == 100, "出生复制世界路径，外部数组改写不影响激光");
        world.Free();

        // 对曲线使用独立已知端点和中点期望，路径采样细节另由现有路径测试覆盖。
        foreach (string segment in new[]
        {
            """{"PathMode":"Bezier","EndMoveQueue":[{"Type":"XYMove","X":100}],"ControlPoints":[{"X":50,"Y":100}]}""",
            """{"PathMode":"Function","AxisMode":"Relative","EndMoveQueue":[{"Type":"XYMove","X":100}],"X":"L*t","Y":"50*sin(PI*t)"}"""
        })
        {
            battle = CreateBattle(out world);
            var json = JsonNode.Parse(PathLaserFixture)!;
            json["PathQueue"] = JsonNode.Parse("[" + segment + "]");
            emitter = StartSpawnFixture(battle, json.ToJsonString());
            laser = (VLaser)emitter.Bullets.Single();
            Check(laser.PointAt(0) == Vector2.Zero && laser.PointAt(laser.PathLength) == new Vector2(100, 0), "曲线路径保留原定义端点");
            Check(laser.PointAt(laser.PathLength / 2).DistanceTo(new Vector2(50, 50)) < 0.05, "Bezier/Function沿弧长经过已知中点");
            world.Free();
        }
    }

    /// <summary>验证固定VPath覆盖整条曲线、冻结世界坐标并沿用四阶段及碰撞。</summary>
    private void VerifyFixedLaserPath()
    {
        // 在已知抛物线的中点与弦中点分别检测，避免曲线路径被替换为直线。
        var battle = CreateBattle(out var world);
        var json = JsonNode.Parse(FixedLaserFixture)!;
        json["Core"]!["Width"] = "12*2";
        json["Laser"]!["PathPointCount"] = 129;
        json["PathQueue"] = JsonNode.Parse("""[{"PathMode":"Function","AxisMode":"Relative","EndMoveQueue":[{"Type":"XYMove","X":400}],"X":"L*t","Y":"200*t*(1-t)"}]""");
        var emitter = StartSpawnFixture(battle, json.ToJsonString());
        var laser = (VLaser)emitter.Bullets.Single();
        Check(((VLaserCoreAttribute)emitter.Root.Core).Width == 24 && laser.Settings.Width == 24, "Core.Width表达式传入实际激光");
        Check(laser.Stage == VLaserStage.Warning && laser.PathLength > 400 && laser.HeadDistance == laser.PathLength,
            "固定曲线出生显示完整路径，不按Length截断或按速度生长");
        Check(laser.PointAt(laser.PathLength / 2).DistanceTo(new Vector2(200, 50)) < 0.05, "固定曲线路径经过已知中点");
        Check(!laser.Intersects(0, new Vector2(200, 50), new Vector2(200, 50), 0), "曲线预警没有伤害");
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        Check(laser.Stage == VLaserStage.Expand, "固定曲线进入展开阶段");
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        Check(laser.Stage == VLaserStage.Active && laser.VisualWidth == 24, "固定曲线按Core.Width完整展开");
        Check(laser.Intersects(0, new Vector2(200, 50), new Vector2(200, 50), 0)
            && !laser.Intersects(0, new Vector2(200, 0), new Vector2(200, 0), 0), "固定曲线按折线判定，不切弦");
        laser.Position = new Vector2(123, 456);
        Check(laser.GlobalPosition == Vector2.Zero && laser.PointAt(laser.PathLength / 2).DistanceTo(new Vector2(200, 50)) < 0.05,
            "固定曲线保持出生世界路径");
        VerificationClock.BattleSeconds(battle, 0.1, Vector2.Zero, false);
        Check(laser.Stage == VLaserStage.Fade, "固定曲线按时消退");
        VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
        Check(!laser.IsAlive && emitter.Bullets.Count == 0, "固定曲线四阶段结束后释放");
        world.Free();
    }

    /// <summary>用独立几何期望验证15度尖端、亮芯包围轮廓与短光束退化。</summary>
    private void VerifyLaserContours()
    {
        // 长直线提供可直接测量的端点和宽度，半角7.5度的tan约为0.1316524976。
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        var laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Width = 24,
            Length = 960,
            EndCap = "Point",
            WarningMs = 0,
            ExpandMs = 0
        }, Vector2.Zero)!;
        var body = laser.GetNode<Line2D>("Laser");
        var core = laser.GetNode<Line2D>("LaserCore");
        Check(body.Width == 24 && core.Width == 18, "主体24像素时亮芯18像素");
        Check(Math.Abs(2 * Math.Atan(body.Width * 0.5 / (core.Points[1].X - body.Points[0].X)) - Math.PI / 12) < 0.00001,
            "直线主体尖端完整夹角15度");
        Check(Math.Abs(2 * Math.Atan(core.Width * 0.5 / (core.Points[1].X - core.Points[0].X)) - Math.PI / 12) < 0.00001,
            "直线亮芯尖端完整夹角15度");
        Check(core.Points[0].X == 0 && core.Points[^1].X == 960 && body.Points[0].X < -22 && body.Points[^1].X > 982,
            "主体在亮芯两侧和尖端均有外部轮廓");
        Check(laser.GetChildren().OfType<Line2D>().Count() == 8
            && Math.Abs(laser.GetNode<Line2D>("LaserGlow0").Width - 120) < 0.001,
            "六层外发光达到主体5倍的120像素最外层宽度");
        battle.Bullets.Release(laser);
        // 短光束限制实际宽度，避免两端收束重叠造成角度变钝、亮芯外露或几何倒置。
        laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Width = 24,
            Length = 20,
            EndCap = "Point",
            WarningMs = 0,
            ExpandMs = 0
        }, Vector2.Zero)!;
        body = laser.GetNode<Line2D>("Laser");
        core = laser.GetNode<Line2D>("LaserCore");
        Check(Math.Abs(body.Width - 3.51073327) < 0.00001 && Math.Abs(core.Width - body.Width * 0.75) < 0.00001,
            "短尖角光束按长度限制宽度并保持75%亮芯");
        Check(core.Points[0].X == 0 && core.Points[^1].X == 20 && body.Points[0].X < 0 && body.Points[^1].X > 20,
            "短光束亮芯端点固定且仍被主体包围");
        world.Free();
    }

    /// <summary>逐步检查亮芯端点不受阶段宽度影响，并验证移动模式仍由固定路径窗口定位。</summary>
    private void VerifyLaserCoreEndpoints()
    {
        // 默认JSON必须可直接加载：8像素主体、6像素判定；不显式覆盖宽度。
        var creator = (VLaserCreator)VNodeCreator.FromJson("""{"Core":{"Type":"VLaser"},"BaseAttributes":[{}],"Laser":{},"Timeline":[{"StartMs":0}]}""");
        Check(creator.Laser.Width == 8 && creator.Laser.HitWidth == 6, "省略宽度的JSON使用8像素主体和6像素判定");
        // 直线与曲线用同一时间序列；曲线路径端点不在同一水平线，以检测外延方向。
        foreach (var path in new Vector2[]?[] { null, new[] { new Vector2(100, 100), new Vector2(180, 160), new Vector2(300, 120) } })
        {
            var battle = CreateBattle(out var world);
            battle.Boss.Stop();
            battle.Player.Attack.Stop();
            var laser = battle.Bullets.SpawnLaser(new VLaserAttribute
            {
                Length = 200,
                EndCap = "Point",
                WarningMs = 50,
                ExpandMs = 50,
                ActiveMs = 100,
                FadeMs = 100
            }, new Vector2(100, 100), path: path)!;
            var core = laser.GetNode<Line2D>("LaserCore");
            var body = laser.GetNode<Line2D>("Laser");
            // 固定期望只来自输入路径；记录外延长度用于比较展开与消退。
            Vector2 start = new(100, 100), end = path?[^1] ?? new Vector2(300, 100);
            double warningExtension = 0, activeExtension = 0;
            for (int tick = 0; tick < 18; tick++)
            {
                if (tick > 0) battle.StepFixed(Vector2.Zero, false);
                Check(core.ToGlobal(core.Points[0]).DistanceTo(start) < 0.001
                    && core.ToGlobal(core.Points[^1]).DistanceTo(end) < 0.001,
                    "预警、展开、生效及消退每一步的亮芯端点固定");
                // 端部延伸只改变主体显示，15度轮廓始终包围亮芯端点。
                double extension = body.ToGlobal(body.Points[0]).DistanceTo(start);
                Check(extension > 0 && Math.Abs(core.Width - body.Width * 0.75) < 0.00001,
                    "每阶段主体包围亮芯且亮芯宽度为75%");
                if (tick == 0) warningExtension = extension;
                if (tick == 6)
                {
                    activeExtension = extension;
                    Check(body.Width == 8 && core.Width == 6 && laser.GetNode<Line2D>("LaserGlow0").Width == 40,
                        "完全展开为8像素主体、6像素亮芯和40像素外光");
                    Check(activeExtension > warningExtension, "展开时主体向外延伸，亮芯不缩短");
                }
                if (tick == 17) Check(extension < activeExtension, "消退时主体收束，亮芯不增长或缩短");
            }
            battle.StepFixed(Vector2.Zero, false);
            Check(!laser.IsAlive, "消退结束后完整释放各层");
            world.Free();
        }
        // 移动窗口经过折点与完整路径终点，仍以固定路径上的头尾决定亮芯，不读取主体外延。
        var movingBattle = CreateBattle(out var movingWorld);
        movingBattle.Boss.Stop();
        movingBattle.Player.Attack.Stop();
        var moving = movingBattle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Mode = "Path",
            Length = 80,
            TravelSpeed = 120,
            EndMs = 3000,
            EndCap = "Point"
        }, new Vector2(100, 100), path: new[] { new Vector2(100, 100), new Vector2(200, 140), new Vector2(320, 100) })!;
        var movingCore = moving.GetNode<Line2D>("LaserCore");
        for (int tick = 1; tick <= 150; tick++)
        {
            movingBattle.StepFixed(Vector2.Zero, false);
            if (tick == 1 || tick % 30 == 0)
            {
                // 原PointAt查询仍截断；独立加上最后一段的超程量验证显示延长线。
                Vector2 direction = new Vector2(120, -40).Normalized();
                Vector2 tail = moving.PointAt(moving.TailDistance)
                    + direction * (float)Math.Max(0, moving.TailDistance - moving.PathLength);
                Vector2 head = moving.PointAt(moving.HeadDistance)
                    + direction * (float)Math.Max(0, moving.HeadDistance - moving.PathLength);
                Check(movingCore.ToGlobal(movingCore.Points[0]).DistanceTo(tail) < 0.001
                    && movingCore.ToGlobal(movingCore.Points[^1]).DistanceTo(head) < 0.001,
                    "移动尖角亮芯端点沿窗口及末段延长线持续移动");
            }
        }
        movingWorld.Free();
    }

    /// <summary>验证连续碰撞在同时间检测，避免高速漏判、弯角切弦以及预警追溯命中。</summary>
    private void VerifyLaserCollisions()
    {
        // 简单世界折线提供独立几何期望，不运行渲染或随机。
        var line = new[] { Vector2.Zero, new Vector2(100, 0) };
        var lengths = new[] { 0.0, 100.0 };
        Check(VMath.SweptPolylineWindowHit(line, lengths, new(50, -20), new(50, 20), 100, 100, 100, 0, 1),
            "玩家一步穿过固定细激光也命中");
        Check(VMath.SweptPolylineWindowHit(line, lengths, new(50, 0), new(50, 0), 0, 100, 10, 0, 1),
            "高速短光束在步中经过目标时命中");
        Check(!VMath.SweptPolylineWindowHit(line, lengths, new(50, 0), new(50, 100), 0, 100, 10, 0, 1),
            "先后经过同一位置但时间不同，不按轨迹并集误判");
        Check(!VMath.SweptPolylineWindowHit(line, lengths, new(50, 2), new(50, 2), 100, 100, 100, 0, 1), "判定宽度外安全");
        Check(!VMath.SweptPolylineWindowHit(line, lengths, new(50, 0), new(50, 0), 0, 0, 100, 0, 1), "未长出的光束没有碰撞");
        var corner = new[] { Vector2.Zero, new Vector2(100, 0), new Vector2(100, 100) };
        var distances = new[] { 0.0, 100.0, 200.0 };
        Check(!VMath.SweptPolylineWindowHit(corner, distances, new(95, 5), new(95, 5), 90, 110, 10, 0, 1), "跨路径拐角不切弦误判");
        Check(VMath.SweptPolylineWindowHit(corner, distances, new(100, 5), new(100, 5), 90, 110, 10, 0, 1), "跨角后实际光束可命中");

        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        var laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            WarningMs = 8,
            ExpandMs = 0,
            ActiveMs = 100,
            FadeMs = 0,
            Length = 100,
            HitWidth = 2
        }, Vector2.Zero)!;
        laser.Advance(1.0 / 60);
        Check(!laser.Intersects(0, new Vector2(50, -1), new Vector2(50, 20), 0), "预警期间穿过，生效前离开，不追溯命中");
        Check(laser.Intersects(0, new Vector2(50, 20), new Vector2(50, -1), 0), "同一步生效后穿过可以命中");
        battle.Bullets.Release(laser);
        laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            WarningMs = 0,
            ExpandMs = 0,
            ActiveMs = 8,
            FadeMs = 100,
            Length = 100,
            HitWidth = 2
        }, Vector2.Zero)!;
        laser.Advance(1.0 / 60);
        Check(!laser.Intersects(0, new Vector2(50, 20), new Vector2(50, -1), 0), "消退后才穿过不命中");
        Check(laser.Intersects(0, new Vector2(50, -1), new Vector2(50, 20), 0), "步中消退前的有效碰撞仍被保留");
        battle.Bullets.Release(laser);
        foreach (string cap in new[] { "Point", "Round" })
        {
            laser = battle.Bullets.SpawnLaser(new VLaserAttribute
            {
                EndCap = cap,
                WarningMs = 0,
                ExpandMs = 0,
                Length = 400
            }, Vector2.Zero)!;
            laser.Advance(1.0 / 60);
            Check(laser.Intersects(0, new Vector2(1, 0), new Vector2(1, 0), 0) == (cap == "Round"), "尖端安全区与圆端判定可区分");
            Check(laser.Intersects(0, new Vector2(200, 0), new Vector2(200, 0), 0), "两种端部的中段都可命中");
            battle.Bullets.Release(laser);
        }
        // EndMs落在本步内部时，仅检测结束之前的移动区间。
        laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Mode = "Path",
            EndMs = 8,
            TravelSpeed = 6000,
            Length = 10,
            HitWidth = 2
        }, Vector2.Zero, path: line)!;
        laser.Advance(1.0 / 60);
        Check(laser.Intersects(0, new Vector2(40, 0), new Vector2(40, 0), 0), "路径提前结束前的步中接触保留");
        Check(!laser.Intersects(0, new Vector2(80, 0), new Vector2(80, 0), 0), "路径提前结束后的轨迹不再伤害");
        world.Free();
    }

    /// <summary>验证延迟出生、父节点结束、停止策略、容量与清场。</summary>
    private void VerifyLaserLifecycle()
    {
        foreach (string stop in new[] { "KeepBullets", "ClearBullets" })
        {
            var battle = CreateBattle(out var world);
            var json = JsonNode.Parse(PathLaserFixture)!;
            json["Core"]!["Amount"] = 2;
            json["AddAttributes"] = JsonNode.Parse("""{"SpawnDelayMs":100}""");
            var emitter = StartSpawnFixture(battle, json.ToJsonString(), stop);
            Check(emitter.Root.Members.Count == 1 && emitter.Bullets.Count == 1 && emitter.Nodes.Count == 0, "激光属于子弹视图且延迟对象不占容量");
            emitter.Stop();
            VerificationClock.BattleSeconds(battle, 0.2, Vector2.Zero, false);
            Check(emitter.Bullets.Count == (stop == "KeepBullets" ? 1 : 0), "停止策略适用激光并取消延迟出生");
            battle.Bullets.Clear();
            Check(battle.Bullets.ActiveCount == 0 && emitter.Root.Batches.Count == 0, "清场移除激光批次");
            world.Free();
        }

        var child = JsonNode.Parse(PathLaserFixture)!;
        child["BaseAttributes"] = JsonNode.Parse("""[{"SpawnDelayMs":50}]""");
        string parent = $$"""
            {"Core":{"Type":"VNode","LifeTimeMs":100},"BaseAttributes":[{"Speed":60,
              "RefMoveQueue":[{"Type":"XYMove","X":100}]}],"Timeline":[{"StartMs":0}],"Children":[{{child.ToJsonString()}}]}
            """;
        var parentBattle = CreateBattle(out var parentWorld);
        var parentEmitter = StartSpawnFixture(parentBattle, parent);
        VerificationClock.BattleSeconds(parentBattle, 0.05, Vector2.Zero, false);
        var born = (VLaser)parentEmitter.Bullets.Single();
        Check(born.Age == 0 && born.PointAt(0) == new Vector2(100, 0), "延迟路径出生复用批次Snapshot，未跟随移动父节点");
        VerificationClock.BattleSeconds(parentBattle, 0.1, Vector2.Zero, false);
        Check(parentEmitter.Nodes.Count == 0 && born.IsAlive && born.GlobalPosition.DistanceTo(new Vector2(112, 0)) < 0.001,
            "父到期后保留已生激光及固定路径");
        parentBattle.Restart();
        Check(!born.IsAlive && parentBattle.Bullets.ActiveCount == 0, "重开释放旧激光与旧时间线");
        parentWorld.Free();

        var fullBattle = CreateBattle(out var fullWorld);
        fullBattle.Boss.Stop();
        fullBattle.Player.Attack.Stop();
        // 现有圆点填满容量；新增激光必须沿用满额跳过且不抽业务随机。
        for (int index = 0; index < BattleConfig.MaxBullets; index++)
            fullBattle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.PlayerSet) with { Position = new Vector2(-10000, -10000) });
        VMath.setRandomSeed(923);
        double next = VMath.getRandomDouble(0, 1);
        VMath.setRandomSeed(923);
        var random = JsonNode.Parse(FixedLaserFixture)!;
        random["RandDiffAttributes"] = JsonNode.Parse("""{"Member":{"Angle":1}}""");
        var fullEmitter = StartSpawnFixture(fullBattle, random.ToJsonString());
        Check(fullEmitter.Bullets.Count == 0 && fullBattle.Bullets.ActiveCount == BattleConfig.MaxBullets, "激光与普通弹共享2048容量");
        Check(VMath.getRandomDouble(0, 1) == next, "满额激光不消耗业务随机");
        fullWorld.Free();
    }

    /// <summary>验证专用JSON字段、数值边界与模式冲突在加载时拒绝。</summary>
    private void VerifyLaserValidation()
    {
        // 独立无效输入不应留到正式战斗才报错。
        foreach (string invalid in new[]
        {
            FixedLaserFixture.Replace("\"Width\":20", "\"Width\":20,\"DurationMs\":10"),
            FixedLaserFixture.Replace("\"Width\":20", "\"Width\":0"),
            FixedLaserFixture.Replace("\"Width\":20", "\"Width\":-1"),
            FixedLaserFixture.Replace("\"HitWidth\":12", "\"HitWidth\":12,\"Width\":24"),
            FixedLaserFixture.Replace("\"HitWidth\":12", "\"HitWidth\":12,\"TipLength\":12"),
            """{"Core":{"Type":"VNode","Width":24},"BaseAttributes":[{}]}""",
            """{"Core":{"Type":"VBullet","Width":24},"BaseAttributes":[{}]}""",
            PathLaserFixture.Replace("\"Mode\":\"Path\"", "\"Mode\":\"Fixed\"").Replace("\"EndMs\":5000,", "").Replace("\"BaseAttributes\":[{}]", "\"BaseAttributes\":[{\"Angle\":1}]"),
            FixedLaserFixture.Replace("\"HitWidth\":12", "\"HitWidth\":21"),
            FixedLaserFixture.Replace("\"ActiveMs\":100", "\"ActiveMs\":0"),
            FixedLaserFixture.Replace("\"WarningMs\":50", "\"WarningMs\":-1"),
            FixedLaserFixture.Replace("\"Mode\":\"Fixed\"", "\"Mode\":\"Unknown\""),
            FixedLaserFixture.Replace("\"Mode\":\"Fixed\"", "\"Mode\":\"Fixed\",\"EndCap\":\"Square\""),
            FixedLaserFixture.Replace("\"Mode\":\"Fixed\"", "\"Mode\":\"Fixed\",\"Color\":\"invalid\""),
            FixedLaserFixture.Replace("\"Type\":\"VLaser\"", "\"Type\":\"VLaser\",\"CreatePositionMode\":\"Follow\""),
            FixedLaserFixture.Replace("\"Type\":\"VLaser\"", "\"Type\":\"VLaser\",\"LifeTimeMs\":100"),
            FixedLaserFixture.Replace("\"BaseAttributes\":[{}]", "\"BaseAttributes\":[{\"Speed\":1}]"),
            FixedLaserFixture.Replace("\"BaseAttributes\":[{}]", "\"BaseAttributes\":[{}],\"MemberTimeline\":[{\"StartMs\":0,\"Set\":{\"X\":1}}]"),
            PathLaserFixture.Replace("\"EndMs\":5000", "\"EndMs\":0"),
            PathLaserFixture.Replace("\"EndMs\":5000,", ""),
            PathLaserFixture.Replace("\"EndMs\":5000", "\"EndMs\":5000.5"),
            PathLaserFixture.Replace("\"TravelSpeed\":120", "\"TravelSpeed\":0"),
            PathLaserFixture.Replace("\"PathPointCount\":3", "\"PathPointCount\":2"),
            PathLaserFixture.Replace("\"PathPointCount\":3", "\"PathPointCount\":4097"),
            PathLaserFixture.Replace("\"BaseAttributes\":[{}]", "\"BaseAttributes\":[{\"Angle\":1}]"),
            PathLaserFixture.Replace("\"Mode\":\"Path\"", "\"Mode\":\"Path\",\"Extra\":1"),
            PathLaserFixture.Replace("\"Mode\":\"Path\"", "\"Mode\":\"Path\",\"Mode\":\"Path\"")
        })
        {
            try { VNodeCreator.FromJson(invalid); Check(false, "无效激光JSON未被拒绝"); }
            catch (JsonException) { Check(true, "无效激光JSON被拒绝"); }
        }
    }

    /// <summary>验证普通贴图、定点激光及路径激光的Display混合配置与输入校验。</summary>
    private void VerifyBulletBlendModes()
    {
        // 同一显示字段覆盖三种弹幕，默认Mix不会更改原有贴图颜色与透明规则。
        foreach (string fixture in new[] { FixedLaserFixture, PathLaserFixture,
            """{"Core":{"Type":"VBullet"},"Display":{"TextureName":"Dot","TextureIndex":6},"BaseAttributes":[{}],"Timeline":[{"StartMs":0}]}""" })
        {
            foreach (string? mode in new string?[] { null, "Mix", "Add" })
            {
                var battle = CreateBattle(out var world);
                var definition = JsonNode.Parse(fixture)!;
                if (mode is not null)
                {
                    definition["Display"] ??= new JsonObject();
                    definition["Display"]!["BlendMode"] = mode;
                }
                var emitter = StartSpawnFixture(battle, definition.ToJsonString());
                var bullet = emitter.Bullets.Single();
                Check(mode == "Add" ? bullet.Material is CanvasItemMaterial { BlendMode: CanvasItemMaterial.BlendModeEnum.Add }
                    : bullet.Material is null, "Display.BlendMode传入实体，缺省保留Mix");
                Check(bullet.GetChildren().OfType<CanvasItem>().All(child => child.UseParentMaterial),
                    "贴图或激光全部显示层继承实体混合材质，不残留覆盖层");
                world.Free();
            }
            // 非字符串、未知值与null均在加载时拒绝，不能静默回退。
            foreach (string invalid in new[] { "null", "1", "\"Screen\"" })
            {
                var definition = JsonNode.Parse(fixture)!;
                definition["Display"] ??= new JsonObject();
                definition["Display"]!["BlendMode"] = JsonNode.Parse(invalid);
                try { VNodeCreator.FromJson(definition.ToJsonString()); Check(false, "无效混合模式必须拒绝"); }
                catch (JsonException) { Check(true, "无效混合模式在加载时拒绝"); }
            }
        }
        // 两种激光的CoreAdd均保留色层普通混合，亮芯共享加算材质并高于全部色层。
        foreach (string fixture in new[] { FixedLaserFixture, PathLaserFixture })
        {
            var battle = CreateBattle(out var world);
            var definition = JsonNode.Parse(fixture)!;
            definition["Display"] = JsonNode.Parse("""{"BlendMode":"CoreAdd"}""");
            var emitter = StartSpawnFixture(battle, definition.ToJsonString());
            var laser = (VLaser)emitter.Bullets.Single();
            var core = laser.GetNode<Line2D>("LaserCore");
            Check(laser.Material is null && laser.Settings.BlendMode == "CoreAdd", "CoreAdd主体和外光使用普通混合");
            Check(!core.UseParentMaterial && core.Material is CanvasItemMaterial { BlendMode: CanvasItemMaterial.BlendModeEnum.Add },
                "CoreAdd仅亮芯独立加算");
            Check(laser.GetChildren().OfType<CanvasItem>().Where(child => child != core)
                .All(child => child.UseParentMaterial && child.ZIndex < core.ZIndex), "亮芯排序高于所有主体和外光");
            // 显示刷新和阶段切换不能丢失材质或重置排序。
            for (int step = 0; step < 13; step++) battle.StepFixed(Vector2.Zero, false);
            laser.RefreshGeometry();
            Check(!core.UseParentMaterial && core.ZIndex == 1 && laser.Material is null, "刷新几何保留CoreAdd分层配置");
            world.Free();
        }
        try
        {
            VNodeCreator.FromJson("""{"Core":{"Type":"VBullet"},"Display":{"BlendMode":"CoreAdd"},"BaseAttributes":[{}],"Timeline":[{"StartMs":0}]}""");
            Check(false, "普通子弹不能使用激光专用CoreAdd");
        }
        catch (JsonException) { Check(true, "普通子弹在加载时拒绝CoreAdd"); }
        // 圆点的代码出生入口同样使用根材质，无需单独绘制分支。
        var directBattle = CreateBattle(out var directWorld);
        directBattle.Boss.Stop();
        directBattle.Player.Attack.Stop();
        var dot = directBattle.Bullets.Spawn(VBulletDefaultSet.Get(VBulletType.PlayerSet) with { BlendMode = "Add" });
        Check(dot!.Material is CanvasItemMaterial { BlendMode: CanvasItemMaterial.BlendModeEnum.Add }, "代码生成的圆点也支持Add混合");
        directWorld.Free();
    }

    /// <summary>从完整初态执行相同激光和输入，返回每一步的可比较状态。</summary>
    /// <param name="extraDisplay">是否额外刷新显示，用于验证显示不改变逻辑。</param>
    /// <returns>含阶段、路径进度、伤害与后续随机的逐步快照。</returns>
    private List<string> CaptureLasers(bool extraDisplay)
    {
        var battle = CreateBattle(out var world);
        var fixedJson = JsonNode.Parse(FixedLaserFixture)!;
        fixedJson["Timeline"] = JsonNode.Parse("""[{"StartMs":0,"IntervalMs":100,"EndMs":500}]""");
        fixedJson["RandDiffAttributes"] = JsonNode.Parse("""{"Member":{"Angle":1}}""");
        var fixedEmitter = StartSpawnFixture(battle, fixedJson.ToJsonString());
        var pathEmitter = StartSpawnFixture(battle, PathLaserFixture);
        var states = new List<string>();
        // 300步涵盖多批次交叠、末段外延和路径在5000ms到期。
        for (int step = 0; step < 300; step++)
        {
            battle.StepFixed(step % 60 < 30 ? Vector2.Left : Vector2.Right, step % 60 == 0);
            if (extraDisplay)
                foreach (var laser in battle.Bullets.ActiveBullets.OfType<VLaser>())
                    for (int frame = 0; frame < 3; frame++) laser.RefreshGeometry();
            states.Add(battle.Player.Health.Hp + "|" + string.Join(";", battle.Bullets.ActiveBullets.OfType<VLaser>().Select(laser =>
                $"{laser.BirthIndex}:{laser.Timeline!.ElapsedUnits}:{laser.Stage}:{laser.HeadDistance:R}:{laser.TailDistance:R}:{laser.GlobalPosition.X:R},{laser.GlobalPosition.Y:R}:{laser.VisualWidth:R}")));
        }
        states.Add(VMath.getRandomDouble(0, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Check(fixedEmitter.Bullets.Count == 0 && pathEmitter.Bullets.Count == 0, "重现测试覆盖全部激光释放");
        world.Free();
        return states;
    }
}
