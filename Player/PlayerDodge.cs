using Godot;
using System;

/// <summary>用玩家时间线管理闪避方向、持续时间与冷却。</summary>
public sealed class PlayerDodge
{
    // 当前玩家及本次闪避的结束年龄，单位为整数逻辑时间单位。
    private PlayerController _owner = null!;
    private long _durationEndUnits, _cooldownEndUnits;
    /// <summary>剩余闪避时间，单位为秒。</summary>
    public double Remaining => RemainingSeconds(_durationEndUnits);
    /// <summary>剩余冷却时间，单位为秒。</summary>
    public double Cooldown => RemainingSeconds(_cooldownEndUnits);
    /// <summary>是否仍处于闪避状态，直到步末结束动作执行。</summary>
    public bool IsActive { get; private set; }
    /// <summary>锁定的屏幕单位方向，默认向上。</summary>
    public Vector2 Direction { get; private set; } = Vector2.Up;

    /// <summary>绑定持有活动时间线的玩家。</summary>
    /// <param name="owner">本场玩家。</param>
    public void Initialize(PlayerController owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        if (owner.Timeline is null) throw new InvalidOperationException("玩家时间线尚未启动。");
    }

    /// <summary>冷却结束后启动闪避并登记持续时间结束动作。</summary>
    /// <param name="direction">屏幕方向，右下为正；零向量按向上。</param>
    /// <returns>成功启动时为真。</returns>
    public bool TryStart(Vector2 direction)
    {
        var timeline = _owner.Timeline ?? throw new InvalidOperationException("玩家时间线尚未启动。");
        long now = timeline.ElapsedUnits;
        if (now < _cooldownEndUnits) return false;
        Direction = direction.IsZeroApprox() ? Vector2.Up : direction.Normalized();
        IsActive = true;
        long durationMs = VTimerProcessor.SecondsToMilliseconds(BattleConfig.DodgeDuration);
        long cooldownMs = VTimerProcessor.SecondsToMilliseconds(BattleConfig.DodgeCooldown);
        _durationEndUnits = checked(now + durationMs * VTimerProcessor.UnitsPerMillisecond);
        _cooldownEndUnits = checked(now + cooldownMs * VTimerProcessor.UnitsPerMillisecond);
        timeline.After(durationMs, () => IsActive = false);
        return true;
    }

    /// <summary>计算指定结束年龄前的剩余秒数。</summary>
    /// <param name="endUnits">结束时的玩家年龄，单位为整数逻辑时间单位。</param>
    /// <returns>非负剩余秒数。</returns>
    private double RemainingSeconds(long endUnits)
    {
        long now = _owner?.Timeline?.ElapsedUnits ?? 0;
        return Math.Max(0, endUnits - now) / (double)VTimerProcessor.UnitsPerSecond;
    }
}
