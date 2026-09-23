using Godot;
using System;

/// <summary>沿Boss下方斜线定时生成六圈敌弹，并为每颗子弹设置延迟追踪。</summary>
public sealed class B01P03_Emitter01 : BulletEmitter
{
    /// <summary>首圈等待一秒，每两秒启动一轮，其余五圈按200毫秒间隔生成。</summary>
    /// <param name="manager">接收子弹的当前战斗容器。</param>
    /// <param name="owner">提供每轮发射起点的Boss。</param>
    protected override void Build(BulletManager manager, BossController owner)
    {
        // 本轮模板和时间定义只存在于Build作用域。
        var template = BulletDefaultSet.Get(BulletType.ScaleSet);
        Timeline!.Repeat(1000, 2000, null, () =>
        {
            // 每轮在事件时刻固定起点及六个斜线圆心。
            Vector2 origin = owner.GlobalPosition;
            Vector2 source = VMath.PolarMove(origin, Math.PI * 5 / 6, 400);
            Vector2 target = VMath.PolarMove(origin, Math.PI * 11 / 6, 400);
            target.Y += 300;
            double distance = VMath.GetDistanceBetween2Points(source, target);
            double lineAngle = VMath.GetAngleBetween2Points(source, target);
            var centers = new Vector2[6];
            for (int index = 0; index < centers.Length; index++)
                centers[index] = VMath.PolarMove(source, lineAngle, distance * index / (centers.Length - 1));

            // 本轮局部动作负责在指定圆心生成一圈。
            Action<int> emitRing = ringIndex =>
            {
                Vector2 center = centers[ringIndex];
                for (int index = 0; index < 12; index++)
                {
                    double angle = index * Math.Tau / 12;
                    Bullet? bullet = AddBullet(manager, template with
                    {
                        Position = VMath.PolarMove(center, angle, 70),
                        AngleRadians = angle,
                        ColorIndex = 2,
                        VisualScale = 2,
                        Radius = 4,
                        Speed = 0,
                        LifetimeSeconds = 10
                    });
                    if (bullet is null) break;
                    // 转向随实际出生的子弹继续生效，不受阶段退出影响。
                    bullet.Timeline!.After(2000, () =>
                    {
                        bullet.SetDirection(VMath.GetAngleBetween2Points(
                            bullet.GlobalPosition, GlobalEvent.GetPlayer().GlobalPosition));
                        bullet.SetSpeed(150);
                    });
                }
            };

            emitRing(0);
            for (int ring = 1; ring < centers.Length; ring++)
            {
                int ringIndex = ring;
                Timeline.After(200 * ringIndex, () => emitRing(ringIndex));
            }
        });
    }
}
