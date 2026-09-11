using Godot;
using System;

/// <summary>周期性瞄准Boss当前中心，每次建立独立的单发批次。</summary>
public sealed class PlayerAttack
{
    // 已累计的攻击秒数，首次等待0.2秒后发射。
    private double _elapsed;
    /// <summary>推进自动攻击时序并构造单发批次。</summary>
    /// <param name="delta">经过的非负秒数。</param>
    /// <param name="bullets">战场弹幕管理器。</param>
    /// <param name="origin">玩家全局中心，单位为逻辑像素。</param>
    /// <param name="boss">当前目标，死亡后停止发射。</param>
    public void Advance(double delta, BulletManager bullets, Vector2 origin, BossController boss)
    {
        if (boss.Hp == 0) return;
        _elapsed += delta;
        while (_elapsed + 1e-9 >= BattleConfig.PlayerInterval)
        {
            _elapsed = Math.Max(0, _elapsed - BattleConfig.PlayerInterval);
            // 本次瞄准角度以度表示，0向右、90向下。
            var angle = Mathf.RadToDeg((boss.GlobalPosition - origin).Angle());
            var emitter = new SingleBulletEmitter(BulletSpawnData.ForTeam(BulletTeam.Player, angle));
            emitter.Emit(bullets, origin);
        }
    }
}
