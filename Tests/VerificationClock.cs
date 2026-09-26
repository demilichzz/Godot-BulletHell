using Godot;
using System;

/// <summary>为隔离验证提供固定战斗步及仅推进指定实体年龄的辅助。</summary>
public static class VerificationClock
{
    /// <summary>重复固定物理步，不向正式战斗传入任意步长。</summary>
    /// <param name="battle">待验证战斗。</param>
    /// <param name="seconds">60Hz步长整数倍的非负秒数。</param>
    /// <param name="movement">固定屏幕方向，右下为正。</param>
    /// <param name="dodge">仅第一步提交闪避按下。</param>
    public static void BattleSeconds(BattleManager battle, double seconds, Vector2 movement, bool dodge)
    {
        // 验证辅助不允许通过舍入掩盖非固定步输入。
        int steps = checked((int)Math.Round(seconds * 60));
        if (Math.Abs(steps - seconds * 60) > 1e-8) throw new ArgumentException("测试时间须为固定步整数倍。");
        for (int index = 0; index < steps; index++) battle.StepFixed(movement, dodge && index == 0);
    }
    /// <summary>隔离推进Boss移动、活动子弹时间线及所属战斗时钟，不执行子弹运动。</summary>
    /// <param name="battle">拥有Boss、弹幕与共享计时器的战斗。</param>
    /// <param name="seconds">非负秒数，按内部1/60000秒时间精度换算。</param>
    public static void BossSeconds(BattleManager battle, double seconds)
    {
        AdvanceEntities(battle, seconds, Array.Empty<BulletEmitter>());
    }
    /// <summary>隔离推进未绑定阶段的发射器及现存子弹时间线。</summary>
    /// <param name="battle">拥有处理器与子弹的战斗。</param>
    /// <param name="seconds">非负逻辑秒数。</param>
    /// <param name="emitters">此次需要推进的独立发射器。</param>
    public static void EmitterSeconds(BattleManager battle, double seconds, params BulletEmitter[] emitters)
    {
        AdvanceEntities(battle, seconds, emitters);
    }

    /// <summary>将隔离推进切为不超过一个固定步的片段，保证步中新生节点从正确时刻计龄。</summary>
    /// <param name="battle">所属战斗。</param>
    /// <param name="seconds">非负秒数，最后片段保留原边界精度。</param>
    /// <param name="emitters">额外推进的未绑定阶段发射器。</param>
    private static void AdvanceEntities(BattleManager battle, double seconds, BulletEmitter[] emitters)
    {
        // 零龄动作先按现有边界语义派发，子弹仍只计龄而不运动。
        long remaining = VTimeline.SecondsToUnits(seconds);
        battle.Timers.AdvanceByUnits(0);
        while (remaining > 0)
        {
            long units = Math.Min(remaining, VTimerProcessor.FixedStepUnits);
            battle.Timers.AdvanceByUnits(units, elapsed =>
            {
                battle.Boss.Advance(elapsed);
                foreach (var emitter in emitters) emitter.AdvanceUnits(units);
                foreach (var bullet in battle.Bullets.ActiveBullets) bullet.Timeline?.AdvanceUnits(units);
            });
            remaining -= units;
        }
    }
}
