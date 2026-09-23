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

/// <summary>由Build定义自身时间线，并保存仍有效的子弹引用。</summary>
public abstract class BulletEmitter : IVTimelineOwner
{
    // 本发射器仍在管理器中存活的子弹。
    private readonly List<Bullet> _bullets = new();
    /// <summary>发射器激活时创建的逻辑时间线。</summary>
    public VTimeline? Timeline { get; private set; }
    /// <summary>所有仍存活的已发子弹的只读视图。</summary>
    public IReadOnlyList<Bullet> Bullets { get; }
    /// <summary>建立不可从外部增删的子弹视图。</summary>
    protected BulletEmitter() => Bullets = _bullets.AsReadOnly();
    /// <summary>创建实体时间线并由Build登记发射动作。</summary>
    /// <param name="owner">提供每次发射位置的Boss。</param>
    /// <param name="manager">本战斗的弹幕容器。</param>
    public void Start(BossController owner, BulletManager manager)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(manager);
        if (Timeline is not null) throw new InvalidOperationException("发射器已启动。");
        Timeline = GlobalEvent.CreateTimeline(this);
        try { Build(manager, owner); }
        catch
        {
            Timeline.Cancel();
            Timeline = null;
            throw;
        }
    }
    /// <summary>停止未来发射，不移除已产生的子弹。</summary>
    public void Stop()
    {
        Timeline?.Cancel();
        Timeline = null;
    }
    /// <summary>由所属阶段按一个逻辑更新累计发射器年龄。</summary>
    /// <param name="units">本次推进的非负整数逻辑时间单位。</param>
    internal void AdvanceUnits(long units) => Timeline?.AdvanceUnits(units);
    /// <summary>登记发射时间线，回调中按队列增量或逐颗生成弹幕。</summary>
    /// <param name="manager">接收初始化子弹的管理器。</param>
    /// <param name="owner">提供各次发射时全局坐标的Boss。</param>
    protected abstract void Build(BulletManager manager, BossController owner);
    /// <summary>检查容量后初始化一颗子弹，并同时登记到管理器和发射器。</summary>
    /// <param name="manager">接收节点的管理器。</param>
    /// <param name="settings">完整参数，位置使用全局逻辑像素。</param>
    /// <returns>成功登记的实例，容量不足时为空。</returns>
    protected Bullet? AddBullet(BulletManager manager, BulletDefaultSet settings)
    {
        // 节点初始化、贴图与容量检查统一交给弹幕管理器和Bullet。
        Bullet? bullet = manager.Spawn(settings, this);
        if (bullet is not null) _bullets.Add(bullet);
        return bullet;
    }
    /// <summary>按队列的递增规则逐颗登记子弹。</summary>
    /// <param name="manager">拥有子弹生命周期的管理器。</param>
    /// <param name="queue">待生成的弹幕队列。</param>
    protected void AddQueue(BulletManager manager, BulletQueue queue) => queue.Emit(this, manager);

    /// <summary>供队列使用的单颗登记入口。</summary>
    /// <param name="manager">弹幕容器。</param>
    /// <param name="settings">完整单颗参数。</param>
    /// <returns>生成的子弹，容量不足时为空。</returns>
    internal Bullet? AddQueueBullet(BulletManager manager, BulletDefaultSet settings) => AddBullet(manager, settings);
    /// <summary>管理器销毁子弹时注销发射器引用。</summary>
    /// <param name="bullet">即将释放的子弹。</param>
    internal void Unregister(Bullet bullet) => _bullets.Remove(bullet);
}
