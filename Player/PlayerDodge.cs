using Godot;
using System;

/// <summary>使用战斗计时器管理闪避方向、持续时间与冷却。</summary>
public sealed class PlayerDodge
{
    // 所属玩家及两份独立结束句柄。
    private Node2D _owner = null!;
    private VTimer? _duration, _cooldown;
    /// <summary>剩余闪避时间，秒，由计时器查询。</summary>
    public double Remaining => (_duration?.RemainingMs ?? 0) / 1000;
    /// <summary>剩余冷却时间，秒，由计时器查询。</summary>
    public double Cooldown => (_cooldown?.RemainingMs ?? 0) / 1000;
    /// <summary>当前是否闪避无敌，直到结束回调执行。</summary>
    public bool IsActive { get; private set; }
    /// <summary>锁定的屏幕单位方向，默认向上。</summary>
    public Vector2 Direction { get; private set; } = Vector2.Up;
    /// <summary>绑定战斗实体及共享逻辑时钟。</summary>
    /// <param name="owner">存活玩家节点。</param>
    public void Initialize(Node2D owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }
    /// <summary>冷却结束时启动闪避和两份单次计时器。</summary>
    /// <param name="direction">屏幕方向，右下为正；零向量按向上。</param>
    /// <returns>是否成功启动。</returns>
    public bool TryStart(Vector2 direction)
    {
        if (_cooldown?.State == VTimerState.Running) return false;
        Direction = direction.IsZeroApprox() ? Vector2.Up : direction.Normalized();
        IsActive = true;
        _duration = GlobalEvent.RegisterTimer(new VTimer(VTimerProcessor.SecondsToMilliseconds(BattleConfig.DodgeDuration),
            0, 0, VTimerType.Once, new[] { _owner }, _ => IsActive = false));
        _cooldown = GlobalEvent.RegisterTimer(new VTimer(VTimerProcessor.SecondsToMilliseconds(BattleConfig.DodgeCooldown),
            0, 0, VTimerType.Once, new[] { _owner }, _ => { }));
        return true;
    }
}
