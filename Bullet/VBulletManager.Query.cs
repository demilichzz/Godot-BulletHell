using Godot;
using System;
using System.Collections.Generic;

/// <summary>按本容器登记顺序查询活动弹幕，复用形状与调用方结果列表。</summary>
public partial class VBulletManager
{
    // 查询形状只在尺寸变化时重建；平移圆心后查询，移动感知框无需逐步分配。
    private RectangleRegionShape _queryRectangle = new(new Rect2(0, 0, 200, 200));

    /// <summary>查询是否仍有指定Emitter的子弹，命中即停止，不统计全场或维护第二份所属列表。</summary>
    /// <param name="emitter">按引用身份判断的发射器，包括通过代码直接关联的子弹。</param>
    /// <returns>全场唯一活动列表中仍有所属子弹时为真。</returns>
    private bool HasEmitterBullets(VBulletEmitter emitter)
    {
        // 活动表立即移除已注销对象，查询不依赖Creator批次，也不改变登记顺序。
        foreach (var bullet in _active)
            if (ReferenceEquals(bullet.Emitter, emitter)) return true;
        return false;
    }

    /// <summary>按登记顺序收集覆盖闭矩形的活动弹幕，复用并清空结果列表。</summary>
    /// <param name="rectangle">世界逻辑像素矩形，尺寸非负，接触边界也包含。</param>
    /// <param name="results">调用方持有的可复用结果列表，不可为空。</param>
    /// <param name="team">可选阵营，默认null包含所有阵营；激光包含预警但排除消退。</param>
    public void CollectBulletsInRect(Rect2 rectangle, List<VBullet> results, VBulletTeam? team = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        VMath.ValidateQueryRectangle(rectangle);
        // 校验完成后才清空调用方结果，活动顺序来自本容器。
        var bullets = ActiveBullets;
        // 区域接口要求正尺寸；原工具允许退化矩形，兼容分支继续使用数学函数。
        bool hasArea = rectangle.Size.X > 0 && rectangle.Size.Y > 0;
        if (hasArea && _queryRectangle.Bounds.Size != rectangle.Size)
            _queryRectangle = new RectangleRegionShape(new Rect2(Vector2.Zero, rectangle.Size));
        IRegionShape region = _queryRectangle;
        results.Clear();
        for (int index = 0; index < bullets.Count; index++)
        {
            // Follow对象使用逻辑世界位置，不依赖显示变换是否已经刷新。
            var bullet = bullets[index];
            if (!bullet.IsAlive || bullet.PendingBirth || (team.HasValue && bullet.Team != team.Value)) continue;
            if (bullet is VLaser laser ? laser.IntersectsRect(rectangle)
                : hasArea ? region.IntersectsCircle(bullet.WorldPosition - rectangle.Position, bullet.Radius)
                : VMath.CircleIntersectsRect(bullet.WorldPosition, bullet.Radius, rectangle)) results.Add(bullet);
        }
    }

}
