using Godot;
using System;
using System.Collections.Generic;

/// <summary>沿Boss下方斜线依次生成六圈敌弹，并为每圈设置延迟追踪。</summary>
public sealed class B01P03_Emitter01 : BulletEmitter
{
    // 每次发射的圆心与每圈子弹数。
    private const int CenterCount = 6, RingCount = 12;
    // 本批次参数、固定的圆心、接收容器和下一圈索引。
    private readonly BulletDefaultSet _template;
    private readonly Vector2[] _centers = new Vector2[CenterCount];
    private BulletManager? _manager;
    private int _nextRing;

    /// <summary>保存阶段提供的弹幕参数快照。</summary>
    /// <param name="template">完整子弹参数；为空时使用ScaleSet。</param>
    public B01P03_Emitter01(BulletDefaultSet? template = null)
    {
        _template = template ?? BulletDefaultSet.Get(BulletType.ScaleSet);
    }

    /// <summary>固定斜线上的六个等距圆心，并立即生成第一圈。</summary>
    /// <param name="manager">接收子弹的当前战斗容器。</param>
    /// <param name="origin">发射时Boss的全局位置，单位为逻辑像素。</param>
    protected override void Build(BulletManager manager, Vector2 origin)
    {
        // 沿用现有斜线端点，只把取点间距改为包含两个端点。
        Vector2 source = VMath.PolarMove(origin, Mathf.Pi * 5 / 6, 400);
        Vector2 target = VMath.PolarMove(origin, Mathf.Pi * 11 / 6, 400);
        target.Y += 300;
        double distance = VMath.GetDistanceBetween2Points(source, target);
        double angle = VMath.GetAngleBetween2Points(source, target);
        for (int index = 0; index < CenterCount; index++)
            _centers[index] = VMath.PolarMove(source, angle, distance * index / (CenterCount - 1));
        _manager = manager;
        EmitNextRing();
    }

    /// <summary>在预定圆心生成下一圈，并只给该圈成功生成的子弹设置转向。</summary>
    internal void EmitNextRing()
    {
        if (_manager is null || _nextRing >= CenterCount) return;
        Vector2 center = _centers[_nextRing++];
        var ringBullets = new List<Bullet>(RingCount);
        for (int index = 0; index < RingCount; index++)
        {
            double angle = index * Math.Tau / RingCount;
            Bullet? bullet = AddBullet(_manager, _template with
            {
                Position = VMath.PolarMove(center, angle, 70),
                AngleRadians = (float)angle,
                ColorIndex = 2,
                VisualScale = 2,
                Radius = 4,
                Speed = 0,
                LifetimeSeconds = 10
            });
            if (bullet is null) break;
            ringBullets.Add(bullet);
        }
        if (ringBullets.Count > 0)
            GlobalEvent.RegisterTimer(new VTimer(2000, 0, 0, VTimerType.Once, ringBullets, aliveTargets =>
            {
                foreach (Node2D target in aliveTargets)
                    if (target is Bullet bullet)
                    {
                        bullet.SetDirection(VMath.StandardizationAngleFloat(
                            VMath.GetAngleBetween2Points(bullet.GlobalPosition, GlobalEvent.GetPlayer().GlobalPosition)));
                        bullet.SetSpeed(150);
                    }
            }));
    }
}
