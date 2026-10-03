using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Boss03第二阶段的反向旋转复制、两类激光及长期运行验证。</summary>
public partial class BattleVerification
{
    /// <summary>核对用户调整后的弹带、激光快照及时间边界、阶段取消与30秒确定性。</summary>
    private void VerifyBoss03Shield()
    {
        // 首轮几何验证时将玩家置于底部，前几个出生轮次不与弹带碰撞。
        var battle = CreateBoss03Battle(out var world);
        battle.Boss.TrySwitchAdjacentPhase(1);
        battle.Player.Position = new Vector2(640, 790);
        VerificationClock.BattleSeconds(battle, 59.0 / 60, Vector2.Zero, false);
        Check(battle.Bullets.ActiveCount == 0, "第二阶段1000ms前没有弹幕");
        battle.StepFixed(Vector2.Zero, false);
        var emitter = battle.Boss.CurrentPhase!.Emitters.Single();
        var clockwise = (VBulletCreator)emitter.GetCreator("ShieldCW")!;
        var counter = (VBulletCreator)emitter.GetCreator("ShieldCW_copy_CCW")!;
        Check(battle.Bullets.ActiveCount == 12 && clockwise.Members.Count == 6 && counter.Members.Count == 6,
            "第二阶段首发两个方向各六颗，按轮出生而非预先创建全部成员");
        Check(clockwise.TotalAmount == 144 && counter.TotalAmount == 144
            && counter.AddAttributes.SpawnDelayMs == 50 && counter.Core.LifeTimeMs == 6000,
            "复制继承数量、逐轮延迟及寿命");
        Check(counter.BaseAttributes.Select(item => item.Speed).SequenceEqual(new double[] {120,120,120,150,150,150})
            && counter.Display.TextureName == "Dot" && counter.Display.TextureIndex == 0
            && counter.Core.Radius == 3 && counter.Core.VisualScale == 3, "复制组保留用户当前的速度、Dot贴图和显示倍率");
        // 当前数据共用同一组基础角，仅逐轮增量反向，不再要求旧版竖直镜像。
        for (int round = 0; round < 5; round++)
        {
            if (round > 0) VerificationClock.BattleSeconds(battle, 0.05, Vector2.Zero, false);
            Check(clockwise.Members.Count == (round + 1) * 6 && counter.Members.Count == clockwise.Members.Count,
                "50ms边界双组同步出生");
            double positive = VMath.StandardizationAngle(Math.PI / 2 + Math.PI / 16 + round * Math.Tau / 24);
            double negative = VMath.StandardizationAngle(Math.PI / 2 + Math.PI / 16 - round * Math.Tau / 24);
            Check(Math.Abs(clockwise.Members[round * 6].Angle - positive) < 1e-10
                && Math.Abs(counter.Members[round * 6].Angle - negative) < 1e-10, "当前基础角保持不变，逐轮分别顺逆时针推进");
            Check(Math.Abs(clockwise.Members[round * 6].WorldPosition.DistanceTo(battle.Boss.Position) - 150) < 0.001
                && Math.Abs(counter.Members[round * 6].WorldPosition.DistanceTo(battle.Boss.Position) - 150) < 0.001,
                "两组新生弹幕保留用户设置的150像素出生半径");
        }
        VerificationClock.BattleSeconds(battle, 1.3, Vector2.Zero, false);
        var lasers = battle.Bullets.ActiveBullets.OfType<VLaser>().Where(laser => laser.Settings.Mode == "Fixed").ToArray();
        var target = battle.Player.GlobalPosition;
        // 新激光使用独立Creator，旧的左右预警支路不变。
        var movingCreator = emitter.GetCreator("ShieldMovingLasers")!;
        var moving = (VLaser)movingCreator.Members.Single();
        Check(moving.PointAt(0) == battle.Boss.Position && moving.PointAt(moving.PathLength) == target
            && moving.Settings.Mode == "Path" && moving.Settings.Length == 180 && moving.Settings.TravelSpeed == 360,
            "2500ms新增从Boss到当前玩家位置的直线移动激光");
        Check(lasers.Length == 2 && lasers.All(laser => laser.Stage == VLaserStage.Warning), "2500ms生成两条预警激光");
        Check(lasers.Select(laser => laser.WorldPosition.X).Order().SequenceEqual(new float[] {460,820})
            && lasers.All(laser => laser.WorldPosition.Y == 240 && laser.Settings.Length == 1100
                && laser.Settings.Width == 8 && laser.Settings.HitWidth == 4), "激光由左右独立参考点发射且使用指定宽度");
        var angles = lasers.Select(laser => laser.Angle).ToArray();
        Check(lasers.All(laser => Math.Abs(laser.Angle - VMath.GetAngleBetween2Points(laser.WorldPosition, target)) < 1e-10),
            "瞄准从各自出生参考点计算，而非统一从Boss中心计算");
        VerificationClock.BattleSeconds(battle, 0.8, Vector2.Right, false);
        Check(lasers.All(laser => laser.Stage == VLaserStage.Expand) && lasers.Select(laser => laser.Angle).SequenceEqual(angles),
            "800ms预警结束且移动玩家不改变已冻结方向");
        Check(moving.PointAt(moving.PathLength) == target && Math.Abs(moving.HeadDistance - 288) < 0.001,
            "玩家移动后已有路径仍固定，光束按360像素每秒前进");
        VerificationClock.BattleSeconds(battle, 0.15, Vector2.Zero, false);
        Check(lasers.All(laser => laser.Stage == VLaserStage.Active), "150ms展开后激光生效");
        VerificationClock.BattleSeconds(battle, 0.45, Vector2.Zero, false);

        Check(lasers.All(laser => laser.Stage == VLaserStage.Fade), "450ms生效结束后消退");
        VerificationClock.BattleSeconds(battle, 0.25, Vector2.Zero, false);
        Check(movingCreator.Members.Count == 2
            && ((VLaser)movingCreator.Members.Last()).PointAt(((VLaser)movingCreator.Members.Last()).PathLength) == battle.Player.GlobalPosition,
            "4000ms周期生成第二条路径，重新取得玩家快照");
        Check(lasers.All(laser => !laser.IsAlive), "250ms消退后回收激光");
        VerificationClock.BattleSeconds(battle, 1.35, Vector2.Zero, false);
        Check(battle.Bullets.ActiveBullets.OfType<VLaser>().Count(laser => laser.Settings.Mode == "Fixed") == 2, "5500ms开始下一次预警");
        battle.Boss.TrySwitchAdjacentPhase(1);
        Check(battle.Bullets.ActiveCount == 0 && clockwise.Members.Count == 0 && counter.Members.Count == 0
            && clockwise.Batches.Count == 0 && counter.Batches.Count == 0 && movingCreator.Batches.Count == 0, "切阶段清除来源及副本批次");
        VerificationClock.BattleSeconds(battle, 7, Vector2.Zero, false);
        Check(battle.Bullets.ActiveCount == 0, "切阶段取消所有复制组的延迟与周期出生");
        world.Free();
        Check(CaptureShield(false).SequenceEqual(CaptureShield(true)), "第二阶段30秒相同完整初态重现，渲染刷新不影响结果");
    }

    /// <summary>执行30秒正式固定步，记录子弹状态、容量峰值与随机尾值。</summary>
    /// <param name="refresh">是否额外更新激光显示。</param>
    /// <returns>包含玩家、Boss、子弹和随机状态的记录。</returns>
    private List<string> CaptureShield(bool refresh)
    {
        // 两次独立初始化具有相同随机、玩家保护及阶段状态。
        var battle = CreateBoss03Battle(out var world);
        battle.Boss.TrySwitchAdjacentPhase(1);
        var states = new List<string>();
        int peak = 0;
        for (int tick = 1; tick <= 1800; tick++)
        {
            battle.StepFixed(tick % 360 < 180 ? Vector2.Left : Vector2.Right, tick % 120 == 0);
            if (refresh)
                foreach (var laser in battle.Bullets.ActiveBullets.OfType<VLaser>()) laser.RefreshGeometry();
            peak = Math.Max(peak, battle.Bullets.ActiveCount);
            if (tick % 30 == 0)
                states.Add($"{battle.Player.Health.Hp}:{battle.Boss.Position}|" + string.Join(";", battle.Bullets.ActiveBullets.Select(bullet =>
                    $"{bullet.Creator!.Core.Id}:{bullet.BirthIndex}:{bullet.WorldPosition.X:R},{bullet.WorldPosition.Y:R}:{bullet.Angle:R}:{bullet.Age:R}")));
        }
        Check(peak > 100 && peak < 512 && peak < BattleConfig.MaxBullets, "第二阶段30秒负载保持在理论上界及2048容量之内");
        GD.Print($"Boss03 phase02 30s peak: {peak}");
        states.Add(VMath.getRandomDouble(0, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        world.Free();
        return states;
    }
}
