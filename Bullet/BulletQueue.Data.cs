using Godot;
using System;
using System.Text.Json;

/// <summary>读取子弹队列的显示数据并执行子弹专用出生校验。</summary>
public sealed partial class BulletQueue
{
    /// <summary>读取独立子弹队列JSON，生成周期由父对象绑定。</summary>
    /// <param name="path">JSON资源路径。</param>
    /// <returns>已校验的子弹模板。</returns>
    public new static BulletQueue Load(string path) => FromJson(ReadFile(path), path);

    /// <summary>读取格式版本2的子弹队列，拒绝旧版队列阵营和伤害。</summary>
    /// <param name="json">JSON文本。</param>
    /// <param name="sourceName">错误来源名称。</param>
    /// <returns>直接持有属性对象的子弹队列。</returns>
    public new static BulletQueue FromJson(string json, string sourceName = "内存")
        => Parse(json, sourceName, element =>
        {
            var queue = (BulletQueue)ReadQueue(element, true);
            queue.ValidateSchedules(1);
            return queue;
        });

    /// <summary>读取子弹专用显示组。</summary>
    /// <param name="element">Display对象。</param>
    internal void ReadDisplay(JsonElement element) => Display = Read<BulletDisplayAttribute>(element);

    /// <summary>先校验公共属性，再建立子弹出生参数模板。</summary>
    protected override void ValidateAttributes()
    {
        base.ValidateAttributes();
        if (!Core.LifeTimeS.HasValue) throw new JsonException("子弹LifeTimeS必须为有限正数。");
        // 贴图映射沿用项目预设，不扫描文件系统。
        BulletType type = Display.TextureName switch
        {
            "Scale" => BulletType.ScaleSet,
            "Dot" => BulletType.DotSet,
            "Drop" => BulletType.DropSet,
            "Star" => BulletType.StarSet,
            _ => throw new JsonException("Display.TextureName未知。")
        };
        _template = BulletDefaultSet.Get(type) with
        {
            ColorIndex = Display.TextureIndex,
            LifetimeSeconds = Core.LifeTimeS.Value,
            Radius = Core.Radius,
            VisualScale = Core.VisualScale,
            AngleRadians = BaseAttributes.Angle,
            Speed = BaseAttributes.Speed,
            AAngle = BaseAttributes.AAngle,
            ASpeed = BaseAttributes.ASpeed,
            AAngleIsSameAsAngle = Core.AAngleIsSameAsAngle
        };
        Bullet.Validate(_template);
    }
}
