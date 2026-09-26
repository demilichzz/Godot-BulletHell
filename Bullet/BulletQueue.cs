using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>不可变的弹幕队列数量与逐颗增量配置，支持with派生。</summary>
public sealed record BulletQueueSet
{
    /// <summary>请求生成的正整数弹幕数量，默认一颗。</summary>
    public int Amount { get; init; } = 1;
    /// <summary>相邻弹幕的X位置增量，逻辑像素。</summary>
    public double XAdd { get; init; }
    /// <summary>相邻弹幕的Y位置增量，逻辑像素。</summary>
    public double YAdd { get; init; }
    /// <summary>相邻弹幕的方向增量，弧度。</summary>
    public double AngleAdd { get; init; }
    /// <summary>相邻弹幕的速度增量，逻辑像素每秒。</summary>
    public double SpeedAdd { get; init; }
}

/// <summary>在共用VNodeQueue基础上增加子弹创建和旧代码队列兼容入口。</summary>
public sealed partial class BulletQueue : VNodeQueue
{
    /// <summary>包含碰撞半径和显示倍率的子弹核心参数。</summary>
    public new BulletCoreAttribute Core => (BulletCoreAttribute)base.Core;
    /// <summary>子弹专用显示参数。</summary>
    public BulletDisplayAttribute Display { get; private set; } = new();
    /// <summary>旧代码入口的数量和增量配置。</summary>
    public BulletQueueSet Settings { get; } = new();
    /// <summary>共用成员存储的子弹类型视图。</summary>
    public IReadOnlyList<Bullet> BulletList { get; }
    /// <summary>子弹默认在出生后独立运动。</summary>
    protected override bool DefaultFollow => false;
    // 旧代码入口首颗参数；数据队列为空。
    private readonly BulletDefaultSet? _first;
    // 数据队列校验后的显示和出生参数模板。
    private BulletDefaultSet? _template;

    /// <summary>建立子弹核心默认值和不复制成员的兼容视图。</summary>
    public BulletQueue()
    {
        base.Core = new BulletCoreAttribute();
        BaseAttributes = new BulletMoveAttribute { Speed = 180 };
        BulletList = new BulletView(Members);
    }

    /// <summary>保留旧代码的首颗参数和固定增量构造方式。</summary>
    /// <param name="source">全局参考位置，逻辑像素。</param>
    /// <param name="first">第一颗完整出生参数。</param>
    /// <param name="settings">数量、位置及运动增量。</param>
    public BulletQueue(Vector2 source, BulletDefaultSet first, BulletQueueSet settings) : this()
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(settings);
        if (!source.IsFinite() || settings.Amount <= 0 || !double.IsFinite(settings.XAdd)
            || !double.IsFinite(settings.YAdd) || !double.IsFinite(settings.AngleAdd) || !double.IsFinite(settings.SpeedAdd))
            throw new ArgumentOutOfRangeException(nameof(settings), "队列参数无效。");
        // 旧入口继续预检末项，保留原有限范围和随机行为。
        double x = source.X + settings.XAdd * (settings.Amount - 1);
        double y = source.Y + settings.YAdd * (settings.Amount - 1);
        double speed = first.Speed + settings.SpeedAdd * (settings.Amount - 1);
        double angle = first.AngleRadians + settings.AngleAdd * (settings.Amount - 1);
        if (!double.IsFinite(x) || Math.Abs(x) > float.MaxValue || !double.IsFinite(y) || Math.Abs(y) > float.MaxValue
            || !double.IsFinite(speed) || Math.Abs(speed) > float.MaxValue || !double.IsFinite(angle))
            throw new ArgumentOutOfRangeException(nameof(settings), "队列终值无效。");
        Source = source;
        _first = first;
        Settings = settings;
        EnableLegacyInstance();
    }

    /// <summary>创建当前子弹队列的独立成员批次。</summary>
    /// <param name="source">有限全局参考位置，逻辑像素。</param>
    /// <returns>未发射的子弹队列。</returns>
    public override BulletQueue CreateInstance(Vector2 source)
    {
        if (_first is not null) throw new InvalidOperationException("旧代码实例不能作为数据模板。");
        return (BulletQueue)base.CreateInstance(source);
    }

    /// <summary>复制子弹专用只读配置到新的空批次。</summary>
    /// <returns>新的子弹队列。</returns>
    protected override VNodeQueue NewInstance() => new BulletQueue { Display = Display, _template = _template };

    /// <summary>检查子弹容量，节点基类不使用此限制。</summary>
    /// <param name="manager">当前子弹管理器。</param>
    /// <returns>容量足够时为真。</returns>
    protected override bool CanCreate(BulletManager manager) => manager.CanSpawn();

    /// <summary>将共用求值结果转换为实际子弹出生参数。</summary>
    /// <param name="emitter">提供共通阵营和伤害的发射器。</param>
    /// <param name="manager">接收子弹的管理器。</param>
    /// <param name="position">全局逻辑像素位置。</param>
    /// <param name="move">已求值的运动参数。</param>
    /// <returns>成功生成的子弹，容量不足时为空。</returns>
    protected override VNode? CreateMember(BulletEmitter emitter, BulletManager manager, Vector2 position, BulletMoveAttribute move)
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
        return emitter.AddQueueBullet(manager, settings);
    }

    /// <summary>数据入口使用共用求值，旧入口保留原直接增量方式。</summary>
    /// <param name="emitter">登记成员的发射器。</param>
    /// <param name="manager">子弹管理器。</param>
    internal override void Emit(BulletEmitter emitter, BulletManager manager)
    {
        if (_first is null) { base.Emit(emitter, manager); return; }
        BeginEmit();
        for (int index = 0; index < Settings.Amount; index++)
        {
            // 原有代码接口仍保留完整出生参数的阵营和伤害。
            var settings = _first with
            {
                Position = new Vector2((float)(Source.X + Settings.XAdd * index), (float)(Source.Y + Settings.YAdd * index)),
                AngleRadians = _first.AngleRadians + Settings.AngleAdd * index,
                Speed = _first.Speed + Settings.SpeedAdd * index
            };
            Bullet? bullet = emitter.AddQueueBullet(manager, settings);
            if (bullet is null) break;
            Track(bullet, index);
        }
    }

    /// <summary>将唯一VNode成员列表投影为兼容的只读子弹视图。</summary>
    private sealed class BulletView : IReadOnlyList<Bullet>
    {
        // 同一存储的只读引用，不另存一份成员。
        private readonly IReadOnlyList<VNode> _source;
        /// <summary>绑定共用成员视图。</summary>
        /// <param name="source">仅包含Bullet的共用成员视图。</param>
        public BulletView(IReadOnlyList<VNode> source) => _source = source;
        /// <summary>当前存活成员数量。</summary>
        public int Count => _source.Count;
        /// <summary>取得指定成员。</summary>
        /// <param name="index">当前列表零基索引。</param>
        public Bullet this[int index] => (Bullet)_source[index];
        /// <summary>按当前列表顺序枚举成员。</summary>
        /// <returns>子弹枚举器。</returns>
        public IEnumerator<Bullet> GetEnumerator()
        {
            foreach (var member in _source) yield return (Bullet)member;
        }
        /// <summary>提供非泛型兼容枚举。</summary>
        /// <returns>成员枚举器。</returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
