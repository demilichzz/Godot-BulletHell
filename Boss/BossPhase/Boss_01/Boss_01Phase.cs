using Godot;
using System;

/// <summary>调度Boss_01每秒一次的24颗环形发射批次。</summary>
public sealed class Boss_01Phase : BossPhase
{
    // 距离下一次发射的累计秒数，保留大步长余量。
    private double _elapsed;
    /// <summary>当前阶段显示名称。</summary>
    public override string Name => "环形弹幕";
    /// <summary>进入阶段时重置发射计时和批次索引。</summary>
    /// <param name="boss">所属Boss。</param>
    public override void Enter(BossController boss)
    {
        base.Enter(boss);
        _elapsed = 0;
    }
    /// <summary>按秒推进时序，每次到期建立一个全新发射批次。</summary>
    /// <param name="boss">提供全局起点和弹幕管理器的Boss。</param>
    /// <param name="delta">非负经过秒数。</param>
    public override void Advance(BossController boss, double delta)
    {
        PruneEmitters();
        _elapsed += delta;
        while (_elapsed + 1e-9 >= BattleConfig.BossInterval)
        {
            _elapsed = Math.Max(0, _elapsed - BattleConfig.BossInterval);
            // 一次发射对应一个独立对象，阶段持有仍有活动弹的批次。
            var emitter = new Boss_01Emitter();
            emitter.Emit((BulletManager)boss.BulletParent, boss.GlobalPosition);
            TrackEmitter(emitter);
        }
    }
}
