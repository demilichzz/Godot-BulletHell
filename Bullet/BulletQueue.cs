using Godot;
using System;
using System.Collections.Generic;

/// <summary>提供单颗弹幕与队列共同使用的方向和速度操作。</summary>
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

/// <summary>按位置、方向和速度增量生成一组弹幕，并跟踪其中存活的成员。</summary>
public sealed class BulletQueue
{
    // 首颗弹幕参数。
    private readonly BulletDefaultSet _first;
    private readonly List<Bullet> _bullets = new();
    // 复用同一只读包装，随存活列表变化自动更新内容。
    private readonly IReadOnlyList<Bullet> _bulletView;
    private bool _emitted;
    /// <summary>首颗弹幕的全局参考位置，单位为逻辑像素。</summary>
    public Vector2 Source { get; }
    /// <summary>本次生成的不可变数量与增量配置。</summary>
    public BulletQueueSet Settings { get; }
    /// <summary>仍存活且成功登记的弹幕。</summary>
    public IReadOnlyList<Bullet> BulletList => _bulletView;

    /// <summary>保存队列初值和每颗的固定增量。</summary>
    /// <param name="source">第一颗弹幕的全局坐标，逻辑像素。</param>
    /// <param name="first">第一颗的完整参数；位置由source覆盖，角度为弧度。</param>
    /// <param name="settings">数量和逐颗增量配置，角度为弧度。</param>
    public BulletQueue(Vector2 source, BulletDefaultSet first, BulletQueueSet settings)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(settings);
        if (!source.IsFinite() || settings.Amount <= 0 || !double.IsFinite(settings.XAdd)
            || !double.IsFinite(settings.YAdd) || !double.IsFinite(settings.AngleAdd)
            || !double.IsFinite(settings.SpeedAdd))
            throw new ArgumentOutOfRangeException(nameof(settings), "队列参数无效。");
        // 只预检最后一颗的增量终值，逐颗仍由Bullet校验完整参数。
        double lastX = source.X + settings.XAdd * (settings.Amount - 1);
        double lastY = source.Y + settings.YAdd * (settings.Amount - 1);
        double speed = first.Speed + settings.SpeedAdd * (settings.Amount - 1);
        double angle = first.AngleRadians + settings.AngleAdd * (settings.Amount - 1);
        if (!double.IsFinite(lastX) || Math.Abs(lastX) > float.MaxValue
            || !double.IsFinite(lastY) || Math.Abs(lastY) > float.MaxValue
            || !double.IsFinite(speed) || Math.Abs(speed) > float.MaxValue || !double.IsFinite(angle))
            throw new ArgumentOutOfRangeException(nameof(settings), "队列终值无效。");
        Source = source;
        _first = first;
        Settings = settings;
        _bulletView = _bullets.AsReadOnly();
    }

    /// <summary>由发射器逐颗登记队列；容量满时保留已成功生成的子弹。</summary>
    /// <param name="emitter">执行登记的发射器。</param>
    /// <param name="manager">拥有子弹生命周期的管理器。</param>
    internal void Emit(BulletEmitter emitter, BulletManager manager)
    {
        if (_emitted) throw new InvalidOperationException("同一队列只能生成一次。");
        _emitted = true;
        // 索引从0开始，第一颗使用原始参数。
        for (int index = 0; index < Settings.Amount; index++)
        {
            // 本颗的位置、角度与速度快照。
            var settings = _first with
            {
                Position = new Vector2((float)(Source.X + Settings.XAdd * index), (float)(Source.Y + Settings.YAdd * index)),
                AngleRadians = _first.AngleRadians + Settings.AngleAdd * index,
                Speed = _first.Speed + Settings.SpeedAdd * index
            };
            // 仅成功登记的子弹加入存活队列。
            Bullet? bullet = emitter.AddQueueBullet(manager, settings);
            if (bullet is null) break;
            bullet.Queue = this;
            _bullets.Add(bullet);
        }
    }

    /// <summary>对全部仍存活子弹设置相同方向。</summary>
    /// <param name="angleRadians">有限弧度角，0向右且顺时针为正。</param>
    public void SetDirection(double angleRadians)
    {
        if (!double.IsFinite(angleRadians)) throw new ArgumentOutOfRangeException(nameof(angleRadians));
        foreach (var bullet in _bullets) bullet.SetDirection(angleRadians);
    }

    /// <summary>对全部仍存活子弹设置相同速度。</summary>
    /// <param name="speed">有符号有限速度，逻辑像素每秒；负值反向。</param>
    public void SetSpeed(double speed)
    {
        if (!double.IsFinite(speed) || Math.Abs(speed) > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(speed));
        foreach (var bullet in _bullets) bullet.SetSpeed(speed);
    }

    /// <summary>在弹幕释放时移除队列成员引用。</summary>
    /// <param name="bullet">已释放的队列成员。</param>
    internal void Unregister(Bullet bullet) => _bullets.Remove(bullet);
}
