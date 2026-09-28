using Godot;
using System.Collections.Generic;

/// <summary>共用Creator树节点，专门创建具有显示和碰撞的VBullet。</summary>
public sealed partial class VBulletCreator : VNodeCreator
{
    /// <summary>子弹专用核心参数。</summary>
    public new VBulletCoreAttribute Core => (VBulletCoreAttribute)base.Core;
    /// <summary>出生时使用的显示参数。</summary>
    public VBulletDisplayAttribute Display { get; private set; } = new();
    /// <summary>共享批次来源的子弹视图，不复制列表。</summary>
    public IReadOnlyList<VBullet> Bullets { get; }
    /// <summary>子弹默认采用出生快照。</summary>
    protected override bool DefaultFollow => false;
    // 校验后的不可变出生参数，不包含运行成员。
    private VBulletDefaultSet? _template;
    /// <summary>建立子弹默认配置和成员投影视图。</summary>
    public VBulletCreator()
    {
        base.Core = new VBulletCoreAttribute { Type = "VBullet" };
        BaseAttributes = System.Array.AsReadOnly(new[] { new VNodeSpawnAttribute { Speed = 180 } });
        Bullets = new MemberView<VBullet>(() => EnumerateMembers(false));
    }

    /// <summary>检查子弹容量，节点基类不使用此限制。</summary>
    /// <param name="manager">当前子弹管理器。</param>
    /// <returns>容量足够时为真。</returns>
    protected override bool CanCreate(VBulletManager manager) => manager.CanSpawn();

    /// <summary>将共用求值结果转换为实际子弹出生参数。</summary>
    /// <param name="emitter">提供共通阵营和伤害的发射器。</param>
    /// <param name="manager">接收子弹的管理器。</param>
    /// <param name="position">全局逻辑像素位置。</param>
    /// <param name="move">已求值的运动参数。</param>
    /// <returns>成功生成的子弹，容量不足时为空。</returns>
    protected override VNode? CreateMember(VBulletEmitter emitter, VBulletManager manager, Vector2 position, VNodeMoveAttribute move)
    {
        var settings = _template! with
        {
            Position = position,
            AngleRadians = move.Angle,
            Speed = move.Speed,
            AAngle = move.AAngle,
            ASpeed = move.ASpeed,
            Team = emitter.Core.Team,
            Damage = emitter.Core.Damage
        };
        return emitter.AddCreatorBullet(manager, settings);
    }

}
