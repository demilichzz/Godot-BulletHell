using Godot;
using System;

/// <summary>使用玩家时间线定期瞄准 Boss 并直接生成单颗子弹。</summary>
public sealed class PlayerAttack
{
    // 自动攻击周期，单位为整数毫秒及内部逻辑时间单位。
    private const long AttackIntervalMs = 200;
    private const long AttackIntervalUnits = AttackIntervalMs * VTimerProcessor.UnitsPerMillisecond;
    // 弹幕模板、绑定对象及永久周期的当前原定触发时刻。
    private readonly BulletDefaultSet _template = BulletDefaultSet.Get(BulletType.PlayerSet);
    private PlayerController? _owner;
    private BulletManager? _bullets;
    private long _nextScheduledUnits, _nextEligibleUnits;
    private bool _scheduled, _active;

    /// <summary>启动自动攻击；同一玩家重复初始化时复用既有周期。</summary>
    /// <param name="owner">持有活动时间线的玩家，射击位置取其当前全局坐标。</param>
    /// <param name="bullets">本场战斗的弹幕容器。</param>
    public void Initialize(PlayerController owner, BulletManager bullets)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(bullets);
        var timeline = owner.Timeline ?? throw new InvalidOperationException("玩家时间线尚未启动。");
        if (_scheduled && !ReferenceEquals(_owner, owner))
            throw new InvalidOperationException("攻击组件不能换绑到其他玩家。");
        _owner = owner;
        _bullets = bullets;
        _nextEligibleUnits = checked(timeline.ElapsedUnits + AttackIntervalUnits);
        _active = true;
        if (_scheduled) return;
        // 首次注册可发生在任意步；整数毫秒格点向上取整。
        long startMs = checked(timeline.ElapsedUnits / VTimerProcessor.UnitsPerMillisecond
            + (timeline.ElapsedUnits % VTimerProcessor.UnitsPerMillisecond == 0 ? 0 : 1)
            + AttackIntervalMs);
        _nextScheduledUnits = checked(startMs * VTimerProcessor.UnitsPerMillisecond);
        _scheduled = true;
        timeline.Repeat(startMs, AttackIntervalMs, null, () =>
        {
            // 大步长补发时玩家年龄已是步末，须用本次动作的原定时刻判断重新启动边界。
            long due = _nextScheduledUnits;
            _nextScheduledUnits = checked(due + AttackIntervalUnits);
            if (!_active || due < _nextEligibleUnits) return;
            var boss = GlobalEvent.GetBoss();
            if (!GodotObject.IsInstanceValid(boss) || boss.IsQueuedForDeletion() || !boss.IsInsideTree() || boss.Hp == 0) return;
            var origin = _owner!.GlobalPosition;
            var angle = VMath.GetAngleBetween2Points(origin, boss.GlobalPosition);
            _bullets!.Spawn(_template with { Position = origin, AngleRadians = angle });
        });
    }

    /// <summary>立即停止射击；空周期随玩家时间线在战斗结束时清理。</summary>
    public void Stop() => _active = false;
}
