using Godot;
using System;

/// <summary>一次场景进入所需的参数，独立于场景节点生命周期。</summary>
public sealed class StageContext
{
    /// <summary>提供切换入口和共享选择数据的常驻管理器。</summary>
    public GameManager Game { get; }
    /// <summary>本次战斗 Boss 配置，非战斗场景可为空。</summary>
    public BossData? Boss { get; }
    /// <summary>构造场景进入参数。</summary>
    /// <param name="game">当前游戏管理器。</param>
    /// <param name="boss">可选 Boss 配置，默认空。</param>
    public StageContext(GameManager game, BossData? boss = null) { Game = game; Boss = boss; }
}
/// <summary>游戏内场景的统一生命周期；同一时刻只激活一个实例。</summary>
public abstract partial class Stage : Node
{
    /// <summary>可用于注册和切换的场景标识。</summary>
    public abstract string StageId { get; }
    /// <summary>是否处于进入后、退出前的活动状态。</summary>
    public bool IsActive { get; private set; }
    /// <summary>当前进入参数，仅在进入后可用。</summary>
    protected StageContext Context { get; private set; } = null!;
    /// <summary>新实例默认禁止更新和输入，等待管理器完成进入。</summary>
    protected Stage() => ProcessMode = ProcessModeEnum.Disabled;
    /// <summary>节点准备好后进入场景；重复进入会被拒绝。</summary>
    /// <param name="context">当前进入参数。</param>
    public void Enter(StageContext context)
    {
        if (IsActive || !IsNodeReady()) throw new InvalidOperationException("场景须就绪且尚未激活。");
        Context = context;
        IsActive = true;
        OnEnter(context);
        ProcessMode = ProcessModeEnum.Inherit;
    }
    /// <summary>停止输入与更新后退出场景；重复调用不重复清理。</summary>
    public void Exit()
    {
        ProcessMode = ProcessModeEnum.Disabled;
        if (!IsActive) return;
        IsActive = false;
        OnExit();
    }
    /// <summary>执行该场景的一次进入流程。</summary>
    /// <param name="context">当前进入参数。</param>
    protected abstract void OnEnter(StageContext context);
    /// <summary>取消订阅并停止场景活动。</summary>
    protected abstract void OnExit();
    /// <summary>节点被外部移除时也执行幂等退出。</summary>
    public override void _ExitTree() => Exit();
}
