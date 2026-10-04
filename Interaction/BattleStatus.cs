/// <summary>HUD只读状态接口，不允许视图修改战斗对象。</summary>
public interface IBattleStatusSource
{
    /// <summary>捕获当前显示数据，不推进模拟或消耗随机。</summary>
    /// <returns>与战斗实体脱离的值快照。</returns>
    BattleStatus CaptureStatus();
}

/// <summary>战斗显示数据，不含任何可修改的实体引用。</summary>
/// <param name="State">当前战斗状态。</param>
/// <param name="PlayerHp">玩家当前生命。</param>
/// <param name="PlayerMaxHp">玩家显示生命上限。</param>
/// <param name="BossHp">Boss当前生命。</param>
/// <param name="BossMaxHp">Boss生命上限。</param>
/// <param name="PhaseIndex">阶段零基下标。</param>
/// <param name="PhaseCount">阶段总数。</param>
/// <param name="PhaseHp">阶段当前血量。</param>
/// <param name="PhaseMaxHp">阶段血量上限。</param>
/// <param name="PhaseName">阶段名称或结束提示。</param>
/// <param name="DodgeCooldown">闪避剩余冷却秒数。</param>
/// <param name="Elapsed">战斗逻辑秒数。</param>
/// <param name="AIHitCount">AI受击次数，无AI时为空。</param>
public readonly record struct BattleStatus(BattleState State, int PlayerHp, int PlayerMaxHp, int BossHp,
    int BossMaxHp, int PhaseIndex, int PhaseCount, int PhaseHp, int PhaseMaxHp, string PhaseName,
    double DodgeCooldown, double Elapsed, long? AIHitCount);
