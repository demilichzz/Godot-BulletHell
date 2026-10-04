using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Boss03两种符卡、三阶段切换及确定性的定向验证。</summary>
public partial class BattleVerification
{
    /// <summary>创建实际Boss03配置的隔离战斗并停止玩家自动射击。</summary>
    /// <param name="world">由调用方释放的独立场景。</param>
    /// <returns>只由测试固定步推进的战斗。</returns>
    private BattleManager CreateBoss03Battle(out Node2D world)
    {
        world = new Node2D();
        AddChild(world);
        var battle = new BattleManager();
        world.AddChild(battle);
        battle.Initialize(world, BossCatalog.Load().Get("Boss_03"));
        battle.SetPhysicsProcess(false);
        battle.Player.Attack.Stop();
        return battle;
    }

    /// <summary>检查首轮时序、阶段边界、清理和完整初态重现。</summary>
    private void VerifyBoss03()
    {
        // 一秒前没有弹幕；首轮三个无显示源各生成八条蓝色激光。
        var battle = CreateBoss03Battle(out var world);
        Check(battle.Boss.CurrentPhase is BossPhase { Index: 0 } && battle.Boss.Hp == 300, "Boss03从第一阶段满血开始");
        Check(!battle.Boss.TrySwitchAdjacentPhase(-1), "第一阶段不可向前切换");
        VerificationClock.BattleSeconds(battle, 59.0 / 60, Vector2.Zero, false);
        Check(battle.Bullets.ActiveCount == 0, "首轮在1000ms开始");
        battle.StepFixed(Vector2.Zero, false);
        var lasers = battle.Bullets.ActiveBullets.OfType<VLaser>().ToArray();
        Check(lasers.Length == 24 && lasers.All(laser => laser.Stage == VLaserStage.Warning), "首轮24条激光同时预警");
        Check(lasers.Select(laser => laser.GlobalPosition).Distinct().Count() == 3
            && lasers.All(laser => laser.Settings.Color == "#27BEFF" && laser.Settings.Width == 8), "三处出生参考形成青蓝激光网");
        VerificationClock.BattleSeconds(battle, 0.9, Vector2.Zero, false);
        Check(lasers.All(laser => laser.Stage == VLaserStage.Expand), "预警900ms后开始展开");
        Check(battle.Bullets.ActiveBullets.Any(bullet => bullet is not VLaser && bullet.Radius == 12)
            && battle.Bullets.ActiveBullets.Any(bullet => bullet is not VLaser && bullet.Radius == 5), "网格展开期间已有大小两种圆弹");
        VerificationClock.BattleSeconds(battle, 0.15, Vector2.Zero, false);
        Check(lasers.All(laser => laser.Stage == VLaserStage.Active), "展开150ms后生效");
        VerificationClock.BattleSeconds(battle, 2.3, Vector2.Zero, false);
        Check(lasers.All(laser => laser.Stage == VLaserStage.Fade), "生效2300ms后关闭激光伤害并消退");
        VerificationClock.BattleSeconds(battle, 0.35, Vector2.Zero, false);
        Check(lasers.All(laser => !laser.IsAlive), "激光3700ms寿命结束");
        VerificationClock.BattleSeconds(battle, 1.3, Vector2.Zero, false);
        Check(battle.Bullets.ActiveBullets.OfType<VLaser>().Count() == 24 && battle.Boss.Position == new Vector2(500, 230),
            "6000ms从换位后的Boss快照再发一轮");
        Check(battle.Boss.TrySwitchAdjacentPhase(1) && battle.Boss.CurrentPhase is BossPhase { Index: 1 }
            && battle.Boss.Hp == 200 && battle.Bullets.ActiveCount == 0, "切入阶段2设置血线且清除上一阶段弹幕");
        VerificationClock.BattleSeconds(battle, 7, Vector2.Zero, false);
        Check(battle.Bullets.ActiveCount > 0 && battle.Boss.CurrentPhase!.Emitters.Count == 1
            && battle.Bullets.ActiveBullets.OfType<VLaser>().All(laser => laser.Settings.Color == "#FF3048"), "阶段2开始红色弹带与激光，不残留首阶段发射");
        Check(battle.Boss.TrySwitchAdjacentPhase(1) && battle.Boss.CurrentPhase is BossPhase { Index: 2 }
            && battle.Boss.Hp == 100 && !battle.Boss.TrySwitchAdjacentPhase(1), "阶段3为末阶段且初始血线100");
        Check(battle.Boss.CurrentPhase!.Emitters.Count == 0 && battle.Bullets.ActiveCount == 0, "阶段3保持空阶段并清除第二阶段弹幕");
        Check(battle.Boss.TrySwitchAdjacentPhase(-1) && battle.Boss.TrySwitchAdjacentPhase(-1)
            && battle.Boss.CurrentPhase is BossPhase { Index: 0 } && battle.Boss.Hp == 300, "可回到阶段1并重建发射器");
        battle.Boss.TakeDamage(100);
        Check(battle.Boss.CurrentPhase is BossPhase { Index: 1 }, "累计损失100点自然进入阶段2");
        battle.Boss.TakeDamage(100);
        Check(battle.Boss.CurrentPhase is BossPhase { Index: 2 }, "累计损失200点自然进入阶段3");
        battle.Boss.TakeDamage(100);
        battle.StepFixed(Vector2.Zero, false);
        Check(battle.State == BattleState.Victory && battle.Bullets.ActiveCount == 0, "第三阶段击破后正常结束战斗");
        battle.Restart();
        Check(battle.Boss.CurrentPhase is BossPhase { Index: 0 } && battle.Boss.Hp == 300 && battle.Bullets.ActiveCount == 0,
            "重开回到首阶段完整初态");
        world.Free();
        Check(CaptureBoss03(false).SequenceEqual(CaptureBoss03(true)), "相同完整初态、输入和种子逐步重现，额外渲染刷新不影响业务");
        VerifyBoss03Shield();
    }

    /// <summary>运行三个波次并记录弹幕与玩家状态，覆盖随机角度、碰撞与换位。</summary>
    /// <param name="refresh">是否额外刷新激光显示。</param>
    /// <returns>每半秒状态及最终随机值，用于比较两个独立战斗。</returns>
    private List<string> CaptureBoss03(bool refresh)
    {
        var battle = CreateBoss03Battle(out var world);
        var states = new List<string>();
        int peak = 0;
        // 12秒包含1000、6000和11000ms三次出生；输入均由固定步编号决定。
        for (int tick = 1; tick <= 720; tick++)
        {
            battle.StepFixed(tick % 240 < 120 ? Vector2.Left : Vector2.Right, tick % 120 == 0);
            if (refresh)
                foreach (var laser in battle.Bullets.ActiveBullets.OfType<VLaser>()) laser.RefreshGeometry();
            peak = Math.Max(peak, battle.Bullets.ActiveCount);
            if (tick % 30 == 0)
                states.Add($"{battle.Player.Health.Hp}:{battle.Boss.Position}|" + string.Join(";", battle.Bullets.ActiveBullets.Select(bullet =>
                    $"{bullet.GetType().Name}:{bullet.WorldPosition.X:R},{bullet.WorldPosition.Y:R}:{bullet.Angle:R}:{bullet.Age:R}")));
        }
        Check(peak > 100 && peak < 512, "三轮运行有足够弹幕且远低于2048容量");
        states.Add(VMath.getRandomDouble(0, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        world.Free();
        return states;
    }
}
