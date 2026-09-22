using Godot;
using System;

/// <summary>管理可降至负数的玩家生命及由战斗计时器结束的受击无敌。</summary>
public sealed class PlayerHealth
{
    // 所属玩家、共享时钟、无敌句柄与有效标志。
    private Node2D _owner = null!;
    private VTimerProcessor _timers = null!;
    private VTimer? _invulnerability;
    private bool _protected;
    /// <summary>当前生命点数，允许为负；达到int下界后保持下界以避免回绕。</summary>
    public int Hp { get; private set; } = BattleConfig.PlayerHp;
    /// <summary>剩余受击无敌秒数，供显示读取。</summary>
    public double Invulnerability => (_invulnerability?.RemainingMs ?? 0) / 1000;
    /// <summary>生命变化通知，参数为剩余点数。</summary>
    public event Action<int>? HealthChanged;
    /// <summary>绑定玩家及战斗计时器。</summary>
    /// <param name="owner">玩家节点。</param>
    /// <param name="timers">共享战斗处理器。</param>
    public void Initialize(Node2D owner, VTimerProcessor timers)
    {
        _owner = owner; _timers = timers;
    }
    /// <summary>造成有效伤害时减少生命并注册无敌结束计时器，零血及负血继续战斗。</summary>
    /// <param name="damage">正整数伤害点数，非正数忽略。</param>
    /// <param name="dodging">当前是否处于闪避无敌。</param>
    /// <returns>是否造成伤害。</returns>
    public bool TakeDamage(int damage, bool dodging)
    {
        if (damage <= 0 || dodging || _protected) return false;
        // 先用64位减法，避免极端负血量回绕为正数；生命不再触发死亡注销。
        Hp = (int)Math.Max(int.MinValue, (long)Hp - damage);
        _protected = true;
        _invulnerability = _timers.Register(new VTimer(VTimerProcessor.SecondsToMilliseconds(BattleConfig.HurtInvulnerability),
            0, 0, VTimerType.Once, new[] { _owner }, _ => _protected = false));
        HealthChanged?.Invoke(Hp);
        return true;
    }
}
