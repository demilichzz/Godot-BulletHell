using Godot;

/// <summary>直接通过循环构造Boss_01的一次24颗等角环形弹幕。</summary>
public sealed class Boss_01Emitter : BulletEmitter
{
    /// <summary>逐颗初始化环形敌弹，不依赖排列模式。</summary>
    /// <param name="manager">统一管理子弹生命周期的容器。</param>
    /// <param name="origin">本批次全局起点，单位为逻辑像素。</param>
    protected override void Build(BulletManager manager, Vector2 origin)
    {
        // 零基索引决定方向；24颗弹的角度间隔为15度，顺时针排列。
        for (int index = 0; index < BattleConfig.RingCount; index++)
        {
            if (AddBullet(manager, BulletSpawnData.ForTeam(BulletTeam.Enemy, index * 360f / BattleConfig.RingCount)
                with { Position = origin }) is null) break;
        }
    }
}
