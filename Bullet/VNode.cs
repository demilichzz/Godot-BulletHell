using Godot;
using System;
using System.Collections.Generic;

/// <summary>无显示、无碰撞的移动点，提供子弹共用的运动、引用和时间线状态。</summary>
public partial class VNode : Node2D, IVTimelineOwner
{
    /// <summary>出生时创建、注销时取消的实体时间线。</summary>
    public VTimeline? Timeline { get; internal set; }
    /// <summary>外部方向弧度，0向右且顺时针为正。</summary>
    public double AngleRadians { get; private set; }
    /// <summary>外部方向的统一接口，单位弧度。</summary>
    public double Angle => AngleRadians;
    /// <summary>外部有符号速度，逻辑像素每秒。</summary>
    public double Speed { get; private set; }
    /// <summary>加速度方向，弧度。</summary>
    public double AAngle { get; private set; }
    /// <summary>有符号加速度，逻辑像素每平方秒。</summary>
    public double ASpeed { get; private set; }
    /// <summary>是否沿外部Angle施加加速度。</summary>
    public bool AAngleIsSameAsAngle { get; private set; }
    /// <summary>总寿命秒数，持久节点为正无穷。</summary>
    public double LifetimeSeconds { get; private set; } = double.PositiveInfinity;
    /// <summary>已存活秒数，与自身时间线同龄。</summary>
    public double Age { get; private set; }
    /// <summary>是否已达到总寿命上限。</summary>
    public bool Expired => Age + 1e-9 >= LifetimeSeconds;
    /// <summary>是否仍为有效运行对象。</summary>
    public bool IsAlive { get; private set; } = true;
    /// <summary>实际速度向量，逻辑像素每秒，不包含父对象平移速度。</summary>
    public Vector2 Velocity => _velocity;
    /// <summary>出生位置，登记后为物理容器局部逻辑像素。</summary>
    public Vector2 SpawnPosition { get; internal set; }
    /// <summary>运动策略，默认按实际速度直线位移。</summary>
    public BulletBehavior Behavior { get; private set; } = new StraightBehavior();
    /// <summary>逻辑父节点，仅用于树生命周期，不表示场景父节点。</summary>
    public VNode? ParentVNode { get; internal set; }
    /// <summary>所在批次的固定零基出生索引，成员移除不改变此值。</summary>
    public int BirthIndex { get; internal set; }
    /// <summary>是否使用持续平移跟随。</summary>
    public bool IsFollowing => _reference is not null;
    /// <summary>读取时解析参考链，立即反映父节点的参数修改。</summary>
    public Vector2 WorldPosition => _reference is null ? GlobalPosition : ReferencePosition(_reference) + _offset;
    // 运行对象所属发射器和生成批次。
    internal BulletEmitter? Emitter;
    internal VNodeQueue? Queue;
    // 实际速度与最后一次非零方向，不反写外部参数。
    private Vector2 _velocity;
    private double _actualAngle;
    // 独立于场景树的平移引用、局部偏移和直接跟随者。
    private Node2D? _reference;
    private Vector2 _offset;
    private readonly List<VNode> _followers = new();

    /// <summary>初始化公共运动状态，不创建显示或碰撞。</summary>
    /// <param name="position">登记前的全局逻辑像素位置。</param>
    /// <param name="move">外部运动参数，弧度和逻辑像素每秒。</param>
    /// <param name="lifeTimeS">正数寿命秒数；null为持久节点。</param>
    /// <param name="sameAngle">是否沿外部Angle加速。</param>
    /// <param name="behavior">可选无状态运动策略，默认直线。</param>
    internal void ConfigureMotion(Vector2 position, BulletMoveAttribute move, double? lifeTimeS, bool sameAngle, BulletBehavior? behavior = null)
    {
        if (IsInsideTree()) throw new InvalidOperationException("对象必须在入树前初始化。");
        ValidateMotion(position, move, lifeTimeS);
        SpawnPosition = Position = position;
        LifetimeSeconds = lifeTimeS ?? double.PositiveInfinity;
        Age = 0;
        AngleRadians = VMath.StandardizationAngle(move.Angle);
        Speed = move.Speed;
        AAngle = VMath.StandardizationAngle(move.AAngle);
        ASpeed = move.ASpeed;
        AAngleIsSameAsAngle = sameAngle;
        Behavior = behavior ?? new StraightBehavior();
        _actualAngle = AngleRadians;
        RebuildVelocity();
    }

    /// <summary>验证两种运行对象共用的完整运动参数。</summary>
    /// <param name="position">有限全局逻辑像素坐标。</param>
    /// <param name="move">弧度方向及有符号速度、加速度。</param>
    /// <param name="lifeTimeS">正数有限秒数或持久节点的null。</param>
    internal static void ValidateMotion(Vector2 position, BulletMoveAttribute move, double? lifeTimeS)
    {
        ArgumentNullException.ThrowIfNull(move);
        if (!position.IsFinite() || !double.IsFinite(move.Angle) || !double.IsFinite(move.AAngle)
            || !double.IsFinite(move.Speed) || Math.Abs(move.Speed) > float.MaxValue
            || !double.IsFinite(move.ASpeed) || Math.Abs(move.ASpeed) > float.MaxValue
            || (lifeTimeS.HasValue && (!double.IsFinite(lifeTimeS.Value) || lifeTimeS <= 0)))
            throw new ArgumentOutOfRangeException(nameof(move), "公共运动参数无效。");
    }

    /// <summary>绑定逻辑参考，Snapshot仅保留生命周期父引用。</summary>
    /// <param name="reference">用于平移跟随的节点或Boss，null为世界原点。</param>
    /// <param name="parent">负责生命周期的父VNode，可为空。</param>
    /// <param name="follow">是否持续跟随平移。</param>
    internal void BindReference(Node2D? reference, VNode? parent, bool follow)
    {
        ParentVNode = parent;
        if (!follow || reference is null) return;
        _reference = reference;
        _offset = GlobalPosition - ReferencePosition(reference);
        if (reference is VNode node) node._followers.Add(this);
    }

    /// <summary>脱离空间参考，保留当时世界位置及自身实际速度。</summary>
    internal void DetachReference()
    {
        if (_reference is null) { ParentVNode = null; return; }
        // 父对象注销前取得最终位置，避免释放后访问Godot对象。
        Vector2 world = WorldPosition;
        if (_reference is VNode node) node._followers.Remove(this);
        _reference = null;
        GlobalPosition = world;
        ParentVNode = null;
    }

    /// <summary>原子设置外部方向，按外部速度重建实际速度。</summary>
    /// <param name="angleRadians">有限弧度，0向右且顺时针为正。</param>
    public void SetDirection(double angleRadians) => ApplyParameters(new ParameterActionAttribute { Angle = angleRadians });

    /// <summary>原子设置外部速度，加速度配置保留。</summary>
    /// <param name="speed">有符号有限速度，逻辑像素每秒。</param>
    public void SetSpeed(double speed) => ApplyParameters(new ParameterActionAttribute { Speed = speed });

    /// <summary>从同一旧状态求值，再一次性应用运行参数。</summary>
    /// <param name="parameters">空字段保持原值，位置单位为逻辑像素。</param>
    public void ApplyParameters(ParameterActionAttribute parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        // 动态瞄准在坐标修改前读取当前世界状态。
        Vector2 oldWorld = WorldPosition;
        double angle = parameters.AngleSource == "AimPlayer"
            ? VMath.GetAngleBetween2Points(oldWorld, GlobalEvent.GetPlayer().GlobalPosition) + (parameters.Angle ?? 0)
            : parameters.Angle ?? AngleRadians;
        double speed = parameters.Speed ?? Speed;
        bool rebuild = parameters.Angle.HasValue || parameters.AngleSource == "AimPlayer" || parameters.Speed.HasValue;
        // 先验证完整目标值，失败不能留下部分更新。
        var move = new BulletMoveAttribute
        {
            Angle = angle,
            Speed = speed,
            AAngle = parameters.AAngle ?? AAngle,
            ASpeed = parameters.ASpeed ?? ASpeed
        };
        Vector2 coordinates = _reference is null ? oldWorld : _offset;
        coordinates = new Vector2((float)(parameters.X ?? coordinates.X), (float)(parameters.Y ?? coordinates.Y));
        Vector2 world = _reference is null ? coordinates : ReferencePosition(_reference) + coordinates;
        double? life = parameters.LifeTimeS ?? (double.IsPositiveInfinity(LifetimeSeconds) ? null : LifetimeSeconds);
        ValidateMotion(world, move, life);
        AngleRadians = VMath.StandardizationAngle(move.Angle);
        Speed = move.Speed;
        AAngle = VMath.StandardizationAngle(move.AAngle);
        ASpeed = move.ASpeed;
        AAngleIsSameAsAngle = parameters.AAngleIsSameAsAngle ?? AAngleIsSameAsAngle;
        LifetimeSeconds = life ?? double.PositiveInfinity;
        if (_reference is not null) _offset = coordinates;
        GlobalPosition = world;
        if (rebuild) RebuildVelocity();
        SyncFollowers();
    }

    /// <summary>由管理层推进运动和年龄，注销由对应管理层执行。</summary>
    /// <param name="delta">非负秒数，正式战斗固定为1/60秒。</param>
    internal void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        GlobalPosition = WorldPosition;
        if (ASpeed != 0 && delta != 0)
        {
            // 先累计加速度，再移动；父对象平移不进入实际速度。
            Vector2 acceleration = VMath.PolarMove(Vector2.Zero, AAngleIsSameAsAngle ? AngleRadians : AAngle, ASpeed);
            Vector2 next = _velocity + acceleration * (float)delta;
            if (!next.IsFinite()) throw new OverflowException("实际速度超出有限范围。");
            _velocity = next;
            if (_velocity != Vector2.Zero) _actualAngle = VMath.GetAngleBetween2Points(Vector2.Zero, _velocity);
            OnDirectionChanged(_actualAngle);
        }
        Behavior.Advance(this, delta);
        if (!Position.IsFinite()) throw new OverflowException("运行位置超出有限范围。");
        if (_reference is not null) _offset = GlobalPosition - ReferencePosition(_reference);
        Timeline?.AdvanceUnits(VTimeline.SecondsToUnits(delta));
        Age = Timeline is null ? Age + delta : Timeline.ElapsedUnits / (double)VTimerProcessor.UnitsPerSecond;
    }

    /// <summary>逻辑注销时解除跟随引用、时间线及所属队列。</summary>
    internal void Deactivate()
    {
        if (!IsAlive) return;
        // 先保存跟随者的位置，再取消本对象与所有登记引用。
        foreach (var follower in _followers.ToArray()) follower.DetachReference();
        DetachReference();
        GlobalEvent.TryNotifyTargetDestroyed(this);
        Timeline?.Cancel();
        Timeline = null;
        IsAlive = false;
        Queue?.Unregister(this);
        Queue = null;
        ParentVNode = null;
    }

    /// <summary>实际方向变化的专用扩展，纯节点无显示处理。</summary>
    /// <param name="angle">实际方向弧度，零速保留上一方向。</param>
    protected virtual void OnDirectionChanged(double angle) { }

    /// <summary>关闭节点自行推进，统一由战斗固定步更新。</summary>
    public override void _Ready() => SetPhysicsProcess(false);

    /// <summary>外部场景释放时同步解除逻辑引用。</summary>
    public override void _ExitTree()
    {
        if (!IsAlive) return;
        if (this is not Bullet && Emitter is not null) Emitter.ReleaseNode(this, false);
        else Deactivate();
    }

    /// <summary>按外部参数重建实际速度并通知显示层。</summary>
    private void RebuildVelocity()
    {
        _velocity = VMath.PolarMove(Vector2.Zero, AngleRadians, Speed);
        if (Speed != 0) _actualAngle = VMath.StandardizationAngle(AngleRadians + (Speed < 0 ? Math.PI : 0));
        OnDirectionChanged(_actualAngle);
    }

    /// <summary>在参数动作后立即同步子引用的场景坐标。</summary>
    private void SyncFollowers()
    {
        foreach (var follower in _followers)
        {
            follower.GlobalPosition = follower.WorldPosition;
            follower.SyncFollowers();
        }
    }

    /// <summary>读取参考对象位置，VNode递归解析自身逻辑参考。</summary>
    /// <param name="reference">仍有效的参考对象。</param>
    /// <returns>全局逻辑像素坐标。</returns>
    private static Vector2 ReferencePosition(Node2D reference)
        => reference is VNode node ? node.WorldPosition : reference.GlobalPosition;
}
