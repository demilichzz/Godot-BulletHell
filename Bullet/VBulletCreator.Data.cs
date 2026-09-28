using Godot;
using System;
using System.Text.Json;

/// <summary>读取子弹队列的显示数据并执行子弹专用出生校验。</summary>
public sealed partial class VBulletCreator
{
    /// <summary>读取独立子弹队列JSON，生成周期由父对象绑定。</summary>
    /// <param name="path">JSON资源路径。</param>
    /// <returns>已校验的子弹模板。</returns>
    public new static VBulletCreator Load(string path) => FromJson(ReadFile(path), path);

    /// <summary>读取当前子弹生成器结构，不接受旧字段。</summary>
    /// <param name="json">JSON文本。</param>
    /// <param name="sourceName">错误来源名称。</param>
    /// <returns>直接持有属性对象的子弹队列。</returns>
    public new static VBulletCreator FromJson(string json, string sourceName = "内存")
        => Parse(json, sourceName, element =>
        {
            if (ReadCreator(element) is not VBulletCreator queue) throw new JsonException("需要VBullet类型根。");
            queue.InitializeTree();
            return queue;
        });

    /// <summary>读取子弹专用显示组。</summary>
    /// <param name="element">Display对象。</param>
    internal void ReadDisplay(JsonElement element) => Display = Read<VBulletDisplayAttribute>(element);

    /// <summary>先校验公共属性，再建立子弹出生参数模板。</summary>
    protected override void ValidateAttributes()
    {
        base.ValidateAttributes();
        if (!Core.LifeTimeMs.HasValue) throw new JsonException("子弹LifeTimeMs必须为有限正数。");
        // 贴图映射沿用项目预设，不扫描文件系统。
        VBulletType type = Display.TextureName switch
        {
            "Scale" => VBulletType.ScaleSet,
            "Dot" => VBulletType.DotSet,
            "Drop" => VBulletType.DropSet,
            "Star" => VBulletType.StarSet,
            _ => throw new JsonException("Display.TextureName未知。")
        };
        _template = VBulletDefaultSet.Get(type) with
        {
            ColorIndex = Display.TextureIndex,
            LifeTimeMs = Core.LifeTimeMs.Value,
            Radius = Core.Radius,
            VisualScale = Core.VisualScale,
            AngleRadians = BaseAttributes[0].Angle,
            Speed = BaseAttributes[0].Speed,
            AAngle = BaseAttributes[0].AAngle,
            ASpeed = BaseAttributes[0].ASpeed,
            AAngleIsSameAsAngle = Core.AAngleIsSameAsAngle
        };
        VBullet.Validate(_template);
    }
}
