using Godot;
using System;

/// <summary>路径终点后的高速碰撞、短段尖角及时间边界验证。</summary>
public partial class BattleVerification
{
    /// <summary>检查一次固定步越过整条路径后仍保留末段扫掠，且结束后不产生危险区域。</summary>
    private void VerifyLaserEndExtension()
    {
        var battle = CreateBattle(out var world);
        battle.Boss.Stop();
        battle.Player.Attack.Stop();
        // 同一步先经过3像素水平段，再沿4像素竖直段及延长线前进。
        var laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Mode = "Path",
            EndMs = 8,
            TravelSpeed = 6000,
            Length = 10,
            HitWidth = 2
        }, Vector2.Zero, path: new[] { Vector2.Zero, new Vector2(3, 0), new Vector2(3, 4), new Vector2(3, 4) })!;
        laser.Advance(1.0 / 60);
        Check(laser.Intersects(0, new Vector2(3, 30), new Vector2(3, 30), 0),
            "一个固定步跨过整条短路径，末段外延仍连续命中");
        Check(!laser.Intersects(0, new Vector2(30, 3), new Vector2(30, 3), 0)
            && !laser.Intersects(0, new Vector2(3, 80), new Vector2(3, 80), 0),
            "不向错误方向外延，也不检测EndMs以后的行程");
        Check(laser.HasFinished && !laser.IntersectsRect(new Rect2(0, 20, 10, 20))
            && !laser.TryGetNearestHazard(new Vector2(3, 30), out _, out _),
            "到期后矩形与AI查询不再返回危险区域");
        battle.Bullets.Release(laser);

        // 原路径短于尖端收束距离，外延后仍应成长为完整光束并参与判定。
        laser = battle.Bullets.SpawnLaser(new VLaserAttribute
        {
            Mode = "Path",
            EndMs = 1000,
            TravelSpeed = 600,
            Length = 180,
            EndCap = "Point",
            Width = 8,
            HitWidth = 4
        }, new Vector2(300, 200), path: new[] { new Vector2(300, 200), new Vector2(303, 204) })!;
        VerificationClock.BattleSeconds(battle, 0.5, Vector2.Zero, false);
        Check(laser.WorldPosition.DistanceTo(new Vector2(480, 440)) < 0.001 && laser.VisualWidth == 8,
            "短斜线路径之后保持3比4方向并成长至完整宽度");
        Check(laser.Intersects(laser.Timeline!.ElapsedUnits, new Vector2(426, 368), new Vector2(426, 368), 0)
            && laser.IntersectsRect(new Rect2(425, 367, 2, 2)), "短尖角路径外延后的中段可命中及查询");
        Check(!laser.Intersects(laser.Timeline.ElapsedUnits, new Vector2(480, 440), new Vector2(480, 440), 0),
            "末段外延仍保留尖端安全裁剪");
        VerificationClock.BattleSeconds(battle, 0.5, Vector2.Zero, false);
        Check(!laser.IsAlive && battle.Bullets.ActiveCount == 0, "斜向延长光束在配置到期时释放");
        world.Free();
    }
}