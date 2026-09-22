using Godot;
using System;

/// <summary>为隔离验证提供固定战斗步及事件分段推进辅助。</summary>
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
    /// <summary>隔离推进Boss时钟及移动，不更新子弹或玩家。</summary>
    /// <param name="boss">拥有真实弹幕容器的Boss。</param>
    /// <param name="seconds">非负秒数，按内部1/60000秒时间精度换算。</param>
    public static void BossSeconds(BossController boss, double seconds)
    {
        // 隔离验证使用处理器推进，Boss本身不再维护周期时钟。
        var manager = (BulletManager)boss.BulletParent;
        manager.Timers.AdvanceByUnits(checked((long)Math.Round(seconds * VTimerProcessor.UnitsPerSecond)), boss.Advance);
    }
}
