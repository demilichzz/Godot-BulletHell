using Godot;
using System;
using System.Linq;
using System.Text.Json;

/// <summary>复用Creator批次与出生流程，生成定点或固定路径激光。</summary>
public sealed class VLaserCreator : VNodeCreator
{
    /// <summary>两种模式共用的只读激光定义。</summary>
    public VLaserAttribute Laser { get; private set; } = new();
    /// <summary>激光显示组，仅使用BlendMode，省略时保留普通透明混合。</summary>
    public VBulletDisplayAttribute Display { get; private set; } = new();
    /// <summary>激光只使用出生快照，不随父对象平移。</summary>
    protected override bool DefaultFollow => false;
    // 可选路径定义只计算几何，不生成VNode或额外时间线。
    private VPathCreator? _path;

    /// <summary>建立空激光Creator，使用前通过JSON加载。</summary>
    public VLaserCreator() => Core = new VLaserCoreAttribute { Type = "VLaser" };

    /// <summary>加载专用参数，并通过VPathCreator读取已有路径格式。</summary>
    /// <param name="element">包含Laser及可选PathQueue的Creator对象。</param>
    internal void ReadLaser(JsonElement element)
    {
        // 显式白名单也拒绝DurationMs等仅供运行读取的派生属性。
        var definition = Required(element, "Laser");
        CheckFields(definition, new[] { "Mode", "Length", "HitWidth", "EndCap", "Color", "CoreColor", "GlowColor",
            "WarningMs", "ExpandMs", "ActiveMs", "FadeMs", "EndMs", "TravelSpeed", "PathPointCount" });
        // 激光不使用贴图字段，显式拒绝无效的TextureName或TextureIndex配置。
        if (element.TryGetProperty("Display", out var display))
        {
            CheckFields(display, new[] { "BlendMode" });
            Display = Read<VBulletDisplayAttribute>(display);
        }
        Laser = Read<VLaserAttribute>(definition) with
        {
            Width = ((VLaserCoreAttribute)Core).Width,
            BlendMode = Display.BlendMode
        };
        Laser.Validate();
        // 移动模式必须提供路径；定点可选完整路径，省略时使用直线长度和出生方向。
        if (Laser.Mode == "Path" || element.TryGetProperty("PathQueue", out _))
            _path = VPathCreator.CreateGeometry(Required(element, "PathQueue"), Laser.PathPointCount);
    }

    /// <summary>激光使用固定几何和专用时长，拒绝会改变既定路径或阶段的普通运动参数。</summary>
    protected override void ValidateAttributes()
    {
        if (Core.LifeTimeMs is not null) throw new JsonException("激光寿命使用Laser阶段时长或EndMs，不重复填写Core.LifeTimeMs。");
        if (Follow || MemberTimeline.Count != 0) throw new JsonException("激光只允许Snapshot，且不接受MemberTimeline参数修改。");
        // 无路径的定点允许出生方向及方向随机；提供路径时不再叠加角度旋转。
        foreach (var value in BaseAttributes.Concat(new[] { AddAttributes, RandDiffAttributes.Batch, RandDiffAttributes.Member }))
            if (value.Speed != 0 || value.ASpeed != 0 || value.AAngle != 0 || (_path is not null && value.Angle != 0))
                throw new JsonException("激光不使用普通Speed/AAngle/ASpeed；路径激光也不接受Angle，请使用TravelSpeed与PathQueue。");
        if (_path is not null && Core.AngleMode != "Fixed") throw new JsonException("固定路径不能使用AimPlayer角度。");
        Core = Core with { LifeTimeMs = Laser.DurationMs };
        base.ValidateAttributes();
    }

    /// <summary>在抽样或建立运行对象前检查现有弹幕容量。</summary>
    /// <param name="manager">本场弹幕管理器。</param>
    /// <returns>尚有一个实体容量时为真。</returns>
    protected override bool CanCreate(VBulletManager manager) => manager.CanSpawn();

    /// <summary>冻结本颗激光的世界路径，经管理器统一登记。</summary>
    /// <param name="emitter">提供阵营、伤害和生命周期的发射器。</param>
    /// <param name="manager">本场弹幕容器。</param>
    /// <param name="position">按现有Snapshot与出生位移求出的世界参考点，像素。</param>
    /// <param name="move">出生方向；普通速度和加速度已经拒绝。</param>
    /// <returns>实际激光；满额时为空。</returns>
    protected override VNode? CreateMember(VBulletEmitter emitter, VBulletManager manager, Vector2 position, VNodeMoveAttribute move)
    {
        // 逐颗冻结；路径端点的世界目标含义由VPath保持，后续父位置不会参与计算。
        var points = _path?.SampleGeometry(position).Select(offset => position + offset).ToArray();
        return manager.SpawnLaser(Laser, position, move.Angle, points, emitter.Core.Damage, emitter);
    }
}
