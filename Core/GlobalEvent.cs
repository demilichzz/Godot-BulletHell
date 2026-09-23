using Godot;
using System;

/// <summary>提供当前战斗实体与共享服务的唯一全局访问入口。</summary>
public static class GlobalEvent
{
    // 场景切换成功后提交的持久战斗绑定。
    private static BattleManager? _current;
    // 初始化、清理和固定步推进期间的临时绑定，允许嵌套恢复。
    private static BattleManager? _context;

    /// <summary>绑定一场新的持久当前战斗。</summary>
    /// <param name="battle">已完成初始化的战斗管理器。</param>
    internal static void BindCurrent(BattleManager battle) => _current = battle ?? throw new ArgumentNullException(nameof(battle));
    /// <summary>仅当持久绑定属于指定战斗时解除绑定。</summary>
    /// <param name="battle">请求解除绑定的战斗管理器。</param>
    internal static void ClearCurrent(BattleManager battle) { if (ReferenceEquals(_current, battle)) _current = null; }
    /// <summary>读取当前持久绑定，供场景切换失败时保存和恢复。</summary>
    /// <returns>当前战斗；没有绑定时为空。</returns>
    internal static BattleManager? CaptureCurrent() => _current;
    /// <summary>建立临时战斗上下文，释放时恢复进入前的上下文。</summary>
    /// <param name="battle">本次操作所属战斗。</param>
    /// <returns>可释放的嵌套上下文作用域。</returns>
    internal static Scope UseBattle(BattleManager battle) { ArgumentNullException.ThrowIfNull(battle); return new Scope(battle, _context); }
    /// <summary>判断战斗是否仍可作为场景绑定恢复。</summary>
    /// <param name="battle">待检查战斗。</param>
    /// <returns>实例存在且未停止时为真。</returns>
    internal static bool IsValidBattle(BattleManager? battle) => battle is not null && !battle.IsStopped && GodotObject.IsInstanceValid(battle);

    /// <summary>注册到当前战斗的共享计时器处理器。</summary>
    /// <param name="timer">尚未注册的计时器。</param>
    /// <returns>已注册的同一个计时器。</returns>
    public static VTimer RegisterTimer(VTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        var battle = RequireBattle();
        if (battle.State != BattleState.Running) throw new InvalidOperationException("当前战斗未处于运行状态，不能注册计时器。");
        return battle.Timers.Register(timer);
    }
    /// <summary>取得当前 Boss。</summary>
    /// <returns>当前战斗中的 Boss 实体。</returns>
    public static BossController GetBoss() => RequireBattle().Boss;
    /// <summary>取得当前玩家。</summary>
    /// <returns>当前战斗中的玩家实体。</returns>
    public static PlayerController GetPlayer() => RequireBattle().Player;
    /// <summary>取得当前弹幕管理器。</summary>
    /// <returns>当前战斗中的弹幕容器。</returns>
    public static BulletManager GetBulletManager() => RequireBattle().Bullets;

    /// <summary>向所属战斗处理器通知目标失效，避免旧节点污染新战斗。</summary>
    /// <param name="node">已失效的战斗节点。</param>
    internal static void TryNotifyTargetDestroyed(Node2D node)
    {
        var battle = ResolveBattle();
        if (battle is null || battle.IsStopped || !OwnsNode(battle, node)) return;
        battle.Timers.NotifyTargetDestroyed(node);
    }
    /// <summary>当前绑定战斗是否仍可接受业务活动。</summary>
    internal static bool HasActiveBattle => IsValidBattle(ResolveBattle());

    /// <summary>临时切换全局战斗上下文的可释放作用域。</summary>
    internal sealed class Scope : IDisposable
    {
        private readonly BattleManager? _previous;
        private bool _disposed;
        /// <summary>保存旧上下文并切换到指定战斗。</summary>
        /// <param name="battle">临时上下文战斗。</param>
        /// <param name="previous">进入前的上下文。</param>
        internal Scope(BattleManager battle, BattleManager? previous) { _previous = previous; _context = battle; }
        /// <summary>恢复进入作用域前的上下文。</summary>
        public void Dispose() { if (_disposed) return; _disposed = true; _context = _previous; }
    }

    /// <summary>解析临时上下文或持久绑定。</summary>
    /// <returns>当前可用战斗。</returns>
    private static BattleManager? ResolveBattle() => _context ?? _current;
    /// <summary>取得有效战斗并验证实体仍在树中。</summary>
    /// <returns>当前活动战斗管理器。</returns>
    private static BattleManager RequireBattle()
    {
        var battle = ResolveBattle();
        if (battle is null || battle.IsStopped || battle.Boss is null || battle.Player is null || battle.Bullets is null
            || !IsLiveNode(battle.Boss) || !IsLiveNode(battle.Player) || !IsLiveNode(battle.Bullets)) throw new InvalidOperationException("当前没有有效战斗。");
        return battle;
    }
    /// <summary>检查节点是否仍在树中且未排队释放。</summary>
    /// <param name="node">待检查节点。</param>
    /// <returns>节点仍可访问时为真。</returns>
    private static bool IsLiveNode(Node2D node) => GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion() && node.IsInsideTree();
    /// <summary>确认节点属于指定战斗的实体或弹幕容器。</summary>
    /// <param name="battle">目标战斗。</param>
    /// <param name="node">待确认节点。</param>
    /// <returns>节点属于该战斗时为真。</returns>
    private static bool OwnsNode(BattleManager battle, Node2D node)
    {
        if (ReferenceEquals(node, battle.Boss) || ReferenceEquals(node, battle.Player)) return true;
        var bullets = battle.Bullets;
        return IsLiveNode(bullets) && (ReferenceEquals(node, bullets) || bullets.IsAncestorOf(node));
    }
}
