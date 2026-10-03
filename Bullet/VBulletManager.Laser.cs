using Godot;
using System;
using System.Collections.Generic;

/// <summary>激光登记与碰撞分支；普通子弹保持原有更新和命中释放流程。</summary>
public partial class VBulletManager
{
    /// <summary>创建一条敌方激光，仍使用现有容量、出生时间线和清理入口。</summary>
    /// <param name="settings">定点与路径共用定义，时间为整数毫秒。</param>
    /// <param name="position">定点起点或路径参考点，世界逻辑像素。</param>
    /// <param name="angleRadians">无路径的定点方向弧度，0向右、顺时针为正；提供路径时为0。</param>
    /// <param name="path">完整世界折线，至少两个点；定点可选、移动必填，调用时复制。</param>
    /// <param name="damage">正整数伤害，默认1。</param>
    /// <returns>已登记的激光；满额时为空。</returns>
    public VLaser? SpawnLaser(VLaserAttribute settings, Vector2 position, double angleRadians = 0,
        IReadOnlyList<Vector2>? path = null, int damage = 1)
        => SpawnLaser(settings, position, angleRadians, path, damage, null);

    /// <summary>Creator专用登记入口，保存所属Emitter以沿用停止和清场规则。</summary>
    /// <param name="settings">共用激光定义。</param>
    /// <param name="position">世界参考点，逻辑像素。</param>
    /// <param name="angleRadians">无路径的定点方向弧度，提供路径时必须为0。</param>
    /// <param name="path">完整固定世界路径；无路径的定点为null。</param>
    /// <param name="damage">正整数伤害。</param>
    /// <param name="emitter">所属敌方Emitter，直接创建时为null。</param>
    /// <returns>登记结果或满额时的null。</returns>
    internal VLaser? SpawnLaser(VLaserAttribute settings, Vector2 position, double angleRadians,
        IReadOnlyList<Vector2>? path, int damage, VBulletEmitter? emitter)
    {
        if (emitter is not null && emitter.Core.Team != VBulletTeam.Enemy)
            throw new ArgumentException("当前激光用于敌方弹幕，不定义玩家持续攻击伤害频率。", nameof(emitter));
        // 先完整校验再交给场景；运行路径始终复制，调用方不能事后改形。
        var points = VLaser.Validate(settings, position, angleRadians, path, damage);
        if (!CanSpawn()) return null;
        var laser = new VLaser();
        try
        {
            laser.ConfigureLaser(settings, position, angleRadians, points, damage);
            Register(laser, emitter);
            return laser;
        }
        catch
        {
            laser.Free();
            throw;
        }
    }

    /// <summary>推进激光自身年龄，检查同时间的玩家轨迹；命中不释放激光。</summary>
    /// <param name="laser">仍存活且已完成零龄动作的激光。</param>
    /// <param name="delta">本次逻辑秒数，正式战斗为1/60。</param>
    /// <param name="player">伤害目标。</param>
    /// <param name="ai">可选陪练AI，不改变原玩家激光判定。</param>
    private void AdvanceLaser(VLaser laser, double delta, PlayerController player, AICharacter? ai)
    {
        // 使用时间线整数年龄，避免毫秒阶段边界受浮点累计影响。
        long previous = laser.Timeline!.ElapsedUnits;
        laser.Advance(delta);
        laser.RefreshGeometry();
        if (ai is not null && laser.Intersects(previous, ai.PreviousPosition, ai.GlobalPosition, BattleConfig.PlayerRadius))
            ai.TakeHit(laser.Damage);
        if (laser.Intersects(previous, player.PreviousPosition, player.GlobalPosition, BattleConfig.PlayerRadius))
            player.Health.TakeDamage(laser.Damage, player.Dodge.IsActive);
        if (laser.Expired || laser.HasFinished) Release(laser);
    }
}
