using Godot;
using System;
using System.Collections.Generic;

/// <summary>弹幕所属阵营，用于筛选伤害目标。</summary>
public enum BulletTeam
{
    // Boss 发出的敌弹。
    Enemy,
    // 玩家发出的攻击弹。
    Player
}

/// <summary>构造单次发射批次并保存仍有效的子弹引用，不负责时序或运动。</summary>
public abstract class BulletEmitter
{
    // 本批次仍在管理器中存活的子弹。
    private readonly List<Bullet> _bullets = new();
    // 阻止同一个批次重复发射。
    private bool _emitted;
    /// <summary>本批次活动子弹的只读视图，注销时同步更新。</summary>
    public IReadOnlyList<Bullet> Bullets { get; }
    /// <summary>建立不可从外部增删的批次视图。</summary>
    protected BulletEmitter() => Bullets = _bullets.AsReadOnly();
    /// <summary>执行一次构造；重复调用抛错，容量不足时只保留成功登记的子弹。</summary>
    /// <param name="manager">拥有节点及生命周期的管理器。</param>
    /// <param name="origin">本批次全局起点，单位为逻辑像素，右和下为正。</param>
    public void Emit(BulletManager manager, Vector2 origin)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (!origin.IsFinite()) throw new ArgumentOutOfRangeException(nameof(origin));
        if (_emitted) throw new InvalidOperationException("发射批次不可重复执行。");
        _emitted = true;
        Build(manager, origin);
    }
    /// <summary>由具体批次逐颗定义参数，可选用排列模式辅助计算方向。</summary>
    /// <param name="manager">接收初始化子弹的管理器。</param>
    /// <param name="origin">全局起点，单位为逻辑像素。</param>
    protected abstract void Build(BulletManager manager, Vector2 origin);
    /// <summary>检查容量后初始化一颗子弹，并同时登记到管理器和批次。</summary>
    /// <param name="manager">接收节点的管理器。</param>
    /// <param name="data">完整参数，位置使用全局逻辑像素。</param>
    /// <returns>成功登记的实例，容量不足时为空。</returns>
    protected Bullet? AddBullet(BulletManager manager, BulletSpawnData data)
    {
        data.Validate();
        if (!manager.CanSpawn()) return null;
        // 入树前完成初始化，失败时释放尚未托管的节点。
        var bullet = new Bullet();
        try
        {
            bullet.Configure(data);
            manager.Register(bullet, this);
            _bullets.Add(bullet);
            return bullet;
        }
        catch
        {
            bullet.Free();
            throw;
        }
    }
    /// <summary>管理器销毁子弹时注销批次引用。</summary>
    /// <param name="bullet">即将释放的子弹。</param>
    internal void Unregister(Bullet bullet) => _bullets.Remove(bullet);
}

/// <summary>按完整参数构造单颗子弹的发射批次。</summary>
public sealed class SingleBulletEmitter : BulletEmitter
{
    // 本次发射的参数快照，位置由 Emit 的全局起点覆盖。
    private readonly BulletSpawnData _data;
    /// <summary>保存单颗子弹的初始化参数。</summary>
    /// <param name="data">单颗参数，发射位置由 Emit 提供。</param>
    public SingleBulletEmitter(BulletSpawnData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data with { };
    }
    /// <summary>在指定位置构造唯一子弹。</summary>
    /// <param name="manager">接收子弹的管理器。</param>
    /// <param name="origin">全局起点，单位为逻辑像素。</param>
    protected override void Build(BulletManager manager, Vector2 origin) => AddBullet(manager, _data with { Position = origin });
}
