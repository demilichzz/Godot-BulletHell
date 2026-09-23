using System;

/// <summary>管理可降至负数的玩家生命及由玩家时间线结束的受击无敌。</summary>
public sealed class PlayerHealth
{
    // 当前玩家、保护结束时的年龄及受击保护状态。
    private PlayerController _owner = null!;
    private long _invulnerabilityEndUnits;
    private bool _protected;
    /// <summary>当前生命点数，允许为负；达到整数下界后保持下界。</summary>
    public int Hp { get; private set; } = BattleConfig.PlayerHp;
    /// <summary>剩余受击无敌时间，单位为秒。</summary>
    public double Invulnerability
    {
        get
        {
            long now = _owner?.Timeline?.ElapsedUnits ?? 0;
            return Math.Max(0, _invulnerabilityEndUnits - now) / (double)VTimerProcessor.UnitsPerSecond;
        }
    }
    /// <summary>生命变化通知，参数为剩余点数。</summary>
    public event Action<int>? HealthChanged;

    /// <summary>绑定持有活动时间线的玩家。</summary>
    /// <param name="owner">本场玩家。</param>
    public void Initialize(PlayerController owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        if (owner.Timeline is null) throw new InvalidOperationException("玩家时间线尚未启动。");
    }

    /// <summary>有效受伤时扣血，并在时间线上登记保护结束动作。</summary>
    /// <param name="damage">正整数伤害，非正数忽略。</param>
    /// <param name="dodging">当前是否处于闪避状态。</param>
    /// <returns>本次是否实际造成伤害。</returns>
    public bool TakeDamage(int damage, bool dodging)
    {
        if (damage <= 0 || dodging || _protected) return false;
        var timeline = _owner.Timeline ?? throw new InvalidOperationException("玩家时间线尚未启动。");
        Hp = (int)Math.Max(int.MinValue, (long)Hp - damage);
        _protected = true;
        long durationMs = VTimerProcessor.SecondsToMilliseconds(BattleConfig.HurtInvulnerability);
        _invulnerabilityEndUnits = checked(timeline.ElapsedUnits + durationMs * VTimerProcessor.UnitsPerMillisecond);
        timeline.After(durationMs, () => _protected = false);
        HealthChanged?.Invoke(Hp);
        return true;
    }
}
