using Godot;

/// <summary>世界坐标中的闭合活动区域；边界视为区域内，距离单位为逻辑像素。</summary>
public interface IRegionShape
{
    /// <summary>判断中心点或完整判定圆是否位于区域内。</summary>
    /// <param name="center">有限世界中心，逻辑像素。</param>
    /// <param name="radius">非负判定半径；默认0表示仅判断中心点。</param>
    /// <returns>完整包含判定圆时为真。</returns>
    bool Contains(Vector2 center, double radius = 0);

    /// <summary>判断区域是否与对象判定圆相交，接触边界也算相交。</summary>
    /// <param name="center">有限世界中心，逻辑像素。</param>
    /// <param name="radius">非负判定半径，逻辑像素。</param>
    /// <returns>存在共同点时为真。</returns>
    bool IntersectsCircle(Vector2 center, double radius);

    /// <summary>限制中心位置，使判定圆完整位于区域内。</summary>
    /// <param name="center">待限制的有限世界中心，逻辑像素。</param>
    /// <param name="radius">非负判定半径，默认0；大于区域容纳能力时报错。</param>
    /// <returns>区域内最近的合法中心位置。</returns>
    Vector2 Clamp(Vector2 center, double radius = 0);

    /// <summary>求从区域内向外移动时第一次离开边界的位置，按对象中心判断。</summary>
    /// <param name="start">有限世界起点，逻辑像素；起点在外时不产生反射接触。</param>
    /// <param name="end">有限预测世界终点，逻辑像素。</param>
    /// <param name="exit">命中时的交点、外法线、时间比例及矩形边标记。</param>
    /// <returns>由内向外穿越时为真；完全位于内部或入射进入区域时为假。</returns>
    bool TryGetExit(Vector2 start, Vector2 end, out RegionExit exit);
}
